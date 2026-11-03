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
using Avalonia.Media;

namespace AttacheCase.ViewModels;

/// <summary>
/// App.axaml のテーマ辞書（App* ブラシ）を ViewModel から現在のテーマで引くための小道具。
/// 画面（axaml）側は DynamicResource で済むが、ViewModel が状態に応じてブラシを選ぶ場合
/// （パスワード確認欄の一致 / 不一致など）はここを通す。テーマ変更時はウィンドウごと
/// 作り直されるので、取得時点のテーマで解決すれば足りる。
/// </summary>
internal static class ThemeBrushes
{
    /// <summary>キーのブラシを現在のテーマで返す。見つからなければ <paramref name="fallback"/> 色の単色。</summary>
    public static IBrush Get(string key, Color fallback)
    {
        var app = Avalonia.Application.Current;
        if (app is not null
            && app.TryGetResource(key, app.ActualThemeVariant, out var value)
            && value is IBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(fallback);
    }
}
