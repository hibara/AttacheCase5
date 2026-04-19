//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AttacheCase.CustomControl
{
  /// <summary>
  /// パスワード表示切り替えアイコン付きの DelayTextBox。
  /// 表示状態が切り替わった際に IsPasswordVisibleChanged イベントを発火する。
  /// </summary>
  [ToolboxItem(true)]
  [DesignerCategory("Code")]
  public class EyeDelayPasswordTextBox : DelayTextBox
  {
    private Button _btnEye;
    private bool _isPasswordVisible = false;

    // テーマ対応用ベース画像
    private static Bitmap _baseEyeIcon;
    private static Bitmap _baseBlindIcon;

    private const int EM_SETMARGINS = 0xd3;
    private const int EC_RIGHTMARGIN = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

    /// <summary>
    /// パスワードの表示/非表示状態が切り替わったときに発生します。
    /// </summary>
    [Category("Property Changed")]
    [Description("パスワードの表示状態が切り替わったときに発生します。")]
    public event EventHandler IsPasswordVisibleChanged;

    /// <summary>
    /// パスワードを表示するかどうか
    /// </summary>
    [Category("Appearance")]
    [Description("パスワードを最初から表示するか、伏せ字にするかを設定します。")]
    [DefaultValue(false)]
    public bool IsPasswordVisible
    {
      get => _isPasswordVisible;
      set
      {
        if (_isPasswordVisible != value)
        {
          _isPasswordVisible = value;
          ApplyPasswordState();
          // イベントを発火
          OnIsPasswordVisibleChanged(EventArgs.Empty);
        }
      }
    }

    public EyeDelayPasswordTextBox()
    {
      InitializeEyeButton();
      ApplyPasswordState();
    }

    protected virtual void OnIsPasswordVisibleChanged(EventArgs e)
    {
      IsPasswordVisibleChanged?.Invoke(this, e);
    }

    private void InitializeEyeButton()
    {
      _btnEye = new Button();
      _btnEye.Size = new Size(26, this.ClientSize.Height);
      _btnEye.Dock = DockStyle.Right;
      _btnEye.Cursor = Cursors.Default;
      _btnEye.FlatStyle = FlatStyle.Flat;
      _btnEye.FlatAppearance.BorderSize = 0;
      _btnEye.FlatAppearance.MouseDownBackColor = Color.Transparent;
      _btnEye.FlatAppearance.MouseOverBackColor = Color.Transparent;
      _btnEye.BackColor = Color.Transparent;

      _btnEye.Click += BtnEye_Click;
      this.Controls.Add(_btnEye);

      this.HandleCreated += (s, e) => SetRightMargin();
    }

    private void ApplyPasswordState()
    {
      this.PasswordChar = _isPasswordVisible ? '\0' : '●';
      UpdateEyeIcon();
    }

    private void SetRightMargin()
    {
      if (_btnEye == null) return;
      var margin = _btnEye.Width + 2;
      SendMessage(this.Handle, EM_SETMARGINS, (IntPtr)EC_RIGHTMARGIN, (IntPtr)(margin << 16));
    }

    private void BtnEye_Click(object sender, EventArgs e)
    {
      IsPasswordVisible = !IsPasswordVisible;
      this.Focus();
      this.SelectionStart = this.Text.Length;
    }

    private void UpdateEyeIcon()
    {
      if (_btnEye == null) return;

      try
      {
        EnsureBaseIcons();
        var baseIcon = _isPasswordVisible ? _baseEyeIcon : _baseBlindIcon;

        if (baseIcon != null)
        {
          _btnEye.Image = ThemeIconManager.Get(baseIcon, ThemeIconManager.IconState.Normal);
          _btnEye.Text = "";
        }
        else
        {
          _btnEye.Text = _isPasswordVisible ? "/" : "*";
        }
      }
      catch
      {
        _btnEye.Text = _isPasswordVisible ? "/" : "*";
      }
    }

    /// <summary>
    /// ベース画像を Properties.Resources から一度だけ読み込む。
    /// </summary>
    private static void EnsureBaseIcons()
    {
      if (_baseEyeIcon != null) return;
      _baseEyeIcon = Properties.Resources.icon_eye;
      _baseBlindIcon = Properties.Resources.icon_blind;
    }

    /// <summary>
    /// テーマ変更時に呼び出してアイコンの色を更新する。
    /// </summary>
    public void RefreshTheme()
    {
      UpdateEyeIcon();
    }

    protected override void OnResize(EventArgs e)
    {
      base.OnResize(e);
      if (_btnEye != null)
      {
        _btnEye.Height = this.ClientSize.Height;
        SetRightMargin();
      }
    }
  }
}