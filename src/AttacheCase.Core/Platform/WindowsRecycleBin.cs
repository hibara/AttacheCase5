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
//----------------------------------------------------------------------
// ファイル／フォルダーを Windows のごみ箱へ移動する（確認ダイアログなし）。
// Windows 版 AttacheCase.Cli\RecycleBin.cs の移植。
//
// SHFileOperation に FOF_ALLOWUNDO（ごみ箱行き）と FOF_NOCONFIRMATION /
// FOF_SILENT / FOF_NOERRORUI を渡すことで、無人バッチでも一切ダイアログを
// 出さずに実行できる。
//
// 重要な注意:
// ごみ箱が使えない場所（UNC・ネットワークドライブ、リムーバブルメディア、
// ポリシーでごみ箱が無効、ごみ箱の容量上限を超えるサイズ）では、
// FOF_ALLOWUNDO を付けていても Windows は完全削除にフォールバックする。
// FOF_NOCONFIRMATION 併用時は確認も出ないため、「復元できるつもりだったのに
// 完全に消えていた」が起こりうる。そのため CanUseRecycleBin でローカル
// 固定ドライブ上にあることを事前に確認し、満たさない場合は実行せず
// 呼び出し側でエラーにする。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AttacheCase.Platform;

[SupportedOSPlatform("windows")]
public static class WindowsRecycleBin
{
    private const uint FO_DELETE = 0x0003;

    private const ushort FOF_SILENT = 0x0004;          // 進捗ダイアログを出さない
    private const ushort FOF_NOCONFIRMATION = 0x0010;  // 確認ダイアログを出さない
    private const ushort FOF_ALLOWUNDO = 0x0040;       // ごみ箱へ入れる
    private const ushort FOF_NOERRORUI = 0x0400;       // エラーダイアログも出さない

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>
    /// そのパスがごみ箱を使える場所にあるか。
    /// ローカルの固定ドライブのみを許可する。UNC パス、ネットワークドライブ、
    /// リムーバブルメディア、CD-ROM は、ごみ箱に入らず完全削除になるため許可しない。
    /// </summary>
    public static bool CanUseRecycleBin(string path, out string reason)
    {
        reason = "";

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            reason = "network path (UNC)";
            return false;
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            reason = "the drive could not be determined";
            return false;
        }

        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.Fixed)
            {
                reason = "drive type is " + drive.DriveType;
                return false;
            }
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 指定されたパスをごみ箱へ移動する。ダイアログは一切表示しない。
    /// </summary>
    /// <returns>成功したら true</returns>
    public static bool Send(IList<string> paths, out string errorMessage)
    {
        errorMessage = "";
        if (paths == null || paths.Count == 0) return true;

        // pFrom は「\0 区切り + 末尾 \0\0」の形式でなければならない
        var from = string.Join("\0", paths) + "\0\0";

        var op = new SHFILEOPSTRUCT
        {
            hwnd = IntPtr.Zero,
            wFunc = FO_DELETE,
            pFrom = from,
            pTo = null,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            fAnyOperationsAborted = false,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null,
        };

        var result = SHFileOperation(ref op);

        if (result != 0)
        {
            errorMessage = "SHFileOperation failed. (code 0x" + result.ToString("X") + ")";
            return false;
        }

        if (op.fAnyOperationsAborted)
        {
            errorMessage = "The operation was aborted.";
            return false;
        }

        return true;
    }
}
