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
// 「送る」メニューのショートカット（.lnk）の作成・削除・存在判定。
// 実際の「送る」フォルダーは触らず、一時フォルダーで同じ API を通す。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.Versioning;
using AttacheCase.Platform;
using Xunit;

namespace AttacheCase.Tests;

public class WindowsSendToShortcutTests : IDisposable
{
    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Windows のみ（Shell の IShellLink に依存）";
        }
    }

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "atc_sendto_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (Exception) { /* 片付けの失敗はテスト結果に影響しない */ }
    }

    [SupportedOSPlatform("windows")]
    private string MakeTargetExe()
    {
        Directory.CreateDirectory(_dir);
        var exe = Path.Combine(_dir, "AttacheCase.exe");
        File.WriteAllBytes(exe, [0x4D, 0x5A]);   // 中身は見ない。存在すればよい
        return exe;
    }

    [WindowsFact]
    public void ショートカットを作ると存在判定が真になり参照先が本体を指す()
    {
        var exe = MakeTargetExe();
        var sendTo = Path.Combine(_dir, "SendTo");

        Assert.False(WindowsSendToShortcut.Exists(sendTo));

        WindowsSendToShortcut.Create(sendTo, exe, japanese: true);

        var link = Path.Combine(sendTo, WindowsSendToShortcut.JapaneseFileName);
        Assert.True(File.Exists(link));
        Assert.True(WindowsSendToShortcut.Exists(sendTo));
        Assert.Equal(exe, WindowsSendToShortcut.ReadTargetPath(link), ignoreCase: true);
        Assert.Empty(Directory.GetFiles(sendTo, "*.tmp"));
    }

    [WindowsFact]
    public void 言語を変えて作り直すと名前は常に一つだけ残る()
    {
        var exe = MakeTargetExe();
        var sendTo = Path.Combine(_dir, "SendTo");

        WindowsSendToShortcut.Create(sendTo, exe, japanese: true);
        WindowsSendToShortcut.Create(sendTo, exe, japanese: false);

        Assert.False(File.Exists(Path.Combine(sendTo, WindowsSendToShortcut.JapaneseFileName)));
        Assert.True(File.Exists(Path.Combine(sendTo, WindowsSendToShortcut.DefaultFileName)));
        Assert.True(WindowsSendToShortcut.Exists(sendTo));

        // 同じ名前で作り直しても壊れない（インストール先の変更に追随する経路）
        WindowsSendToShortcut.Create(sendTo, exe, japanese: false);
        Assert.Single(Directory.GetFiles(sendTo));
    }

    [WindowsFact]
    public void 削除すると両方の名前とも消えて存在判定が偽になる()
    {
        var exe = MakeTargetExe();
        var sendTo = Path.Combine(_dir, "SendTo");

        WindowsSendToShortcut.Create(sendTo, exe, japanese: true);
        File.WriteAllText(Path.Combine(sendTo, WindowsSendToShortcut.DefaultFileName), "");
        Assert.True(WindowsSendToShortcut.Exists(sendTo));

        WindowsSendToShortcut.Remove(sendTo);

        Assert.False(WindowsSendToShortcut.Exists(sendTo));
        Assert.Empty(Directory.GetFiles(sendTo));

        // 無い状態でもう一度消しても例外にならない
        WindowsSendToShortcut.Remove(sendTo);
    }

    [WindowsFact]
    public void 参照先が無ければ作らず例外になる()
    {
        var sendTo = Path.Combine(_dir, "SendTo");
        Assert.Throws<FileNotFoundException>(
            () => WindowsSendToShortcut.Create(sendTo, Path.Combine(_dir, "missing.exe"), japanese: false));
        Assert.False(WindowsSendToShortcut.Exists(sendTo));
    }

    [WindowsFact]
    public void 無いフォルダーや空のパスでは存在判定が偽になる()
    {
        Assert.False(WindowsSendToShortcut.Exists(""));
        Assert.False(WindowsSendToShortcut.Exists(Path.Combine(_dir, "nowhere")));
    }
}
