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
// ファイル／フォルダーのパスを出す TextBlock。
//
// Text に渡されたパスをそのまま出し、与えられた幅と MaxLines（通常 2）に
// 入り切らないときだけ自前で省略する。省略の優先順位:
//   (1) 途中を省略（先頭を残し「…」を挟んで末尾の「区切り + ファイル名」を残す）
//   (2) ファイル名だけでも入らないときは先頭を省略（末尾を優先して残す）
// 末尾のファイル名が消える「末尾省略」はしない。
//
// Avalonia 11.3.10 の TextTrimming="PathSegmentEllipsis" / "PrefixCharacterEllipsis" は
// 日本語と英数字が混じったパスでレイアウト中に例外を投げてアプリごと落ちる
// （invariants.md 参照）ので使わない。
//
// 収まるかどうかの判定は、TextBlock 自身のレイアウトではなく、幅と MaxLines だけを
// 制約にした自前の TextLayout で行う（2026-10-27 実測の罠: TextBlock のレイアウトは
// 配置段階で高さ制約が「計測時に選んだ候補の高さ」に固定されるため、日本語を含んで
// 行の高さがわずかに増える候補が高さで 1 行に切られ、計測時と配置時で判定が食い違って
// 1 行に縮みすぎた）。幅だけは TextBlock の制約を知る必要があるので、基底の
// CreateTextLayout が作るレイアウトの MaxWidth から取る。
// TextTrimming は None のままにすること（Avalonia 側の省略と二重になる）。
//----------------------------------------------------------------------

using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace AttacheCase.Controls;

public class PathTextBlock : TextBlock
{
    private const string Ellipsis = "…";
    private static readonly char[] Separators = { '\\', '/' };

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override TextLayout CreateTextLayout(string? text)
    {
        if (string.IsNullOrEmpty(text) || MaxLines <= 0 || TextWrapping == TextWrapping.NoWrap)
        {
            return base.CreateTextLayout(text);
        }

        double maxWidth;
        using (var probe = base.CreateTextLayout(text))
        {
            maxWidth = probe.MaxWidth;
        }

        // 結果は覚えない。レイアウトのたびに測り直す（候補は二分探索で十数個、パスは高々数百文字）。
        // 覚えると MaxLines / フォントが変わったときに古い省略結果を出してしまう（CodeRabbit PR#19 の指摘）
        return base.CreateTextLayout(Fit(text, maxWidth));
    }

    /// <summary>与えられた幅・行数に収まる形へ縮める（収まるならそのまま）</summary>
    private string Fit(string path, double maxWidth)
    {
        if (Fits(path, maxWidth)) return path;

        var lastSep = path.LastIndexOfAny(Separators);
        if (lastSep > 0)
        {
            var head = path[..lastSep];
            var tail = path[lastSep..];              // 区切りを含むファイル名
            if (Fits(Ellipsis + tail, maxWidth))
            {
                // 先頭を入るだけ残し、途中を省略する
                var k = LargestFitting(head.Length, n => head[..n] + Ellipsis + tail, maxWidth);
                if (k > 0 && char.IsHighSurrogate(head[k - 1])) k--;   // サロゲートペアの途中で切らない
                return head[..k] + Ellipsis + tail;
            }
        }

        // ファイル名だけでも入らない: 先頭を削って末尾を残す
        var m = LargestFitting(path.Length, n => Ellipsis + path[^n..], maxWidth);
        if (m > 0 && m < path.Length && char.IsLowSurrogate(path[^m]))
        {
            // サロゲートペアの途中から始めない。ペアごと含めて入らなければペアごと外す
            m = Fits(Ellipsis + path[^(m + 1)..], maxWidth) ? m + 1 : m - 1;
        }
        // 「…」+ 1 文字すら入らない幅では「…」だけにする（収まらない候補は返さない）
        return m > 0 ? Ellipsis + path[^m..] : Ellipsis;
    }

    /// <summary>0..max の範囲で、build(n) が収まる最大の n を二分探索で求める</summary>
    private int LargestFitting(int max, Func<int, string> build, double maxWidth)
    {
        var lo = 0;
        var hi = max;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Fits(build(mid), maxWidth)) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>幅 maxWidth・MaxLines 行で全文が描けるか（高さは制約しない）</summary>
    private bool Fits(string candidate, double maxWidth)
    {
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        using var layout = new TextLayout(
            candidate,
            typeface,
            FontSize,
            foreground: null,
            textAlignment: TextAlignment,
            textWrapping: TextWrapping,
            textTrimming: TextTrimming.None,
            textDecorations: null,
            flowDirection: FlowDirection,
            maxWidth: maxWidth,
            maxHeight: double.PositiveInfinity,
            lineHeight: LineHeight,
            letterSpacing: LetterSpacing,
            maxLines: MaxLines);

        // TextLine.Length は段落末尾の分だけ文字数より 1 多くなる。MaxLines で
        // 打ち切られた分は数えられないので、合計が文字数に届けば全文が入っている
        var consumed = layout.TextLines.Sum(line => line.Length);
        return consumed >= candidate.Length;
    }
}
