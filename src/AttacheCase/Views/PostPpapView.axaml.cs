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

using AttacheCase.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace AttacheCase.Views;

public partial class PostPpapView : UserControl
{
  public PostPpapView()
  {
    AvaloniaXamlLoader.Load(this);

    // 画面が切り替わった瞬間から、そのままキーを打ち始められるようにする。
    // このビューは AvaloniaXamlLoader を直接呼んでいて x:Name のフィールドが
    // 生成されないため、FindControl で引く
    FocusHelper.TrackFocus(this, () => DataContext switch
    {
      PostPpapViewModel { IsRegistering: true } => this.FindControl<TextBox>("EmailBox"),
      PostPpapViewModel { IsHome: true } => this.FindControl<TextBox>("RecipientBox"),
      _ => null
    });
  }

  private void OnKeyDown(object? sender, KeyEventArgs e)
  {
    if (e.Key == Key.Escape)
    {
      if (DataContext is ViewModels.PostPpapViewModel viewModel)
      {
        viewModel.CancelCommand.Execute(null);
      }
    }
  }
}
