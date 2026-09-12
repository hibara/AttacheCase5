//----------------------------------------------------------------------
// SPDX-License-Identifier: LicenseRef-AttacheCase-SourceAvailable-1.0
// Copyright (c) 2016-2026 HiBARA Software, LLC
// Released under the AttacheCase Source Available License Version 1.0 (LICENSE.md).
// This is NOT an open source license. Business use requires a separate Commercial,
// Integration or OEM License (COMMERCIAL-LICENSE.md).
// Third-party components remain under their own licenses (THIRD_PARTY_NOTICES.md).
// AttacheCase Source Available License Version 1.0 に基づき公開しています。
// オープンソースライセンスではありません。業務・商用利用には別途
// Commercial / Integration / OEM License が必要です（詳細: LICENSE.md / COMMERCIAL-LICENSE.md）。
//
// 処理後の元ファイル削除（/del, /delenc）。
//   1: 通常削除
//   2: ごみ箱へ移動（NSFileManager trashItemAtURL:。失敗時は ~/.Trash へ移動）
//   3: 完全削除（ランダム値とゼロで上書きしてから削除）
//
// 注意: APFS / SSD ではコピーオンライトやウェアレベリングにより、上書きが
// 物理的な消去を保証しない。Windows 版と同じ手順を踏むが、その点は同じ。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AttacheCase.Cli;

public static class FileEraser
{
    private const int BufferSize = 64 * 1024;

    /// <summary>削除方法</summary>
    public enum Mode
    {
        None = 0,
        Delete = 1,
        Trash = 2,
        Wipe = 3,
    }

    /// <summary>
    /// 指定されたパスを削除する。
    /// </summary>
    /// <returns>削除できたら true。対象が存在しない場合も true を返す。</returns>
    public static bool Erase(string path, Mode mode, int randomTimes = 1, int zeroTimes = 1)
    {
        if (mode == Mode.None) return true;

        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path)) return true;

        // 対象そのものがシンボリックリンクのときは、リンク先に触れずリンクだけを消す。
        // Directory.Exists / File.Exists はリンク先を見て true を返すため、ここで弾かないと
        // Wipe がリンク先の中身を上書きし、Delete が Directory.Delete(recursive) で
        // リンク先ごと消してしまう（どちらも実測で確認）。
        // ゴミ箱へ移動する場合はリンク自体が移動するだけなので、そのまま通す。
        if (mode != Mode.Trash)
        {
            FileSystemInfo self = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
            if (self.LinkTarget != null)
            {
                try
                {
                    self.Delete();
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        try
        {
            switch (mode)
            {
                case Mode.Trash:
                    return MoveToTrash(path);

                case Mode.Wipe:
                    if (isDirectory)
                    {
                        // シンボリックリンクは辿らない。辿るとフォルダーの外にある実体を
                        // 上書きしてしまい、復元できない
                        var options = new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            AttributesToSkip = FileAttributes.ReparsePoint,
                            IgnoreInaccessible = true,
                        };

                        var failed = 0;
                        foreach (var file in Directory.EnumerateFiles(path, "*", options))
                        {
                            try
                            {
                                WipeFile(file, randomTimes, zeroTimes);
                            }
                            catch (Exception ex)
                            {
                                // 1 件失敗しても残りは消す。どれが残ったかは呼び出し側に伝わらないので、
                                // ここで記録しておく
                                failed++;
                                Console.Error.WriteLine($"Wipe failed: {file}: {ex.Message}");
                            }
                        }

                        // 一部でも消し損ねたらフォルダーごと消さない（何が残ったか分かるようにする）
                        if (failed > 0) return false;

                        Directory.Delete(path, true);
                    }
                    else
                    {
                        WipeFile(path, randomTimes, zeroTimes);
                    }
                    return true;

                case Mode.Delete:
                default:
                    if (isDirectory)
                    {
                        Directory.Delete(path, true);
                    }
                    else
                    {
                        File.Delete(path);
                    }
                    return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// ファイルの中身をランダム値とゼロで上書きしてから削除する。
    /// </summary>
    private static void WipeFile(string filePath, int randomTimes, int zeroTimes)
    {
        var info = new FileInfo(filePath);

        // シンボリックリンクの中身を上書きすると、リンク先にある別のファイルを壊す。
        // リンクそのものだけを消す
        if (info.LinkTarget != null)
        {
            info.Delete();
            return;
        }

        if (info.IsReadOnly) info.IsReadOnly = false;

        var length = info.Length;

        if (length > 0)
        {
            var buffer = new byte[BufferSize];

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                for (var pass = 0; pass < randomTimes; pass++)
                {
                    RandomNumberGenerator.Fill(buffer);
                    OverwriteWholeFile(fs, buffer, length);
                }

                Array.Clear(buffer);
                for (var pass = 0; pass < zeroTimes; pass++)
                {
                    OverwriteWholeFile(fs, buffer, length);
                }
            }
        }

        File.Delete(filePath);
    }

    private static void OverwriteWholeFile(FileStream fs, byte[] buffer, long length)
    {
        fs.Seek(0, SeekOrigin.Begin);

        var remaining = length;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            fs.Write(buffer, 0, count);
            remaining -= count;
        }

        fs.Flush(true);
    }

    //======================================================================
    // ごみ箱へ移動
    //======================================================================

    /// <summary>
    /// ファイル／フォルダーをごみ箱へ移動する。
    /// NSFileManager の trashItemAtURL: を使い、失敗した場合は ~/.Trash への
    /// 移動にフォールバックする。
    /// </summary>
    private static bool MoveToTrash(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows のごみ箱は Shell API 経由。使えない場所（UNC・リムーバブル等）では
            // 完全削除にフォールバックされてしまうため、事前判定で断る
            if (!Platform.WindowsRecycleBin.CanUseRecycleBin(path, out var reason))
            {
                Console.Error.WriteLine($"Cannot use the Recycle Bin for this path ({reason}): {path}");
                return false;
            }

            if (!Platform.WindowsRecycleBin.Send(new[] { path }, out var error))
            {
                Console.Error.WriteLine($"Failed to move to the Recycle Bin: {error}");
                return false;
            }

            return true;
        }

        if (OperatingSystem.IsMacOS() && TrashViaFileManager(path))
        {
            return true;
        }

        return MoveIntoTrashFolder(path);
    }

    private static bool TrashViaFileManager(string path)
    {
        // stringWithUTF8String: / fileURLWithPath: は autorelease オブジェクトを
        // 返す。CLI は autorelease プールを張っていないスレッドから呼ぶため、
        // 自前でプールを積まないとリークし、stderr に警告が出る
        var pool = IntPtr.Zero;
        try
        {
            pool = objc_autoreleasePoolPush();
            var nsFileManager = objc_getClass("NSFileManager");
            var nsString = objc_getClass("NSString");
            var nsUrl = objc_getClass("NSURL");
            if (nsFileManager == IntPtr.Zero || nsString == IntPtr.Zero || nsUrl == IntPtr.Zero)
            {
                return false;
            }

            var manager = SendMessage(nsFileManager, sel_registerName("defaultManager"));
            var pathString = SendMessageUtf8(nsString, sel_registerName("stringWithUTF8String:"), path);
            var url = SendMessagePtr(nsUrl, sel_registerName("fileURLWithPath:"), pathString);
            if (manager == IntPtr.Zero || url == IntPtr.Zero) return false;

            var result = SendMessageTrash(
                manager,
                sel_registerName("trashItemAtURL:resultingItemURL:error:"),
                url, IntPtr.Zero, IntPtr.Zero);

            return result != 0;
        }
        catch (Exception)
        {
            // dlopen 失敗や API 変更に備えてフォールバックへ落とす
            return false;
        }
        finally
        {
            if (pool != IntPtr.Zero)
            {
                try { objc_autoreleasePoolPop(pool); }
                catch (Exception) { /* プールを畳めなくても処理結果は変えない */ }
            }
        }
    }

    /// <summary>
    /// ~/.Trash へ移動する（NSFileManager が使えなかった場合のフォールバック）。
    /// 「元に戻す」の記録は行われない。
    /// </summary>
    private static bool MoveIntoTrashFolder(string path)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var trashDir = Path.Combine(home, ".Trash");
            if (!Directory.Exists(trashDir)) return false;

            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            var destination = Path.Combine(trashDir, name);

            // 同名のものがごみ箱にある場合は連番を付ける
            var baseName = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            var index = 1;
            while (File.Exists(destination) || Directory.Exists(destination))
            {
                destination = Path.Combine(trashDir, $"{baseName} {index}{extension}");
                index++;
            }

            if (Directory.Exists(path))
            {
                Directory.Move(path, destination);
            }
            else
            {
                File.Move(path, destination);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    //======================================================================
    // Objective-C ランタイム
    //======================================================================

    private const string LibObjC = "/usr/lib/libobjc.dylib";

    [DllImport(LibObjC)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(LibObjC)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessagePtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageUtf8(
        IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern byte SendMessageTrash(
        IntPtr receiver, IntPtr selector, IntPtr url, IntPtr resultingUrl, IntPtr error);
}
