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
// DLL プリロード攻撃対策（DllSearchPathGuard）の検証。
//
// (1) 検索順序: 対策後は、カレントディレクトリ・アプリケーションディレクトリに
//     置いた DLL が名前だけの LoadLibrary で見つからないことを Win32 のエラー
//     コードで確かめる。おとりは PE でない中身にしてあるので、検索順序に
//     含まれていれば ERROR_BAD_EXE_FORMAT (193)、含まれていなければ
//     ERROR_MOD_NOT_FOUND (126) になり、「読まれたか／探されもしなかったか」を
//     区別できる。
// (2) P/Invoke リゾルバー: System32 の DLL はそこから解決し、System32 にも
//     同梱フォルダーにも無い名前は拒否する（DllNotFoundException）。パス付きだけは既定解決に委ねる。
// (3) 起動時チェック: FindDlls はフォルダー直下の .dll だけを返す。テストホストは
//     単一ファイル発行ではないので FindForeignDlls は空（起動を止めない）。
//
// SetDefaultDllDirectories はプロセス全体に効いて元に戻せないため、
// このテストはテストホスト全体の検索順序を System32 に絞る。他のテストは
// ネイティブライブラリをフルパスで読むか managed のみなので影響しない。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using AttacheCase.Platform;
using Xunit;

namespace AttacheCase.Tests;

public class DllSearchPathGuardTests
{
    private const int ErrorModNotFound = 126;   // ERROR_MOD_NOT_FOUND
    private const int ErrorBadExeFormat = 193;  // ERROR_BAD_EXE_FORMAT

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Windows のみ（DLL 検索順序に依存）";
        }
    }

    [Fact]
    public void Windows以外では何もせず成功を返し起動も止めない()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.True(DllSearchPathGuard.Install());
        Assert.Empty(DllSearchPathGuard.FindForeignDlls());
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void 対策後はカレントディレクトリとアプリケーションディレクトリのDLLが探されない()
    {
        var name = "atc_decoy_" + Guid.NewGuid().ToString("N") + ".dll";
        var decoyDirs = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
        };
        var decoys = new System.Collections.Generic.List<string>();
        try
        {
            foreach (var dir in decoyDirs)
            {
                var path = Path.Combine(dir, name);
                if (decoys.Contains(path)) continue;
                File.WriteAllText(path, "not a PE file");
                decoys.Add(path);
            }

            // 前提の確認: おとり自体はフルパスなら「見つかるが PE でない」(193) になる
            Assert.Equal(ErrorBadExeFormat, TryLoad(decoys[0]));

            Assert.True(DllSearchPathGuard.Install());
            Assert.True(DllSearchPathGuard.IsInstalled);
            Assert.True(DllSearchPathGuard.DefaultDllDirectoriesRestricted);

            // 名前だけの LoadLibrary で、おとりが探されもしないこと
            Assert.Equal(ErrorModNotFound, TryLoad(name));

            // 2 回呼んでも壊れない（GUI と CLI が同じ経路を通る前提の保険）
            Assert.True(DllSearchPathGuard.Install());
        }
        finally
        {
            foreach (var path in decoys)
            {
                try { File.Delete(path); } catch { /* 後始末の失敗は無視 */ }
            }
        }
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void リゾルバーはSystem32のDLLをSystem32から解決する()
    {
        DllSearchPathGuard.Install();

        var handle = DllSearchPathGuard.Resolve("bcrypt.dll", typeof(DllSearchPathGuardTests).Assembly, null);
        Assert.NotEqual(IntPtr.Zero, handle);

        AssertLoadedFromSystem32(handle);

        // 拡張子なしでもローダーが .dll を補う
        Assert.NotEqual(IntPtr.Zero, DllSearchPathGuard.Resolve("bcrypt", typeof(DllSearchPathGuardTests).Assembly, null));
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void リゾルバーは見つからない名前を拒否しパス付きは既定に委ねる()
    {
        DllSearchPathGuard.Install();
        var assembly = typeof(DllSearchPathGuardTests).Assembly;

        // System32 にも同梱フォルダーにも無い名前 → 既定解決へ戻さず拒否（DllNotFoundException）。
        // 既定解決は単一ファイル発行で exe のフォルダーも探すため、そこへ落とさない
        var name = "atc_missing_" + Guid.NewGuid().ToString("N") + ".dll";
        Assert.Throws<DllNotFoundException>(() => DllSearchPathGuard.Resolve(name, assembly, null));

        // パス付き・ルート付きは呼び出し元の意図どおり（触らない）
        Assert.Equal(IntPtr.Zero, DllSearchPathGuard.Resolve(@"sub\bcrypt.dll", assembly, null));
        Assert.Equal(IntPtr.Zero, DllSearchPathGuard.Resolve("sub/bcrypt.dll", assembly, null));
        Assert.Equal(IntPtr.Zero, DllSearchPathGuard.Resolve(@"C:\nowhere\bcrypt.dll", assembly, null));
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void リゾルバーはカレントディレクトリのおとりをSystem32の名前で読まない()
    {
        // System32 に実在する名前のおとりをカレントディレクトリに置いても、
        // リゾルバーは System32 の本物を返す（おとりは PE でないので、読みに
        // 行っていれば失敗して Zero になるか、別のパスになる）
        DllSearchPathGuard.Install();
        var decoy = Path.Combine(Directory.GetCurrentDirectory(), "dwmapi.dll");
        var created = false;
        try
        {
            if (!File.Exists(decoy))
            {
                File.WriteAllText(decoy, "not a PE file");
                created = true;
            }

            var handle = DllSearchPathGuard.Resolve("dwmapi.dll", typeof(DllSearchPathGuardTests).Assembly, null);
            Assert.NotEqual(IntPtr.Zero, handle);
            AssertLoadedFromSystem32(handle);
        }
        finally
        {
            if (created)
            {
                try { File.Delete(decoy); } catch { /* 後始末の失敗は無視 */ }
            }
        }
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void FindDllsはフォルダー直下のdllだけを大文字小文字を区別せず返す()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atc_finddlls_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "b.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "A.DLL"), "x");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "readme.dll.txt"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "sub.dll"));            // フォルダーは対象外
            File.WriteAllText(Path.Combine(dir, "sub.dll", "nested.dll"), "x"); // 直下のみ

            Assert.True(DllSearchPathGuard.TryFindDlls(dir, out var found));
            Assert.Equal(new[] { "A.DLL", "b.dll" }, found);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void 存在しないフォルダーはTryFindDllsが失敗を返しFindDllsは空を返す()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atc_nodir_" + Guid.NewGuid().ToString("N"));
        // 列挙できない = 安全と断定できない → TryFindDlls は false（呼び出し側は起動を断る）
        Assert.False(DllSearchPathGuard.TryFindDlls(dir, out var dlls));
        Assert.Empty(dlls);
        // 診断用の薄いラッパーは従来どおり空を返す
        Assert.Empty(DllSearchPathGuard.FindDlls(dir));
    }

    [Fact]
    public void テストホストは単一ファイル発行ではないので起動時チェックは何も返さない()
    {
        // 出力フォルダーには DLL が並ぶが、単一ファイル発行でなければ起動を止めない
        Assert.False(DllSearchPathGuard.IsSingleFileBundle);
        Assert.Empty(DllSearchPathGuard.FindForeignDlls());
    }

    [Fact]
    public void 起動を断るメッセージにはフォルダーとDLL名が入る()
    {
        var message = DllSearchPathGuard.BuildRefusalMessage(new[] { "bcrypt.dll", "dwmapi.dll" });
        Assert.Contains(AppContext.BaseDirectory, message);
        Assert.Contains("bcrypt.dll", message);
        Assert.Contains("dwmapi.dll", message);
        Assert.Contains("AttacheCase.exe", message);
    }

    [Fact]
    public void 検索順序を限定できなかったときのメッセージは空でない()
    {
        var message = DllSearchPathGuard.BuildInstallFailureMessage();
        Assert.False(string.IsNullOrWhiteSpace(message));
        // 日本語は「アタッシェケース」、英語は "AttacheCase"。両方に共通するのは "DLL"
        Assert.Contains("DLL", message);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Windowsでは検索順序の限定に成功しIsInstalledがtrueになる()
    {
        Assert.True(DllSearchPathGuard.Install());
        Assert.True(DllSearchPathGuard.IsInstalled);
        Assert.True(DllSearchPathGuard.DefaultDllDirectoriesRestricted);
    }

    /// <summary>LoadLibrary を試し、失敗なら Win32 エラーコード、成功なら 0 を返す。</summary>
    private static int TryLoad(string nameOrPath)
    {
        var handle = LoadLibraryW(nameOrPath);
        var error = Marshal.GetLastWin32Error();
        if (handle != IntPtr.Zero)
        {
            FreeLibrary(handle);
            return 0;
        }
        return error;
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Install後はbcryptなど暗号系DLLがSystem32から固定読み込みされている()
    {
        // CoreLib の P/Invoke（RandomNumberGenerator → bcrypt など）はリゾルバーを通らないため、
        // Install が System32 の本物を先に読み込んで固定していることを、モジュールの実パスで確かめる。
        // 固定済みなら、あとで exe の隣へ同名 DLL を置いても名前一致で既読モジュールが再利用される
        DllSearchPathGuard.Install();

        var system32 = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.System))
            .TrimEnd(Path.DirectorySeparatorChar);

        foreach (var dll in new[] { "bcrypt.dll", "ncrypt.dll", "crypt32.dll", "advapi32.dll" })
        {
            var handle = GetModuleHandleW(dll);
            Assert.NotEqual(IntPtr.Zero, handle); // Install で読み込まれている
            var parent = Path.GetFullPath(Path.GetDirectoryName(GetModulePath(handle))!)
                .TrimEnd(Path.DirectorySeparatorChar);
            Assert.Equal(system32, parent, ignoreCase: true);
        }

        // 実際に乱数が引けること（bcrypt が壊れていない）も確認
        var buffer = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buffer);
        Assert.Contains(buffer, b => b != 0);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string lpModuleName);

    /// <summary>読み込んだモジュールの親フォルダーが「まさに System32」であることを完全一致で確かめる
    /// （StartsWith だと C:\Windows\System32-evil のような接頭辞一致も通ってしまう）。</summary>
    [SupportedOSPlatform("windows")]
    private static void AssertLoadedFromSystem32(IntPtr module)
    {
        var path = GetModulePath(module);
        var parent = Path.GetFullPath(Path.GetDirectoryName(path)!).TrimEnd(Path.DirectorySeparatorChar);
        var system32 = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.System)).TrimEnd(Path.DirectorySeparatorChar);
        Assert.Equal(system32, parent, ignoreCase: true);
    }

    private static string GetModulePath(IntPtr module)
    {
        var buffer = new StringBuilder(1024);
        var length = GetModuleFileNameW(module, buffer, buffer.Capacity);
        Assert.True(length > 0);
        return buffer.ToString(0, (int)length);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetModuleFileNameW(IntPtr hModule, StringBuilder lpFilename, int nSize);
}
