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
// 「対策後は、カレントディレクトリ・アプリケーションディレクトリに置いた
// DLL が名前だけの LoadLibrary で見つからない」ことを Win32 のエラーコードで
// 確かめる。おとりは PE でない中身にしてあるので、検索順序に含まれていれば
// ERROR_BAD_EXE_FORMAT (193)、含まれていなければ ERROR_MOD_NOT_FOUND (126)
// になり、「読まれたか／探されもしなかったか」を区別できる。
//
// SetDefaultDllDirectories はプロセス全体に効いて元に戻せないため、
// このテストはテストホスト全体の検索順序を System32 に絞る。他のテストは
// ネイティブライブラリをフルパスで読むか managed のみなので影響しない。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;
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
    public void Windows以外では何もせず成功を返す()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.True(DllSearchPathGuard.Apply());
    }

    [WindowsFact]
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

            Assert.True(DllSearchPathGuard.Apply());

            // 名前だけの LoadLibrary で、おとりが探されもしないこと
            Assert.Equal(ErrorModNotFound, TryLoad(name));

            // 2 回呼んでも壊れない（GUI と CLI が同じ経路を通る前提の保険）
            Assert.True(DllSearchPathGuard.Apply());
        }
        finally
        {
            foreach (var path in decoys)
            {
                try { File.Delete(path); } catch { /* 後始末の失敗は無視 */ }
            }
        }
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);
}
