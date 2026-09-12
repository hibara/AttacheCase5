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
// 更新の進み具合を見せるウィンドウ。
//
// 進捗バーの見た目は暗号化・復号の画面にそろえてある。
// 閉じるボタンでも「中止」と同じ扱いにする。閉じたのに裏で処理が続いていると、
// 何が起きているのか分からなくなるため。
//----------------------------------------------------------------------

using System;
using System.Threading;
using AttacheCase.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AttacheCase.Views;

public partial class UpdateProgressWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();

    public UpdateProgressWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var localize = LocalizationService.Instance;
        Title = localize["Update_Title"];

        var cancelButton = this.FindControl<Button>("CancelButton");
        if (cancelButton != null)
        {
            cancelButton.Content = localize["Update_Cancel"];
        }

        Closing += OnClosing;
    }

    /// <summary>中止の合図。ダウンロードや検証はこれを見て止まる。</summary>
    public CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>利用者が中止したか。</summary>
    public bool IsCancelled => _cancellation.IsCancellationRequested;

    /// <summary>上段の説明を差し替える。</summary>
    public void SetStatus(string text) =>
        Dispatcher.UIThread.Post(() =>
        {
            var status = this.FindControl<TextBlock>("StatusText");
            if (status != null) status.Text = text;
        });

    /// <summary>
    /// 進み具合を差し替える。<paramref name="percent"/> が null なら
    /// 量の分からない処理として扱う。
    /// </summary>
    public void SetProgress(double? percent, string? detail = null) =>
        Dispatcher.UIThread.Post(() =>
        {
            var bar = this.FindControl<ProgressBar>("Progress");
            if (bar != null)
            {
                bar.IsIndeterminate = percent is null;
                if (percent is not null)
                {
                    bar.Value = Math.Clamp(percent.Value, 0, 100);
                }
            }

            var detailText = this.FindControl<TextBlock>("DetailText");
            if (detailText != null)
            {
                detailText.Text = detail ?? string.Empty;
            }
        });

    /// <summary>
    /// 処理が終わったので閉じる。中止の合図は立てない。
    ///
    /// UI スレッド上では**その場で**閉じる。Post で後回しにすると、呼び出し側が続けて
    /// 開く通知ダイアログ（ShowDialog でメインウィンドウを無効化する）より後に
    /// この所有窓が閉じることになり、Windows が所有者（無効化済みのメイン）へ
    /// アクティブを戻そうとして通知ダイアログが背面へ落ち、進行不能になる
    /// （2026-09-12 に更新の通し試験で実測）。
    /// </summary>
    public void Finish()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            FinishCore();
        }
        else
        {
            Dispatcher.UIThread.Post(FinishCore);
        }
    }

    private void FinishCore()
    {
        Closing -= OnClosing;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _cancellation.Cancel();
        Closing -= OnClosing;
        Close();
    }

    /// <summary>
    /// 閉じるボタンで消されたときも中止として扱う。裏で処理が続くのを避ける。
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _cancellation.Cancel();
    }
}
