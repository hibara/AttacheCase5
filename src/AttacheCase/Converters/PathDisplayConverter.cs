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
// 見出しに出すファイルパスを「先頭 … 末尾（ファイル名）」の形に縮める。
//
// 経緯（2026-09-12 実測）: Avalonia 11.3.10 の TextTrimming="PathSegmentEllipsis" と
// "PrefixCharacterEllipsis" は、日本語と英数字が混じって描画ランが複数に分かれる
// パスで ShapedTextRun.Split(0) → ArgumentOutOfRangeException を投げ、レイアウト中の
// 未処理例外としてアプリごと落ちる（日本語名のファイルをドロップしただけで発生）。
// 落ちないのは末尾を削る "CharacterEllipsis" だけだが、それではいちばん見たい
// ファイル名が消える。そこで縮めるのは自前でやり、TextBlock 側は保険として
// CharacterEllipsis だけを付ける。
//
// 幅は測らない（文字数で決める）。全角は 2、半角は 1 として数え、既定の予算は
// 見出しのフォント（18pt 太字）と最小ウィンドウ幅 680px に収まる程度にしてある。
//----------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;
using Avalonia.Data.Converters;

namespace AttacheCase.Converters;

public sealed class PathDisplayConverter : IValueConverter
{
    public static readonly PathDisplayConverter Instance = new();

    /// <summary>既定の表示予算（半角換算の文字数）</summary>
    public const int DefaultBudget = 48;

    private const string Ellipsis = "…";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path) return value;
        var budget = parameter is string s && int.TryParse(s, out var n) && n > 0 ? n : DefaultBudget;
        return Shorten(path, budget);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// パスを予算内に縮める。ファイル名（末尾の区切り以降）は必ず残し、
    /// 先頭の区切り（ドライブやルート直下）を残したうえで、末尾側から入るだけ
    /// 区切りを足していく。ファイル名だけで予算を超えるときは、ファイル名の
    /// 先頭を削って拡張子側を残す。
    /// </summary>
    public static string Shorten(string path, int budget = DefaultBudget)
    {
        if (string.IsNullOrEmpty(path) || Width(path) <= budget) return path;

        var separators = new[] { '\\', '/' };
        var lastSep = path.LastIndexOfAny(separators);
        if (lastSep < 0)
        {
            return TrimHead(path, budget);
        }

        var fileName = path[(lastSep + 1)..];
        var dirPart = path[..lastSep];          // 区切りを含まない
        var sep = path[lastSep].ToString();

        // ファイル名だけで予算を超えるなら、ファイル名の先頭を削る
        var minimal = Ellipsis + sep + fileName;
        if (Width(minimal) > budget)
        {
            return TrimHead(fileName, budget);
        }

        // 先頭の区切り（"C:" や "/Users" など）は残す
        var firstSep = dirPart.IndexOfAny(separators);
        var head = firstSep < 0 ? dirPart : dirPart[..firstSep];
        var middle = firstSep < 0 ? string.Empty : dirPart[(firstSep + 1)..];

        // 末尾側から、入るだけ階層を足す
        var tail = new StringBuilder();
        var segments = middle.Split(separators, StringSplitOptions.None);
        var kept = 0;
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (middle.Length == 0) break;
            var candidate = sep + segments[i];
            var total = Width(head + sep + Ellipsis + candidate + tail + sep + fileName);
            if (total > budget) break;
            tail.Insert(0, candidate);
            kept++;
        }

        // すべての階層が入るなら省略は要らない
        if (kept == segments.Length && Width(path) <= budget) return path;

        var result = head + sep + Ellipsis + tail + sep + fileName;
        if (Width(result) <= budget) return result;

        // 先頭の区切りすら入らない
        return TrimHead(Ellipsis + sep + fileName, budget);
    }

    /// <summary>先頭を削って予算に収める（末尾を優先して残す）</summary>
    private static string TrimHead(string text, int budget)
    {
        var w = Width(text);
        if (w <= budget) return text;
        var i = 0;
        w += Width(Ellipsis);
        while (i < text.Length && w > budget)
        {
            w -= Width(text[i]);
            i++;
        }
        return Ellipsis + text[i..];
    }

    private static int Width(string s)
    {
        var w = 0;
        foreach (var ch in s) w += Width(ch);
        return w;
    }

    // 省略記号は 1 グリフなので 1、それ以外の非 Latin-1 は全角とみなして 2
    private static int Width(char ch) => ch == '…' ? 1 : (ch > 0xFF ? 2 : 1);
}
