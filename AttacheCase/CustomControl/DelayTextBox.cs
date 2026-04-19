//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace AttacheCase.CustomControl
{
  /// <summary>
  /// テキスト変更後、指定ミリ秒だけ待ってから TextChanged を発火する TextBox。
  /// キー入力中は TextChanged を抑制し、入力が止まったタイミングで通知する。
  /// </summary>
  [ToolboxItem(true)]
  [DesignerCategory("Code")]
  public class DelayTextBox : TextBox
  {
    private Timer _delayTimer;
    private bool _timerElapsed;
    private bool _keysPressed;
    private int _delay = 200;

    /// <summary>
    /// テキスト変更イベントを発火するまでの遅延時間（ミリ秒）
    /// </summary>
    [DefaultValue(200)]
    [Category("Behavior")]
    [Description("テキスト変更イベントを発火するまでの遅延時間（ミリ秒）")]
    public int Delay
    {
      get => _delay;
      set
      {
        _delay = Math.Max(0, value);
        if (_delayTimer != null)
        {
          _delayTimer.Interval = _delay;
        }
      }
    }

    public DelayTextBox()
    {
      // コンストラクタでは何もしない（デザイナーでの事故防止）
    }

    /// <summary>
    /// タイマーを実行時のみ、必要最小限のタイミングで生成する
    /// </summary>
    private void EnsureTimerInitialized()
    {
      if (IsInDesignMode()) return;
      if (_delayTimer != null) return;

      _delayTimer = new Timer();
      _delayTimer.Interval = _delay;
      _delayTimer.Tick += DelayTimer_Tick;
    }

    private void DelayTimer_Tick(object sender, EventArgs e)
    {
      _delayTimer.Stop();
      _timerElapsed = true;
      OnTextChanged(EventArgs.Empty);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
      if (IsInDesignMode())
      {
        base.OnKeyPress(e);
        return;
      }

      // 実行時に初めてキーが押されたタイミングでタイマーを生成
      EnsureTimerInitialized();

      _keysPressed = true;
      _delayTimer.Stop();
      _delayTimer.Start();

      base.OnKeyPress(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
      if (IsInDesignMode())
      {
        base.OnTextChanged(e);
        return;
      }

      // タイマー経過、またはコードからの変更（キー入力以外）は即座に通知
      if (_timerElapsed || !_keysPressed)
      {
        _timerElapsed = false;
        _keysPressed = false;
        base.OnTextChanged(e);
      }
      // キー入力中の場合は、タイマーの Tick によって OnTextChanged が呼ばれるのを待つ
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        if (_delayTimer != null)
        {
          _delayTimer.Stop();
          _delayTimer.Tick -= DelayTimer_Tick;
          _delayTimer.Dispose();
          _delayTimer = null;
        }
      }
      base.Dispose(disposing);
    }

    /// <summary>
    /// デザインモード判定（デザイナーで安全に動作するシンプルな実装）
    /// </summary>
    private bool IsInDesignMode()
    {
      // 1. 標準の DesignMode プロパティ
      if (DesignMode) return true;

      // 2. LicenseManager による判定（コンストラクタ中にも有効）
      if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return true;

      // 3. Site 経由の判定（親コントロールがある場合に有効）
      if (Site != null && Site.DesignMode) return true;

      return false;
    }
  }
}
