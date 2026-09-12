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
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace AttacheCase.Views;

/// <summary>
/// ステップ（画面）が切り替わったときに、入力すべきコントロールへ自動でフォーカスを移すヘルパー。
/// ドラッグ＆ドロップでいきなりパスワード入力画面が現れる作りなので、
/// 何もしないとマウスでテキストボックスをクリックしないとタイプできない。
/// </summary>
internal static class FocusHelper
{
  /// <summary>
  /// DataContext（ViewModel）のプロパティ変更を監視し、
  /// resolve が返したコントロールへフォーカスを移す。
  /// resolve が null を返す間、または前回と同じコントロールのときは何もしない
  /// （利用者が自分で他のコントロールを触っている最中にフォーカスを奪い返さないため）。
  /// </summary>
  public static void TrackFocus(UserControl view, Func<Control?> resolve)
  {
    INotifyPropertyChanged? subscribed = null;
    Control? last = null;

    void Apply()
    {
      var target = resolve();
      if (target == null)
      {
        last = null;
        return;
      }

      if (ReferenceEquals(target, last)) return;
      last = target;

      // IsVisible を切り替えた直後はまだレイアウトが済んでおらず Focus() が効かないため、
      // レイアウト後（Loaded 優先度）に実行されるよう遅らせる
      Dispatcher.UIThread.Post(() =>
      {
        if (!target.IsEffectivelyVisible || !target.IsEffectivelyEnabled) return;

        // テキストボックスは Tab 移動扱いにすると全選択されてしまうので、
        // フォーカスだけ移してキャレットを末尾に置く
        if (target is TextBox textBox)
        {
          textBox.Focus(NavigationMethod.Unspecified);
          textBox.CaretIndex = textBox.Text?.Length ?? 0;
        }
        else
        {
          target.Focus(NavigationMethod.Tab);
        }
      }, DispatcherPriority.Loaded);
    }

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    void Unsubscribe()
    {
      if (subscribed == null) return;
      subscribed.PropertyChanged -= OnViewModelPropertyChanged;
      subscribed = null;
    }

    void Subscribe()
    {
      Unsubscribe();
      subscribed = view.DataContext as INotifyPropertyChanged;
      if (subscribed != null) subscribed.PropertyChanged += OnViewModelPropertyChanged;
      last = null;
      Apply();
    }

    view.DataContextChanged += (_, _) => Subscribe();

    // 外れたときに購読を切っているので、戻ってきたら必ず張り直す。
    // DataContext が同じままだと DataContextChanged は起きない
    view.AttachedToVisualTree += (_, _) => Subscribe();

    view.DetachedFromVisualTree += (_, _) => Unsubscribe();
  }
}
