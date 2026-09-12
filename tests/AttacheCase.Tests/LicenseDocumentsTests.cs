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
// ライセンス表示のテスト。
//
// ここで守りたいのは「配布物からライセンス文が消えないこと」。
// MIT などは著作権表示と許諾表示を配布物に含めることを条件にしているので、
// 埋め込みが外れたまま気づかずに配ると、条件を満たさないものを配ることになる。
//----------------------------------------------------------------------

using System.Linq;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class LicenseDocumentsTests
{
    [Theory]
    [InlineData(LicenseDocument.SourceAvailable)]
    [InlineData(LicenseDocument.Commercial)]
    [InlineData(LicenseDocument.ThirdParty)]
    public void 文書は3つとも埋め込まれている(LicenseDocument document)
    {
        // csproj の EmbeddedResource か LogicalName を変えると、ここで落ちる
        var markdown = LicenseDocuments.LoadMarkdown(document);

        Assert.False(string.IsNullOrWhiteSpace(markdown));
        Assert.True(markdown.Length > 500, $"{document} の中身が短すぎます: {markdown.Length} 文字");
    }

    [Fact]
    public void 第三者表示にはライセンス全文が入っている()
    {
        // 名前だけ並べても MIT の条件は満たさない。全文が要る
        var text = LicenseDocuments.LoadText(LicenseDocument.ThirdParty);

        Assert.Contains("Permission is hereby granted, free of charge", text);
        Assert.Contains("THE SOFTWARE IS PROVIDED \"AS IS\"", text);
        Assert.Contains("SIL OPEN FONT LICENSE", text.ToUpperInvariant());
        Assert.Contains("Microsoft Public License", text);
        Assert.Contains("Redistribution and use in source and binary forms", text);
    }

    [Fact]
    public void 第三者表示には同梱物の著作権表示が入っている()
    {
        var text = LicenseDocuments.LoadText(LicenseDocument.ThirdParty);

        Assert.Contains("The AvaloniaUI Project", text);
        Assert.Contains("Legion of the Bouncy Castle", text);
        Assert.Contains("SharpZipLib Contributors", text);
        Assert.Contains("Wiesław Šoltés", text);
        Assert.Contains("Google Inc.", text);            // Skia
        Assert.Contains("Inter Project Authors", text);  // フォント
    }

    [Fact]
    public void 同梱していないものを載せない()
    {
        // 実体の無い表示は、あるものを漏らすのと同じくらい信用を損なう。
        // 依存を消したときに一覧を直し忘れると、ここで気づける
        var text = LicenseDocuments.LoadText(LicenseDocument.ThirdParty);
        var listed = text[..text.IndexOf("配布物に含まれないもの", System.StringComparison.Ordinal)];

        Assert.DoesNotContain("Newtonsoft.Json", listed);
        Assert.DoesNotContain("Avalonia.Controls.ColorPicker", listed);
    }

    // =================================================================
    // 表示用の整形
    // =================================================================

    [Fact]
    public void 見出しと強調の記号を落とす()
    {
        var text = LicenseDocuments.ToDisplayText("# 見出し\n\n**強調**と`コード`。\n");

        Assert.Contains("見出し", text);
        Assert.DoesNotContain("#", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("`", text);
    }

    [Fact]
    public void リンクは表示側の文字だけ残す()
    {
        var text = LicenseDocuments.ToDisplayText("[Avalonia](https://example.test/a) は MIT です。\n");

        Assert.Contains("Avalonia は MIT です。", text);
        Assert.DoesNotContain("https://", text);
    }

    [Fact]
    public void 表は桁をそろえて開く()
    {
        var markdown = "| 名前 | ライセンス |\n|---|---|\n| Avalonia | MIT |\n| BouncyCastle | MIT |\n";
        var lines = LicenseDocuments.ToDisplayText(markdown)
            .Split('\n')
            .Where(line => line.Contains("MIT"))
            .ToArray();

        Assert.Equal(2, lines.Length);

        // 全角を 2 桁と数えていないと、ここがずれる
        var column = lines.Select(line => line.IndexOf("MIT", System.StringComparison.Ordinal)).Distinct();
        Assert.Single(column);

        // 罫線の行は落とす
        Assert.DoesNotContain("|", LicenseDocuments.ToDisplayText(markdown));
    }

    [Fact]
    public void コードブロックの中は一字も変えない()
    {
        // ライセンス全文が入っている。折り返しも記号落としもしてはいけない
        var body = "  **これは強調ではない**  [リンク](url) と長い行 " + new string('x', 200);
        var markdown = "```\n" + body + "\n```\n";

        Assert.Contains(body.TrimEnd(), LicenseDocuments.ToDisplayText(markdown));
    }

    [Fact]
    public void 長い行は折り返す()
    {
        var markdown = new string('あ', 200) + "\n";
        var lines = LicenseDocuments.ToDisplayText(markdown).TrimEnd().Split('\n');

        Assert.True(lines.Length > 1);
        Assert.All(lines, line =>
            Assert.True(LicenseDocuments.DisplayWidth(line) <= LicenseDocuments.WrapColumns,
                $"折り返しきれていません: {LicenseDocuments.DisplayWidth(line)} 桁"));
    }

    [Fact]
    public void 英文は語の途中で切らない()
    {
        var markdown = string.Join(" ", Enumerable.Repeat("permission", 30)) + "\n";
        var lines = LicenseDocuments.ToDisplayText(markdown).TrimEnd().Split('\n');

        Assert.True(lines.Length > 1);
        Assert.All(lines, line => Assert.DoesNotContain("permissio\n", line));
        Assert.All(lines, line =>
            Assert.All(line.Split(' ').Where(w => w.Length > 0),
                word => Assert.Equal("permission", word)));
    }

    [Fact]
    public void DisplayWidth_全角を2桁と数える()
    {
        Assert.Equal(2, LicenseDocuments.DisplayWidth("あ"));
        Assert.Equal(1, LicenseDocuments.DisplayWidth("a"));
        Assert.Equal(5, LicenseDocuments.DisplayWidth("あiう"));
    }
}
