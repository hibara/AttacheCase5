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
using AttacheCase.Cli;
using AttacheCase.Models;
using AttacheCase.Platform.Windows;
using AttacheCase.Services;
using AttacheCase.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AttacheCase.Views;

public partial class MainWindow : Window
{
  private readonly IWindowLayoutService _layoutService;

  public MainWindow()
  {
    InitializeComponent();
    AddHandler(DragDrop.DragOverEvent, OnDragOver);
    AddHandler(DragDrop.DropEvent, OnDrop);
    // デザイナー用フォールバック（DIが効かない場合）
    _layoutService = new WindowLayoutService("AttacheCase");
  }

  public MainWindow(IWindowLayoutService layoutService)
  {
    InitializeComponent();
    _layoutService = layoutService;
  }

  protected override void OnOpened(EventArgs e)
  {
    base.OnOpened(e);
    RestoreLayout();
  }

  protected override void OnClosing(WindowClosingEventArgs e)
  {
    base.OnClosing(e);
    SaveLayout();
  }

  /// <summary>
  /// Esc キー。旧版（5.0 以前の WinForms 版、Form1.KeyPreview）と同じく、どの画面でも Esc で
  /// アプリを終了する。処理中（進捗バーが動いているとき）だけは終了ではなく「中止」。
  /// 以前は画面ごとに Esc → 戻る / キャンセルを割り当てていて終了しなかった（利用者指摘 2026-10-25）。
  /// コンボボックスのドロップダウンやメニューが開いているときは、それらが Esc を処理済みにするので
  /// ここへは来ない（開いているものを閉じるだけ）
  /// </summary>
  protected override void OnKeyDown(KeyEventArgs e)
  {
    base.OnKeyDown(e);
    if (e.Handled || e.Key != Key.Escape) return;
    if (DataContext is not MainWindowViewModel viewModel) return;

    e.Handled = true;
    if (viewModel.HandleEscapeKey()) return;
    Close();
  }

  private void RestoreLayout()
  {
    var s = _layoutService.Load();

    // Windows のタブレットモード（旧 5.0 の Form1_Load と同じ扱い）:
    // 設定「PC がタブレットモードで動作しているとき、最大化モードで起動しない」が OFF なら
    // WindowState は触らず OS に任せる（タブレットモードでは OS が全画面にする）。
    // ON なら保存した状態（ふつうは Normal）で起動し、OS に最大化されていたら一度だけ戻す。
    var tabletMode = OperatingSystem.IsWindows() && WindowsTabletMode.IsTabletMode();
    if (tabletMode && !NoMaximizeInTabletMode)
    {
      ApplyNormalBounds(s);
      return;
    }

    if (s.IsMaximized)
    {
      WindowState = WindowState.Maximized;
    }
    else if (s.IsMinimized)
    {
      WindowState = WindowState.Minimized;
    }
    else
    {
      WindowState = WindowState.Normal;
      ApplyNormalBounds(s);

      if (tabletMode)
      {
        Dispatcher.UIThread.Post(() =>
        {
          if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        }, DispatcherPriority.Background);
      }
    }
  }

  /// <summary>保存してある位置・サイズを適用する（位置が無ければ主画面の中央）</summary>
  private void ApplyNormalBounds(WindowLayoutSettings s)
  {
    if (s.Left < 0 || s.Top < 0)
    {
      if (Screens.Primary != null)
      {
        s.Left = Screens.Primary.Bounds.Width / 2 - s.Width / 2;
        s.Top = Screens.Primary.Bounds.Height / 2 - s.Height / 2;
      }
    }

    Position = new Avalonia.PixelPoint((int)s.Left, (int)s.Top);
    Width = s.Width;
    Height = s.Height;
  }

  /// <summary>
  /// 設定「PC がタブレットモードで動作しているとき、最大化モードで起動しない」。
  /// 起動引数の /notmaximized は設定を上書きする（保存はしない。CommandLineStartup は
  /// ウィンドウが開く前に Program.Main が入れている）
  /// </summary>
  private static bool NoMaximizeInTabletMode =>
    CommandLineStartup.Options?.NoMaximizeInTabletMode
    ?? SettingsService.Instance.Current.NoMaximizeInTabletMode;

  private void SaveLayout()
  {
    var s = new WindowLayoutSettings
    {
      IsMaximized = WindowState == WindowState.Maximized,
      IsMinimized = WindowState == WindowState.Minimized,
    };

    // 最大化・最小化時は復元用の位置/サイズを保持したいので、
    // Normal 状態のときだけ位置・サイズを記録する
    if (WindowState == WindowState.Normal)
    {
      s.Left = Position.X;
      s.Top = Position.Y;
      s.Width = Width;
      s.Height = Height;
    }
    else
    {
      // 直前の Normal 時の値を維持するため、既存設定をベースにする
      var prev = _layoutService.Load();
      s.Left = prev.Left;
      s.Top = prev.Top;
      s.Width = prev.Width;
      s.Height = prev.Height;
    }

    _layoutService.Save(s);
  }

  private void OnDragOver(object? sender, DragEventArgs e)
  {
    // DataContextがMainWindowViewModelかチェック
    if (DataContext is not MainWindowViewModel viewModel)
    {
      e.DragEffects = DragDropEffects.None;
      return;
    }

    // ファイルが含まれているかチェック
    if (!e.DataTransfer.Contains(DataFormat.File))
    {
      e.DragEffects = DragDropEffects.None;
      return;
    }

    // ファイルパスを取得
    var files = e.DataTransfer.TryGetFiles();
    if (files == null)
    {
      e.DragEffects = DragDropEffects.None;
      return;
    }

    var paths = files.Select(f => f.Path.LocalPath).ToList();

    // ViewModelのCanAcceptDropで判定
    if (viewModel.CanAcceptDrop(paths))
      e.DragEffects = DragDropEffects.Copy;
    else
      e.DragEffects = DragDropEffects.None;
  }

  private void OnDrop(object? sender, DragEventArgs e)
  {
    // DataContextがMainWindowViewModelかチェック
    if (DataContext is not MainWindowViewModel viewModel) return;

    // ファイルが含まれているかチェック
    if (!e.DataTransfer.Contains(DataFormat.File)) return;

    // ファイルパスを取得
    var files = e.DataTransfer.TryGetFiles();
    if (files == null) return;

    // ドロップ元（Finder など）がアクティブなままだと、画面が切り替わっても
    // キー入力がこちらに来ないので、ウィンドウを前面に出してから処理する
    Activate();

    var paths = files.Select(f => f.Path.LocalPath).ToList();

    // ViewModelのOnFilesDroppedを呼び出し
    viewModel.OnFilesDropped(paths);
  }

}
