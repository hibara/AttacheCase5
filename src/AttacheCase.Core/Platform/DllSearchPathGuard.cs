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
// DLL プリロード攻撃（DLL ハイジャック）対策。Windows 専用、他 OS では何もしない。
//
// 過去に JPCERT/CC から何度も指摘を受けている脆弱性クラス。典型的な経路は
// 「.atc と同じフォルダーに細工した DLL を置いて、.atc をダブルクリック
// させる」で、関連付け起動のカレントディレクトリがそのフォルダーになるため
// 既定の検索順序（カレントディレクトリ・PATH を含む）だと読み込まれてしまう。
//
// 対策は旧 Windows 版 5.0（AttacheCase/Program.cs）・SFX スタブ
// （ExeOutShared/Program.cs）・AtcSetup と同じ:
//   - SetDllDirectory("")            … 検索順序からカレントディレクトリを外す
//   - SetDefaultDllDirectories(SYSTEM32) … LoadLibrary(名前だけ) の検索先を
//                                          %SystemRoot%\System32 に限定する
// 5.5 では GUI（Avalonia）と CLI を Mac 版の骨格から作ったため一度抜け落ちた。
// 両方の Program.Main の最初の行で Apply() を呼ぶこと（2026-09-12）。
//
// .NET 10 の単一ファイル発行でも壊れない理由: ランタイムが内包ネイティブ
// ライブラリ（Skia / ANGLE 等）を展開先から読むときはフルパスで LoadLibraryEx
// するので、名前だけの検索順序をどう絞っても影響しない。依存 DLL は System32
// のもの（kernel32 / user32 / d3d 等）だけなので SYSTEM32 限定で足りる。
//
// 注意:
//   - SetDllDirectory(null) は「既定に戻す」= カレントディレクトリを含める、
//     という逆の意味になる。必ず空文字を渡す。
//   - 2 本の API は独立に呼ぶ。片方の失敗でもう片方を飛ばすと対策が外れる。
//   - GetLastWin32Error は各呼び出しの直後に取る（次の P/Invoke で上書きされる）。
//   - Main より前にランタイム自身が読む DLL は制御外（旧版も同じ）。
//----------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AttacheCase.Platform;

/// <summary>
/// DLL の検索順序からカレントディレクトリ・PATH を外し、System32 に限定する。
/// プロセスの最初（Main の 1 行目）で 1 回呼ぶ。
/// </summary>
public static class DllSearchPathGuard
{
    // LOAD_LIBRARY_SEARCH_APPLICATION_DIR : 0x00000200
    // LOAD_LIBRARY_SEARCH_USER_DIRS       : 0x00000400
    // LOAD_LIBRARY_SEARCH_SYSTEM32        : 0x00000800
    // LOAD_LIBRARY_SEARCH_DEFAULT_DIRS    : 0x00001000
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    /// <summary>
    /// 対策を適用する。Windows 以外では何もせず true を返す。
    /// Windows で 2 本の API のどちらかが失敗したときは false（起動は続ける。
    /// 検出のためログに残すのは呼び出し側の判断）。
    /// </summary>
    public static bool Apply()
    {
        if (!OperatingSystem.IsWindows()) return true;
        return ApplyWindows();
    }

    [SupportedOSPlatform("windows")]
    private static bool ApplyWindows()
    {
        var isDllDirectorySet = SetDllDirectoryW("");
        var dllDirectoryError = Marshal.GetLastWin32Error();
        var isDefaultDirectoriesSet = SetDefaultDllDirectories(LoadLibrarySearchSystem32);
        var defaultDirectoriesError = Marshal.GetLastWin32Error();

        if (!isDllDirectorySet)
        {
            System.Diagnostics.Debug.WriteLine(
                "SetDllDirectory(\"\") failed: " + dllDirectoryError);
        }

        if (!isDefaultDirectoriesSet)
        {
            System.Diagnostics.Debug.WriteLine(
                "SetDefaultDllDirectories failed: " + defaultDirectoriesError);
        }

        return isDllDirectorySet && isDefaultDirectoriesSet;
    }

    // kernel32 は「既知の DLL」なので、この P/Invoke 自体がハイジャックされることはない。
    // 念のため検索先も System32 に固定しておく。
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectoryW(string lpPathName);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
}
