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
using System.Linq;
using System.Threading.Tasks;
using AttacheCase.Services;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AttacheCase.Views;

/// <summary>
/// 通知（OK）と確認（はい／いいえ）のメッセージダイアログ。
/// MessageBox.Avalonia はボタンへフォーカスを当てられず、
/// キーボードだけで応答できなかったため自前で用意している。
/// 確認ダイアログは安全側の「いいえ」にフォーカスを置き、←→ で選んで Enter で決定する。
/// </summary>
public partial class MessageDialogWindow : Window
{
  private readonly TaskCompletionSource<bool> _completion = new();
  private readonly Button _defaultButton;
  private bool _result;

  /// <summary>デザイナ用</summary>
  public MessageDialogWindow() : this(string.Empty, string.Empty, DialogIcon.Info, false)
  {
  }

  private MessageDialogWindow(string title, string message, DialogIcon icon, bool isConfirm)
  {
    InitializeComponent();

    var localize = LocalizationService.Instance;

    Title = title;
    MessageText.Text = message;
    var iconImage = icon switch
    {
      DialogIcon.Question => IconQuestion,
      DialogIcon.Warning => IconWarning,
      DialogIcon.Error => IconError,
      _ => IconInfo
    };
    iconImage.IsVisible = true;

    YesButton.Content = localize["Button_Yes"];
    NoButton.Content = localize["Button_No"];
    OkButton.Content = localize["Button_OK"];

    YesButton.IsVisible = isConfirm;
    NoButton.IsVisible = isConfirm;
    OkButton.IsVisible = !isConfirm;

    // 確認ダイアログの初期フォーカスは安全側（いいえ）に置く
    _defaultButton = isConfirm ? NoButton : OkButton;

    // ボタン自身がキーを処理する前に受け取りたいのでトンネリングで拾う
    AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);

    Opened += (_, _) =>
    {
      // 前面に出す。直前に閉じた所有窓の後始末でアクティブが所有者へ戻ると、
      // このダイアログが背面へ落ちて操作不能になることがある（更新の通し試験で実測）
      Activate();
      _defaultButton.Focus(NavigationMethod.Tab);
    };
    Closed += (_, _) => _completion.TrySetResult(_result);
  }

  /// <summary>
  /// 通知（OK ボタンのみ）を表示する。
  /// </summary>
  public static Task ShowNotifyAsync(Window? owner, string title, string message, DialogIcon icon)
    => ShowAsync(owner, title, message, icon, isConfirm: false);

  /// <summary>
  /// 「はい／いいえ」の確認を表示する。はいなら true。
  /// </summary>
  public static Task<bool> ShowConfirmAsync(Window? owner, string title, string message)
    => ShowAsync(owner, title, message, DialogIcon.Question, isConfirm: true);

  private static Task<bool> ShowAsync(Window? owner, string title, string message, DialogIcon icon, bool isConfirm)
  {
    var dialog = new MessageDialogWindow(title, message, icon, isConfirm);

    if (owner != null)
      _ = dialog.ShowDialog(owner);
    else
      dialog.Show();

    return dialog._completion.Task;
  }

  private void OnDialogKeyDown(object? sender, KeyEventArgs e)
  {
    switch (e.Key)
    {
      case Key.Left:
      case Key.Right:
        MoveFocus(e.Key == Key.Left ? -1 : 1);
        e.Handled = true;
        break;

      case Key.Enter:
        // フォーカスのあるボタンで決定する（フォーカスが外れていれば既定のボタン）
        Commit(FocusedButton() ?? _defaultButton);
        e.Handled = true;
        break;

      case Key.Escape:
        // Esc は打ち消し側。通知ダイアログでは閉じるだけ
        Commit(NoButton.IsVisible ? NoButton : OkButton);
        e.Handled = true;
        break;
    }
  }

  private Button? FocusedButton()
    => FocusManager?.GetFocusedElement() as Button;

  private void MoveFocus(int delta)
  {
    var buttons = new[] { YesButton, NoButton, OkButton }.Where(b => b.IsVisible).ToArray();
    if (buttons.Length < 2) return;

    var index = Array.IndexOf(buttons, FocusedButton());
    if (index < 0) index = 0;

    var next = buttons[(index + delta + buttons.Length) % buttons.Length];
    next.Focus(NavigationMethod.Tab);
  }

  private void Commit(Button button)
  {
    _result = ReferenceEquals(button, YesButton);
    Close();
  }

  private void OnYesClick(object? sender, RoutedEventArgs e) => Commit(YesButton);
  private void OnNoClick(object? sender, RoutedEventArgs e) => Commit(NoButton);
  private void OnOkClick(object? sender, RoutedEventArgs e) => Commit(OkButton);
}
