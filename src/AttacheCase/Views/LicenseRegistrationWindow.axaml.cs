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
        if (registerButton != null)
        {
            registerButton.Click += OnRegisterClick;
            // 有効なコードが入るまでは押せない
            registerButton.IsEnabled = false;
        }

        // 貼り付けた（打ち替えた）時点で有効・無効を判定して表示する
        var codeTextBox = this.FindControl<TextBox>("CodeTextBox");
        if (codeTextBox != null)
        {
            codeTextBox.TextChanged += (_, _) => ValidateCode();
        }

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

    /// <summary>
    /// 入力欄のコードを（保存せずに）検証し、結果を表示する。
    /// 有効なら緑字のお礼、無効なら赤字、空欄なら何も出さない。
    /// 「登録」ボタンは有効なコードのときだけ押せる。
    /// </summary>
    private bool ValidateCode()
    {
        var codeTextBox = this.FindControl<TextBox>("CodeTextBox");
        var resultText = this.FindControl<TextBlock>("ResultText");
        var registerButton = this.FindControl<Button>("RegisterButton");
        if (codeTextBox == null || resultText == null) return false;

        var code = codeTextBox.Text?.Trim() ?? "";

        // 空欄で Decrypt を呼ぶと保存済みのコードを読みに行くので、先に弾く
        var isValid = code.Length > 0 && new LicenseRegister(code).Decrypt(fRegister: false);

        if (code.Length == 0)
        {
            resultText.Text = "";
        }
        else if (isValid)
        {
            resultText.Text = Localize["LicenseReg_Success"];
            resultText.Foreground = ThemeBrush("AppSuccessForeground", Brushes.Green);
        }
        else
        {
            resultText.Text = Localize["LicenseReg_Invalid"];
            resultText.Foreground = ThemeBrush("AppErrorForeground", Brushes.Red);
        }

        if (registerButton != null) registerButton.IsEnabled = isValid;
        return isValid;
    }

    /// <summary>App.axaml のテーマ辞書（ライト／ダーク）から色を引く。</summary>
    private IBrush ThemeBrush(string key, IBrush fallback)
    {
        return this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : fallback;
    }

    private void OnRegisterClick(object? sender, RoutedEventArgs e)
    {
        var codeTextBox = this.FindControl<TextBox>("CodeTextBox");
        if (codeTextBox == null || !ValidateCode()) return;

        var code = codeTextBox.Text?.Trim() ?? "";
        if (new LicenseRegister(code).Decrypt(fRegister: true))
        {
            // 登録できたら、そのままバージョン情報へ戻る
            // （App.OnAboutMenuClick がこの画面を閉じたあとに開き直す。登録済み表示になる）
            Close();
        }
    }

}
