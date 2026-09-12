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
// 見出しのパス表示（PathDisplayConverter.Shorten）。
// Avalonia の PathSegmentEllipsis / PrefixCharacterEllipsis が日本語混じりのパスで
// 落ちる（2026-09-12 実測）ため、縮める処理を自前にした。ここでは
//   - 予算内なら手を付けない
//   - ファイル名は必ず残り、先頭の区切りも残る
//   - 末尾側の階層から入るだけ残す
//   - ファイル名だけで予算を超えるときは拡張子側が残る
//   - 全角は 2 として数える
// を見る。
//----------------------------------------------------------------------

using AttacheCase.Converters;
using Xunit;

namespace AttacheCase.Tests;

public class PathDisplayConverterTests
{
    [Fact]
    public void 予算内のパスはそのまま()
    {
        const string path = @"C:\Users\alice\Desktop\test.txt";
        Assert.Equal(path, PathDisplayConverter.Shorten(path, 48));
    }

    [Fact]
    public void 長いパスは先頭と末尾を残して中を省略する()
    {
        const string path = @"C:\Users\alice\Documents\Projects\AttacheCase\dogfooding\samples\report.txt";
        var s = PathDisplayConverter.Shorten(path, 40);

        Assert.StartsWith(@"C:\…\", s);
        Assert.EndsWith(@"\report.txt", s);
        Assert.True(s.Length <= 40, s);
        // 末尾側の階層が優先して残る
        Assert.Contains(@"\samples\report.txt", s);
    }

    [Fact]
    public void 日本語は全角2で数えてファイル名を残す()
    {
        const string path = @"C:\Users\alice\日本語のフォルダー\さらに深い階層\テスト用の書類ファイル名がとても長い場合の確認.txt";
        var s = PathDisplayConverter.Shorten(path, 48);

        Assert.EndsWith(".txt", s);
        Assert.Contains("…", s);
        Assert.DoesNotContain("alice", s);
    }

    [Fact]
    public void ファイル名だけで予算を超えるときは拡張子側を残す()
    {
        const string path = @"D:\a_very_long_ascii_file_name_that_goes_on_and_on_and_on_and_on_and_on.txt";
        var s = PathDisplayConverter.Shorten(path, 30);

        Assert.StartsWith("…", s);
        Assert.EndsWith("and_on.txt", s);
        Assert.True(s.Length <= 30, s);
    }

    [Fact]
    public void 区切りの無い文字列も先頭を削る()
    {
        var s = PathDisplayConverter.Shorten(new string('x', 100), 20);
        Assert.StartsWith("…", s);
        Assert.Equal(20, s.Length);
    }

    [Fact]
    public void 空や短い文字列はそのまま()
    {
        Assert.Equal("", PathDisplayConverter.Shorten("", 48));
        Assert.Equal("a.txt", PathDisplayConverter.Shorten("a.txt", 48));
    }

    [Fact]
    public void スラッシュ区切りのパスでも同じ()
    {
        const string path = "/Users/alice/Library/Application Support/AttacheCase/very/deep/folder/file.atc";
        var s = PathDisplayConverter.Shorten(path, 40);
        Assert.StartsWith("/…/", s);
        Assert.EndsWith("/file.atc", s);
        Assert.True(s.Length <= 40, s);
    }
}
