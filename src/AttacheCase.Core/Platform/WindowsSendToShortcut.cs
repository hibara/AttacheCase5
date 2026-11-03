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
// エクスプローラーの「送る」メニューへアタッシェケースを出し入れする。
//
// 「送る」の実体は利用者ごとのフォルダー（%APPDATA%\Microsoft\Windows\SendTo）に
// 置かれたショートカット（.lnk）で、そこにある項目がそのままメニューに並ぶ。
// AttacheCase 3 までは設定画面に「送るフォルダーに追加」ボタンがあったが
// 5.0 で落ちていた（利用者からの要望で 5.5.1 に復活、2026-10-10）。
//
// 設計（利用者指示）: レジストリにも settings.json にも状態を持たない。
// 「送るフォルダーにショートカットがあればチェック ON、無ければ OFF」で、
// 画面のチェックは常にフォルダーの実態を映す。利用者が手でショートカットを
// 消したり足したりしても、次に設定画面を開けばそのとおりに見える。
//
// ショートカットの中身は exe のパスだけ（引数なし）。「送る」で選ばれた
// ファイルはエクスプローラーが引数として渡し、本体の ApplyCommandLineAsync が
// 先頭ファイルの種類から暗号化か復号かを決める（/en /de を書かなくてよい）。
//
// .lnk の作成は Shell の IShellLinkW + IPersistFile（COM）。ユーザー領域なので
// 昇格（AtcSetup）は要らない。読み取り側（Exists）は .lnk の中身を見ない。
// 名前は日本語 UI なら「アタッシェケース.lnk」、それ以外は「AttacheCase.lnk」
// （AttacheCase 3 と同じ）。存在の判定は両方の名前を見る（言語を切り替えても
// 「入っているのに OFF」にならないように）。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace AttacheCase.Platform;

[SupportedOSPlatform("windows")]
public static class WindowsSendToShortcut
{
    /// <summary>英語など日本語以外の UI で使うショートカット名</summary>
    public const string DefaultFileName = "AttacheCase.lnk";

    /// <summary>日本語 UI で使うショートカット名（AttacheCase 3 と同じ）</summary>
    public const string JapaneseFileName = "アタッシェケース.lnk";

    /// <summary>このアプリが作る（消す）ショートカット名の全部</summary>
    public static readonly IReadOnlyList<string> KnownFileNames = [DefaultFileName, JapaneseFileName];

    /// <summary>「送る」フォルダー（%APPDATA%\Microsoft\Windows\SendTo）。取れなければ空文字。</summary>
    public static string SendToDirectory
    {
        get
        {
            try
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            }
            catch (Exception)
            {
                return "";
            }
        }
    }

    /// <summary>UI の言語に応じたショートカット名。</summary>
    public static string FileNameFor(bool japanese) => japanese ? JapaneseFileName : DefaultFileName;

    /// <summary>
    /// 「送る」フォルダーにこのアプリのショートカットがあるか。
    /// 日本語名・英語名のどちらかがあれば true。中身は見ない。
    /// </summary>
    public static bool Exists() => Exists(SendToDirectory);

    /// <summary>指定フォルダー版（テスト用）。</summary>
    public static bool Exists(string sendToDirectory)
    {
        if (string.IsNullOrEmpty(sendToDirectory)) return false;

        foreach (var name in KnownFileNames)
        {
            if (File.Exists(Path.Combine(sendToDirectory, name))) return true;
        }

        return false;
    }

    /// <summary>
    /// 「送る」フォルダーにショートカットを作る。別名（もう一方の言語の名前）が
    /// 残っていれば消して、常に 1 つだけにする。既にあれば作り直す
    /// （インストール先が変わっていても追随できる）。
    /// </summary>
    /// <param name="targetExePath">本体 AttacheCase.exe のフルパス。</param>
    /// <param name="japanese">日本語名にするか。</param>
    public static void Create(string targetExePath, bool japanese)
        => Create(SendToDirectory, targetExePath, japanese);

    /// <summary>指定フォルダー版（テスト用）。</summary>
    public static void Create(string sendToDirectory, string targetExePath, bool japanese)
    {
        ArgumentException.ThrowIfNullOrEmpty(sendToDirectory);
        ArgumentException.ThrowIfNullOrEmpty(targetExePath);

        if (!File.Exists(targetExePath))
        {
            throw new FileNotFoundException("ショートカットの参照先が見つかりません。", targetExePath);
        }

        Directory.CreateDirectory(sendToDirectory);

        var fileName = FileNameFor(japanese);
        var shortcutPath = Path.Combine(sendToDirectory, fileName);

        // 一時名に書いてから本名へ置き換える（書きかけの .lnk を「送る」に見せない）
        var tempPath = Path.Combine(sendToDirectory, fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            WriteShellLink(tempPath, targetExePath);
            File.Move(tempPath, shortcutPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }

        // もう一方の言語名が残っていれば消す（2 つ並ぶのは紛らわしい）
        foreach (var name in KnownFileNames)
        {
            if (string.Equals(name, fileName, StringComparison.Ordinal)) continue;
            TryDelete(Path.Combine(sendToDirectory, name));
        }
    }

    /// <summary>「送る」フォルダーからこのアプリのショートカットを（両方の名前とも）消す。</summary>
    public static void Remove() => Remove(SendToDirectory);

    /// <summary>指定フォルダー版（テスト用）。</summary>
    public static void Remove(string sendToDirectory)
    {
        if (string.IsNullOrEmpty(sendToDirectory)) return;

        foreach (var name in KnownFileNames)
        {
            var path = Path.Combine(sendToDirectory, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// .lnk の参照先（パス）を読む。無い・読めないときは null。
    /// テストの確認用。アプリ本体は中身を見ない（Exists の説明を参照）。
    /// </summary>
    public static string? ReadTargetPath(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)) return null;

        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, STGM_READ);
            var buffer = new StringBuilder(MAX_PATH);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            return buffer.ToString();
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    private static void WriteShellLink(string shortcutPath, string targetExePath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(targetExePath);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetExePath) ?? "");
            link.SetIconLocation(targetExePath, 0);   // exe 自身のアイコン
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // 消せなくても本処理の結果は変えない
        }
    }

    #region COM（Shell の IShellLinkW / IPersistFile）

    private const int MAX_PATH = 260;
    private const uint SLGP_RAWPATH = 0x0004;
    private const int STGM_READ = 0x00000000;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]   // CLSID_ShellLink
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]   // IID_IShellLinkW
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]   // IID_IPersistFile
    private interface IPersistFile
    {
        // IPersist
        void GetClassID(out Guid pClassID);

        // IPersistFile
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    #endregion
}
