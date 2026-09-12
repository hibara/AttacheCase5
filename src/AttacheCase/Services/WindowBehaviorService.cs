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
// 設定「ウィンドウ」の反映。
//   - 最前面表示（Topmost）
//   - メニューバーアイコン（macOS の NSStatusItem = Avalonia の TrayIcon）
//   - 処理中の最小化と、Dock アイコンへの進捗表示
//
// 設定の保存時にも呼ばれ、その場で見た目へ反映する。
//----------------------------------------------------------------------

using System;
using AttacheCase.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AttacheCase.Services;

public sealed class WindowBehaviorService
{
    public static WindowBehaviorService Instance { get; } = new();

    private readonly LocalizationService _localize = LocalizationService.Instance;

    private Window? _window;
    private TrayIcon? _trayIcon;

    /// <summary>処理のために最小化したので、終わったら戻す必要がある</summary>
    private bool _minimizedForProcessing;

    /// <summary>Dock アイコンに進捗を出している最中か（最小化する設定のときだけ出す）</summary>
    private bool _showingDockProgress;

    /// <summary>設定の保存イベントを購読済みか</summary>
    private bool _subscribed;

    private WindowBehaviorService()
    {
    }

    /// <summary>
    /// メインウィンドウに結びつけ、保存されている設定を反映する。
    /// 以後、設定が保存されるたびに反映し直す。
    /// </summary>
    public void Attach(Window window, SettingsService settingsService)
    {
        _window = window;

        Apply(settingsService.Current);

        // 言語変更でウィンドウを作り直すことがあるので、購読は一度だけにする
        if (_subscribed) return;

        _subscribed = true;
        settingsService.Saved += (_, settings) => Apply(settings);
    }

    /// <summary>設定をウィンドウの見た目へ反映する</summary>
    public void Apply(AppSettings settings)
    {
        if (_window != null)
        {
            _window.Topmost = settings.WindowForeground;
        }

        UpdateMenuBarIcon(settings.ShowMenuBarIcon);
    }

    /// <summary>
    /// 処理を始めるときに呼ぶ。設定に応じてウィンドウを最小化し、
    /// Dock アイコンに進捗を出し始める。
    /// </summary>
    public void BeginProcessing(RunContext context)
    {
        // Windows: タスクバーボタンの進捗ゲージは最小化と無関係の標準 UX なので、
        // 処理中は常に出す（Windows 版本体と同じ）
        if (OperatingSystem.IsWindows())
        {
            _showingTaskbarProgress = true;
            Platform.Windows.WindowsTaskbarProgress.SetProgress(GetWindowHandle(), 0);
        }

        // ウィンドウが隠れて様子が分からなくなるので、最小化する設定のときだけ
        // Dock アイコンに進捗を出す
        if (!context.Settings.MinimizeWhileProcessing) return;

        if (OperatingSystem.IsMacOS())
        {
            _showingDockProgress = true;
            MacDockTile.SetProgress(0);
        }

        if (_window == null) return;

        _minimizedForProcessing = true;
        _window.WindowState = WindowState.Minimized;
    }

    /// <summary>Windows のタスクバー進捗を出している最中か</summary>
    private bool _showingTaskbarProgress;

    private IntPtr GetWindowHandle()
    {
        try
        {
            return _window?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>進捗（0〜100）を Dock アイコンへ反映する</summary>
    public void ReportProgress(int percent)
    {
        if (_showingTaskbarProgress && OperatingSystem.IsWindows())
        {
            Platform.Windows.WindowsTaskbarProgress.SetProgress(GetWindowHandle(), percent);
        }

        if (!_showingDockProgress) return;

        MacDockTile.SetProgress(percent);
    }

    /// <summary>処理が終わったときに呼ぶ</summary>
    public void EndProcessing()
    {
        if (_showingTaskbarProgress && OperatingSystem.IsWindows())
        {
            _showingTaskbarProgress = false;
            Platform.Windows.WindowsTaskbarProgress.Clear(GetWindowHandle());
        }

        if (_showingDockProgress)
        {
            _showingDockProgress = false;
            MacDockTile.Clear();
        }

        RestoreWindow();
    }

    /// <summary>
    /// ダイアログを出す前に呼ぶ。最小化したままだと利用者が気づけないため、
    /// ウィンドウを戻してから表示する。
    /// </summary>
    public void RestoreForDialog() => RestoreWindow();

    /// <summary>メニューバーアイコンなどからウィンドウを前面に出す</summary>
    public void ActivateWindow()
    {
        if (_window == null) return;

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Show();
        _window.Activate();
    }

    private void RestoreWindow()
    {
        if (!_minimizedForProcessing || _window == null) return;

        _minimizedForProcessing = false;
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void UpdateMenuBarIcon(bool show)
    {
        if (!show)
        {
            if (_trayIcon == null) return;

            _trayIcon.IsVisible = false;
            return;
        }

        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = true;
            return;
        }

        if (Application.Current == null) return;

        try
        {
            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(
                    new Uri("avares://AttacheCase/Assets/Icons/menubar-icon.png"))),
                ToolTipText = _localize["AppName"],
                IsVisible = true,
            };

            // クリックでウィンドウを前面に出すだけ（メニューは持たせない）
            _trayIcon.Clicked += (_, _) => ActivateWindow();

            TrayIcon.SetIcons(Application.Current, [_trayIcon]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not create the menu bar icon: " + ex.Message);
            _trayIcon = null;
        }
    }
}
