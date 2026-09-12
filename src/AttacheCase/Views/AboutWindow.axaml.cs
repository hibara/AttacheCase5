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
using System.Reflection;
using AttacheCase.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AttacheCase.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // 所有モーダルは最小化するとタスクバーに出ず行方不明になるため、
        // タイトルバーを閉じる（×）だけにする（Windows のみ）
        Opened += (_, _) => Platform.Windows.WindowsDialogChrome.MakeCloseOnly(this);

        try
        {
            var localize = LocalizationService.Instance;

            // タイトル
            Title = localize["About_Title"];

            // アイコン: Windows では旧 Windows 版のアイコンに差し替える
            // （axaml の既定は macOS 用の main-icon_64x64.png）
            if (OperatingSystem.IsWindows())
            {
                var iconImage = this.FindControl<Image>("AppIconImage");
                if (iconImage != null)
                {
                    using var stream = Avalonia.Platform.AssetLoader.Open(
                        new Uri("avares://AttacheCase/Assets/main-icon-win_64x64.png"));
                    iconImage.Source = new Avalonia.Media.Imaging.Bitmap(stream);
                }
            }

            // バージョン取得（Windows は 4 桁、macOS は 3 桁。AppVersion.Display が分岐）
            var versionString = AppVersion.Display;

            // コントロールを取得してローカライズ適用
            var versionText = this.FindControl<TextBlock>("VersionText");
            if (versionText != null)
            {
                versionText.Text = localize.GetString("About_Version", versionString);
            }

            var copyrightText = this.FindControl<TextBlock>("CopyrightText");
            if (copyrightText != null)
            {
                copyrightText.Text = localize["About_Copyright"];
            }

            // ライセンス表示。MIT などは「著作権表示と許諾表示を配布物に含めること」を
            // 条件にしているので、アプリの中から読めなければならない
            var licenseButton = this.FindControl<Button>("LicenseButton");
            if (licenseButton != null)
            {
                licenseButton.Content = localize["About_License"];
                licenseButton.Click += OnLicenseButtonClick;
            }

            var closeButton = this.FindControl<Button>("CloseButton");
            if (closeButton != null)
            {
                closeButton.Content = localize["About_Close"];
                closeButton.Click += OnCloseButtonClick;
            }

            // 商用利用登録（未登録のときだけ表示されるボタン）
            var registrationButton = this.FindControl<Button>("CommercialRegisterButton");
            if (registrationButton != null)
            {
                registrationButton.Click += OnLicenseRegistrationButtonClick;
            }

            // 隠し機能: ライセンスアイコンにカーソルを載せると指カーソルになり、
            // 右クリックで「商用ライセンスを削除」のポップアップメニューが出る
            // （旧版準拠。確認ダイアログは入れ子を避けるため App 側で出す）
            var licenseIcon = this.FindControl<Image>("LicenseIconImage");
            if (licenseIcon != null)
            {
                licenseIcon.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                var deleteItem = new MenuItem { Header = localize["About_DeleteLicenseMenu"] };
                deleteItem.Click += (_, _) => Close(AboutAction.ConfirmDeleteLicense);
                licenseIcon.ContextMenu = new ContextMenu { Items = { deleteItem } };
            }

            RefreshRegistrationStatus();
        }
        catch (Exception ex)
        {
            // ローカライズに失敗してもウィンドウは表示
            System.Diagnostics.Debug.WriteLine($"AboutWindow initialization error: {ex.Message}");
        }
    }

    /// <summary>「バージョン情報」を閉じたあと、続けて行う操作。App 側が受け取って処理する。</summary>
    public enum AboutAction { None = 0, ShowLicense, ShowRegistration, ConfirmDeleteLicense }

    private void OnLicenseButtonClick(object? sender, RoutedEventArgs e)
    {
        // 入れ子モーダル（バージョン情報の中でモーダルを開く）は Windows で操作不能に
        // なるため、いったんバージョン情報を閉じ、呼び出し元（App）がメインウィンドウ
        // 所有でライセンス画面を開き直す。
        Close(AboutAction.ShowLicense);
    }

    private void OnLicenseRegistrationButtonClick(object? sender, RoutedEventArgs e)
    {
        // 同上。登録コード入力にバージョン情報の内容は不要なので、閉じてから開く。
        Close(AboutAction.ShowRegistration);
    }

    /// <summary>
    /// 商用ライセンスの登録状態を「バージョン情報」内に表示する（旧版準拠）。
    /// 未登録 = 赤字の通知と「商用利用登録」ボタン。
    /// 登録済み = 商用利用パネル（ユーザー名 / E-mail）。登録画面はもう出さない。
    /// </summary>
    private void RefreshRegistrationStatus()
    {
        var freePanel = this.FindControl<StackPanel>("FreeVersionPanel");
        var registeredPanel = this.FindControl<Border>("RegisteredPanel");
        if (freePanel == null || registeredPanel == null) return;

        var registered = false;
        try
        {
            var license = new LicenseRegister();
            if (license.GetCommercialLicense())
            {
                registered = true;
                var userNameText = this.FindControl<TextBlock>("LicUserNameText");
                if (userNameText != null) userNameText.Text = license.UserNameString;
                var emailText = this.FindControl<TextBlock>("LicEmailText");
                if (emailText != null) emailText.Text = license.EmailAddressString;
            }
        }
        catch (Exception)
        {
            // 読めなければ未登録扱い
        }

        freePanel.IsVisible = !registered;
        registeredPanel.IsVisible = registered;
    }

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        Close(AboutAction.None);
    }
}
