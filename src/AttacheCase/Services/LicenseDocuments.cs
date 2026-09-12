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
// ライセンス文書の読み込みと、画面に出すための整形。
//
// 文書はリポジトリの .md をそのまま埋め込む。表示用に別の写しを持つと、
// 依存を足したときに片方だけ直して食い違う。第三者ライセンスの表示は
// MIT などの条件そのものなので、食い違いは条件違反になりうる。
//
// 整形は最小限。見出し・強調・リンク・表を、等幅で読める平文に開くだけで、
// 本文の語は変えない。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AttacheCase.Services;

/// <summary>画面で見せるライセンス文書。</summary>
public enum LicenseDocument
{
    /// <summary>本体の利用条件（AttacheCase Source Available License）。</summary>
    SourceAvailable,

    /// <summary>業務利用・組み込みのライセンス区分。</summary>
    Commercial,

    /// <summary>同梱している第三者ソフトウェアの表示。</summary>
    ThirdParty,
}

public static class LicenseDocuments
{
    /// <summary>
    /// 折り返す桁数（半角換算）。等幅で読む前提の平文にそろえる。
    ///
    /// 表は折り返さない。桁がずれると読めなくなるため。
    /// </summary>
    internal const int WrapColumns = 84;

    /// <summary>
    /// 埋め込み時の名前。csproj の LogicalName と対応する。
    /// ここを変えるときは csproj も直すこと（テストが食い違いを見張っている）。
    /// </summary>
    private static string ResourceNameOf(LicenseDocument document) => document switch
    {
        LicenseDocument.SourceAvailable => "LICENSE.md",
        LicenseDocument.Commercial => "COMMERCIAL-LICENSE.md",
        LicenseDocument.ThirdParty => "THIRD_PARTY_NOTICES.md",
        _ => throw new ArgumentOutOfRangeException(nameof(document)),
    };

    /// <summary>埋め込まれた .md をそのまま読む。</summary>
    public static string LoadMarkdown(LicenseDocument document)
    {
        var name = ResourceNameOf(document);
        var assembly = typeof(LicenseDocuments).Assembly;

        using var stream = assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException(
                               $"ライセンス文書が埋め込まれていません: {name}");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>画面に出せる平文にして読む。</summary>
    public static string LoadText(LicenseDocument document) =>
        ToDisplayText(LoadMarkdown(document));

    /// <summary>
    /// Markdown を等幅で読める平文にする。
    /// </summary>
    internal static string ToDisplayText(string markdown)
    {
        var output = new List<string>();
        var table = new List<string[]>();
        var inCodeFence = false;

        // .md はすでに人の手で改行してある。そのまま折り返すと、行末の
        // 「、」だけが次の行に落ちるような読みにくい形になる。
        // いったん段落につなぎ直してから、この画面の桁で折り返す
        var paragraph = new StringBuilder();
        var paragraphIndent = string.Empty;
        var paragraphMarker = string.Empty;

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;

            output.AddRange(Wrap(
                paragraph.ToString(),
                paragraphIndent + paragraphMarker,
                paragraphIndent + new string(' ', DisplayWidth(paragraphMarker))));

            paragraph.Clear();
            paragraphIndent = string.Empty;
            paragraphMarker = string.Empty;
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            // コードブロックの中はライセンス全文。1 文字も触らない
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushTable(table, output);
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                output.Add(raw.TrimEnd());
                continue;
            }

            // 表は行がそろってから幅を決める。いったん溜める
            if (line.TrimStart().StartsWith("|", StringComparison.Ordinal))
            {
                FlushParagraph();
                var cells = SplitTableRow(line);
                if (!IsTableSeparator(cells)) table.Add(cells);
                continue;
            }

            FlushTable(table, output);

            if (line.Length == 0)
            {
                FlushParagraph();
                output.Add(string.Empty);
                continue;
            }

            // 区切り線
            if (line is "---" or "***" or "___")
            {
                FlushParagraph();
                output.Add(new string('─', WrapColumns));
                continue;
            }

            // 見出し。記号を落として、大きいものだけ下線を引く
            var heading = 0;
            while (heading < line.Length && line[heading] == '#') heading++;
            if (heading > 0 && heading < line.Length && line[heading] == ' ')
            {
                FlushParagraph();
                var text = Inline(line[(heading + 1)..].Trim());
                output.Add(text);
                if (heading <= 2) output.Add(new string(heading == 1 ? '═' : '─', DisplayWidth(text)));
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            var body = Inline(line.TrimStart());

            // 箇条書きは、印のところで新しい段落として始める
            if (body.StartsWith("- ", StringComparison.Ordinal) ||
                body.StartsWith("* ", StringComparison.Ordinal))
            {
                FlushParagraph();
                paragraphIndent = new string(' ', indent);
                paragraphMarker = "・";
                paragraph.Append(body[2..]);
                continue;
            }

            if (paragraph.Length == 0)
            {
                paragraphIndent = new string(' ', indent);
                paragraph.Append(body);
                continue;
            }

            // 続きの行をつなぐ。英字どうしのときだけ空白を入れる
            // （日本語の間に空白を入れると不自然になる）
            if (NeedsSpaceBetween(paragraph[^1], body[0])) paragraph.Append(' ');
            paragraph.Append(body);
        }

        FlushParagraph();
        FlushTable(table, output);

        return string.Join("\n", output).TrimEnd() + "\n";
    }

    /// <summary>行をつなぐときに空白が要るか。英字どうしのときだけ要る。</summary>
    private static bool NeedsSpaceBetween(char left, char right) =>
        !IsWide(left) && !IsWide(right) && left != ' ' && right != ' ';

    /// <summary>
    /// 行頭に来てはいけない文字（行頭禁則）。
    /// ここを見ないと、行末の「、」だけが次の行へ落ちる。
    /// </summary>
    private const string NotLineStart = "、。，．・：；？！ー〜）］｝」』】〉》〕”’ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ";

    /// <summary>強調・コード・リンクの記号を外す。語は変えない。</summary>
    internal static string Inline(string text)
    {
        var s = text.Replace("<br>", " / ", StringComparison.OrdinalIgnoreCase);

        // [表示](URL) → 表示。URL は .md 側に残る（DMG に同梱している）
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '[')
            {
                var close = s.IndexOf(']', i + 1);
                if (close > 0 && close + 1 < s.Length && s[close + 1] == '(')
                {
                    var end = s.IndexOf(')', close + 2);
                    if (end > 0)
                    {
                        sb.Append(s, i + 1, close - i - 1);
                        i = end;
                        continue;
                    }
                }
            }

            sb.Append(s[i]);
        }

        return sb.Replace("**", "").Replace("`", "").ToString();
    }

    /// <summary>
    /// 半角換算の表示幅。全角は 2 と数える。
    /// これを使わないと、日本語の入った表の桁がそろわない。
    /// </summary>
    internal static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var c in text) width += IsWide(c) ? 2 : 1;
        return width;
    }

    private static bool IsWide(char c) =>
        c is >= 'ᄀ' and <= 'ᅟ' ||      // ハングル字母
        c is >= '⺀' and <= '〾' ||      // CJK の記号・部首
        c is >= 'ぁ' and <= '㏿' ||      // かな・注音・ハングル・CJK 互換
        c is >= '㐀' and <= '䶿' ||      // CJK 拡張 A
        c is >= '一' and <= '鿿' ||      // CJK 統合漢字
        c is >= 'ꀀ' and <= '꓏' ||      // イ文字
        c is >= '가' and <= '힣' ||      // ハングル音節
        c is >= '豈' and <= '﫿' ||      // CJK 互換漢字
        c is >= '︰' and <= '﹏' ||      // CJK 互換形
        c is >= '＀' and <= '｠' ||      // 全角英数・記号
        c is >= '￠' and <= '￦';

    /// <summary>
    /// 指定の桁で折り返す。英字は語の途中で切らず、日本語はどこでも切る。
    /// </summary>
    internal static IEnumerable<string> Wrap(string text, string firstPrefix, string restPrefix)
    {
        if (text.Length == 0) return new[] { firstPrefix.TrimEnd() };

        var lines = new List<string>();
        var prefix = firstPrefix;
        var current = new StringBuilder();
        var width = DisplayWidth(prefix);
        var breakable = -1;   // 直近で切ってよい位置（半角の空白の直後）

        foreach (var c in text)
        {
            var w = IsWide(c) ? 2 : 1;

            if (width + w > WrapColumns && current.Length > 0)
            {
                var cut = breakable > 0 ? breakable : current.Length;

                // 行頭に来てはいけない文字が次の行の先頭に落ちるなら、
                // 1 文字ぶん手前で切る（「、」だけの行を作らない）
                while (cut > 1 && cut < current.Length &&
                       NotLineStart.Contains(current[cut], StringComparison.Ordinal))
                {
                    cut--;
                }

                // これから足す文字が行頭禁則なら、いまの行に押し込む
                if (cut >= current.Length && NotLineStart.Contains(c, StringComparison.Ordinal))
                {
                    current.Append(c);
                    width += w;
                    continue;
                }

                lines.Add(prefix + current.ToString(0, cut).TrimEnd());

                var rest = current.ToString(cut, current.Length - cut).TrimStart();
                current.Clear();
                current.Append(rest);

                prefix = restPrefix;
                width = DisplayWidth(prefix) + DisplayWidth(rest);
                breakable = -1;
            }

            current.Append(c);
            width += w;

            // 半角の空白で切れば語を割らずに済む。全角の文字はどこでも切れる
            if (c == ' ') breakable = current.Length;
            else if (IsWide(c)) breakable = current.Length;
        }

        if (current.Length > 0) lines.Add(prefix + current.ToString().TrimEnd());
        return lines;
    }

    private static string[] SplitTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("|", StringComparison.Ordinal)) trimmed = trimmed[1..];
        if (trimmed.EndsWith("|", StringComparison.Ordinal)) trimmed = trimmed[..^1];

        return trimmed.Split('|').Select(cell => Inline(cell.Trim())).ToArray();
    }

    /// <summary>--- だけの行（表の罫線）か。</summary>
    private static bool IsTableSeparator(string[] cells) =>
        cells.Length > 0 && cells.All(cell =>
            cell.Length > 0 && cell.All(c => c is '-' or ':' or ' '));

    /// <summary>溜めた表を書き出す。</summary>
    private static void FlushTable(List<string[]> rows, List<string> output)
    {
        if (rows.Count == 0) return;

        var columns = rows.Max(r => r.Length);
        var widths = new int[columns];

        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], DisplayWidth(row[i]));
            }
        }

        // 桁をそろえると画面からはみ出す表は、1 件ずつ縦に開く。
        // 著作権表示のように長い値が入る表は、横に伸ばすと読めない
        if (widths.Sum() + (columns - 1) * 2 > WrapColumns)
        {
            FlushTableAsRecords(rows, output);
            return;
        }

        foreach (var row in rows)
        {
            var line = new StringBuilder();
            for (var i = 0; i < row.Length; i++)
            {
                var cell = row[i];
                line.Append(cell);

                // 最後の列は詰め物をしない。行末に空白が並ぶのを避ける
                if (i < row.Length - 1)
                {
                    line.Append(new string(' ', widths[i] - DisplayWidth(cell)));
                    line.Append("  ");
                }
            }

            output.Add(line.ToString().TrimEnd());
        }

        // 見出し行の下に罫線を引く。1 行だけの表には引かない
        if (rows.Count > 1)
        {
            var ruleWidth = Math.Min(WrapColumns, widths.Sum() + (columns - 1) * 2);
            output.Insert(output.Count - (rows.Count - 1), new string('─', ruleWidth));
        }

        rows.Clear();
    }

    /// <summary>
    /// 表を 1 件ずつ縦に開く。1 列目を見出しにし、残りを「項目: 値」で並べる。
    /// </summary>
    private static void FlushTableAsRecords(List<string[]> rows, List<string> output)
    {
        var header = rows[0];

        foreach (var row in rows.Skip(1))
        {
            if (row.Length == 0) continue;

            output.AddRange(Wrap(row[0], "  ", "  "));

            for (var i = 1; i < row.Length; i++)
            {
                if (row[i].Length == 0) continue;

                var label = i < header.Length && header[i].Length > 0 ? header[i] + ": " : string.Empty;
                output.AddRange(Wrap(label + row[i], "      ", "      " + new string(' ', DisplayWidth(label))));
            }

            output.Add(string.Empty);
        }

        rows.Clear();
    }
}
