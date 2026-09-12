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
// 商用ライセンス（レジストレーションコード）の登録・削除画面。
// Windows 版 Form2 のライセンス登録部の Avalonia 版。
// 検証・保存の実体は Core の LicenseRegister（Windows: レジストリ /
// その他: 設定ディレクトリのファイル）。
//----------------------------------------------------------------------

using System;
using AttacheCase.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AttacheCase.Views;

public partial class LicenseRegistrationWindow : Window
{
    private static readonly LocalizationService Localize = LocalizationService.Instance;

    public LicenseRegistrationWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // 所有モーダルは最小化するとタスクバーに出ず行方不明になるため、
        // タイトルバーを閉じる（×）だけにする（Windows のみ）
        Opened += (_, _) => Platform.Windows.WindowsDialogChrome.MakeCloseOnly(this);

        var registerButton = this.FindControl<Button>("RegisterButton");
        if (registerButton != null) registerButton.Click += OnRegisterClick;

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton != null) closeButton.Click += (_, _) => Close();

        RefreshStatus();
    }

    /// <summary>現在の登録状態を表示へ反映する。</summary>
    private void RefreshStatus()
    {
        var statusText = this.FindControl<TextBlock>("StatusText");
        if (statusText == null) return;

        var license = new LicenseRegister();
        statusText.Text = license.GetCommercialLicense()
            ? Localize.GetString(
                "LicenseReg_StatusRegistered", license.UserNameString, license.EmailAddressString)
            : Localize["LicenseReg_StatusNotRegistered"];
    }

    private void OnRegisterClick(object? sender, RoutedEventArgs e)
    {
        var codeTextBox = this.FindControl<TextBox>("CodeTextBox");
        var resultText = this.FindControl<TextBlock>("ResultText");
        if (codeTextBox == null || resultText == null) return;

        var code = codeTextBox.Text?.Trim() ?? "";
        var license = new LicenseRegister(code);

        if (!string.IsNullOrEmpty(code) && license.Decrypt(fRegister: true))
        {
            resultText.Text = Localize["LicenseReg_Success"];
            resultText.Foreground = Brushes.Green;
            codeTextBox.Text = "";

            // 登録完了。もう一度押せると空欄のまま「コードが正しくありません」に
            // なってしまうので、入力欄ごと無効化する（あとは閉じるだけ）
            codeTextBox.IsEnabled = false;
            if (sender is Button registerButton) registerButton.IsEnabled = false;

            RefreshStatus();
        }
        else
        {
            resultText.Text = Localize["LicenseReg_Invalid"];
            resultText.Foreground = Brushes.Red;
        }
    }

}
