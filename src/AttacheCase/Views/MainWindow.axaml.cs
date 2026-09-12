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
using AttacheCase.Models;
using AttacheCase.Services;
using AttacheCase.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;

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

  private void RestoreLayout()
  {
    var s = _layoutService.Load();

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
  }

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