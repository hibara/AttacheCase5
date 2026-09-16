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
// 設定「復号したファイルを関連付けられたアプリで開く」（/opf）の OS 分岐。
// Windows で /usr/bin/open 固定のまま動かなかった不具合（2026-09-14）の再発防止。
//----------------------------------------------------------------------

using System;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class PostProcessHelperTests
{
    [Fact]
    public void OpenStartInfo_UsesTheShellOfTheCurrentOs()
    {
        var path = OperatingSystem.IsWindows() ? @"C:\Temp\report.xlsx" : "/tmp/report.xlsx";

        var info = PostProcessHelper.CreateOpenStartInfo(path);

        if (OperatingSystem.IsWindows())
        {
            // ShellExecute で関連付けを解決する（旧 Windows 版と同じ）
            Assert.Equal(path, info.FileName);
            Assert.True(info.UseShellExecute);
            Assert.Empty(info.ArgumentList);
        }
        else
        {
            Assert.Equal(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "xdg-open", info.FileName);
            Assert.False(info.UseShellExecute);
            Assert.Equal([path], info.ArgumentList);
        }
    }
}
