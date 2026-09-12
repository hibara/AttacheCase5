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

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AttacheCase.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 「挿入」メニューから選ばれた書式指定子を、カーソル位置へ差し込む。
    /// カーソル位置の扱いはビュー側の都合なので、ここで完結させる。
    /// </summary>
    private void OnInsertAutoNameToken(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string token }) return;

        var box = this.FindControl<TextBox>("AutoNameFormatBox");
        if (box == null) return;

        var text = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);

        box.Text = text.Insert(caret, token);
        box.CaretIndex = caret + token.Length;
        box.Focus();
    }
}
