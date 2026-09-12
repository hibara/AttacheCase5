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
// 関連付けの DefaultIcon 値から同梱アイコン番号を取り出す処理（OS 非依存の部分）。
//----------------------------------------------------------------------

using AttacheCase.Platform;
using Xunit;

namespace AttacheCase.Tests;

public class WindowsFileAssociationTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\AttacheCase5\Icons\atc3.ico""", 3)]
    [InlineData(@"C:\Program Files\AttacheCase5\Icons\atc1.ico", 1)]
    [InlineData(@"""D:\portable\Icons\ATC4.ICO""", 4)]
    [InlineData(@"""C:\Program Files\AttacheCase5\Icons\atc2.ico"",0", 2)]
    public void 同梱アイコンのパスから番号を取り出す(string value, int expected)
    {
        Assert.True(WindowsFileAssociation.TryParseIconIndex(value, out var index));
        Assert.Equal(expected, index);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"""C:\Program Files\AttacheCase5\AttacheCase.exe"",0")]
    [InlineData(@"C:\icons\atc5.ico")]
    [InlineData(@"C:\icons\atc.ico")]
    [InlineData(@"C:\icons\myatc1.ico")]
    public void 同梱アイコン以外は取り出さない(string? value)
    {
        Assert.False(WindowsFileAssociation.TryParseIconIndex(value, out _));
    }
}
