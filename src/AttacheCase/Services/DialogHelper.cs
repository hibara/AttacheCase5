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
using System.Threading.Tasks;
using AttacheCase.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace AttacheCase.Services;

/// <summary>
/// メッセージダイアログをメインウィンドウ所有のモーダルとして表示するためのヘルパー。
/// 非モーダルだと親ウィンドウのクリックで奥に隠れたり消えたりするため、
/// ShowDialog(owner) でモーダル化する。
/// </summary>
public static class DialogHelper
{
    /// <summary>
    /// 現在のアプリのメインウィンドウを取得する。デスクトップ以外の環境では null を返す。
    /// </summary>
    public static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    /// <summary>
    /// 利用者への通知。無人実行（/exit=1）ではダイアログを出さず標準エラー出力に書く。
    /// モーダルは誰も押さないと永遠に待つため、無人実行では出してはいけない。
    /// </summary>
    public static async Task NotifyAsync(bool unattended, string title, string message, DialogIcon icon = DialogIcon.Error)
    {
        if (unattended)
        {
            Console.Error.WriteLine(title + ": " + message);
            return;
        }

        // 「処理中は最小化する」設定で隠れたままだと気づけないので、戻してから表示する
        WindowBehaviorService.Instance.RestoreForDialog();

        await MessageDialogWindow.ShowNotifyAsync(GetMainWindow(), title, message, icon);
    }

    /// <summary>
    /// 「はい／いいえ」の確認。無人実行では確認できないので「いいえ」を選んだものとして扱う。
    /// </summary>
    public static async Task<bool> ConfirmAsync(bool unattended, string title, string message)
    {
        if (unattended)
        {
            Console.Error.WriteLine(title + ": " + message);
            return false;
        }

        WindowBehaviorService.Instance.RestoreForDialog();

        return await MessageDialogWindow.ShowConfirmAsync(GetMainWindow(), title, message);
    }
}
