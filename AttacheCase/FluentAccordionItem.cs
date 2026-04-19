//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AttacheCase.Properties;

namespace AttacheCase
{
  public sealed class FluentAccordionItem : UserControl
  {
    private readonly Panel _headerPanel;
    private readonly PictureBox _iconPictureBox;
    private readonly Label _titleLabel;
    private readonly Panel _contentHost;
    private readonly Timer _animationTimer;

    private bool _expanded;
    private bool _animating;
    private int _targetContentHeight;
    private int _animationStep = 24;

    private readonly Image _iconCollapsed;
    private readonly Image _iconExpanded;

    public event EventHandler HeaderClicked;
    public event EventHandler ExpandedChanged;

    public FluentAccordionItem()
    {
      DoubleBuffered = true;
      BackColor = Color.FromArgb(243, 243, 243);
      Padding = new Padding(0);
      Margin = new Padding(0, 0, 0, 8);

      _iconCollapsed = CreateChevronBitmap(false, 16, Color.FromArgb(32, 32, 32));
      _iconExpanded = CreateChevronBitmap(true, 16, Color.FromArgb(32, 32, 32));

      _headerPanel = new Panel
      {
        Dock = DockStyle.Top,
        Height = 40,
        Cursor = Cursors.Hand,
        BackColor = Color.White,
        Padding = new Padding(12, 0, 12, 0)
      };
      _headerPanel.Paint += HeaderPanel_Paint;
      _headerPanel.MouseEnter += HeaderPanel_MouseEnter;
      _headerPanel.MouseLeave += HeaderPanel_MouseLeave;
      _headerPanel.Click += HeaderPanel_Click;

      _iconPictureBox = new PictureBox
      {
        Size = new Size(20, 20),
        Location = new Point(12, 10),
        SizeMode = PictureBoxSizeMode.CenterImage,
        Image = _iconCollapsed,
        BackColor = Color.Transparent,
        Cursor = Cursors.Hand
      };
      _iconPictureBox.Click += HeaderPanel_Click;

      _titleLabel = new Label
      {
        AutoSize = false,
        Location = new Point(40, 0),
        Size = new Size(400, 40),
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Yu Gothic UI", 10.0f, FontStyle.Regular),
        ForeColor = Color.FromArgb(24, 24, 24),
        Text = Resources.FluentAccordionItemDetail, // 「詳細」
        BackColor = Color.Transparent,
        Cursor = Cursors.Hand
      };
      _titleLabel.Click += HeaderPanel_Click;

      _contentHost = new Panel
      {
        Dock = DockStyle.Top,
        Height = 0,
        Visible = false,
        BackColor = Color.White,
        Padding = new Padding(40, 4, 12, 12)
      };
      _contentHost.Resize += ContentHost_Resize;
      _contentHost.ControlAdded += ContentHost_ControlChanged;
      _contentHost.ControlRemoved += ContentHost_ControlChanged;

      _animationTimer = new Timer
      {
        Interval = 15
      };
      _animationTimer.Tick += AnimationTimer_Tick;

      _headerPanel.Controls.Add(_iconPictureBox);
      _headerPanel.Controls.Add(_titleLabel);

      Controls.Add(_contentHost);
      Controls.Add(_headerPanel);

      Height = _headerPanel.Height;
      MinimumSize = new Size(200, _headerPanel.Height);
    }

    [Category("Appearance")]
    public string HeaderText
    {
      get => _titleLabel.Text;
      set => _titleLabel.Text = value;
    }

    [Category("Behavior")]
    [DefaultValue(false)]
    public bool Expanded
    {
      get => _expanded;
      set
      {
        if (_expanded == value) return;
        SetExpanded(value, true);
      }
    }

    [Category("Behavior")]
    [DefaultValue(24)]
    public int AnimationStep
    {
      get => _animationStep;
      set => _animationStep = Math.Max(4, value);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public Panel ContentPanel => _contentHost;

    public void SetExpanded(bool expanded, bool animated)
    {
      if (_animating)
        _animationTimer.Stop();

      _expanded = expanded;
      _targetContentHeight = CalculateDesiredContentHeight();

      _iconPictureBox.Image = _expanded ? _iconExpanded : _iconCollapsed;

      if (!animated)
      {
        _contentHost.Visible = _expanded;
        _contentHost.Height = _expanded ? _targetContentHeight : 0;
        UpdateOverallHeight();
        ExpandedChanged?.Invoke(this, EventArgs.Empty);
        return;
      }

      if (_expanded)
      {
        _contentHost.Visible = true;
      }

      _animating = true;
      _animationTimer.Start();
      ExpandedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AnimationTimer_Tick(object sender, EventArgs e)
    {
      int desired = _expanded ? _targetContentHeight : 0;

      if (_contentHost.Height < desired)
      {
        _contentHost.Height = Math.Min(_contentHost.Height + _animationStep, desired);
      }
      else if (_contentHost.Height > desired)
      {
        _contentHost.Height = Math.Max(_contentHost.Height - _animationStep, desired);
      }

      UpdateOverallHeight();

      if (_contentHost.Height == desired)
      {
        _animationTimer.Stop();
        _animating = false;

        if (!_expanded)
        {
          _contentHost.Visible = false;
        }
      }
    }

    private void UpdateOverallHeight()
    {
      Height = _headerPanel.Height + _contentHost.Height;
      Invalidate();
    }

    private int CalculateDesiredContentHeight()
    {
      var bottom = 0;

      foreach (Control control in _contentHost.Controls)
      {
        var candidate = control.Bottom + control.Margin.Bottom;
        if (candidate > bottom)
          bottom = candidate;
      }

      var desired = bottom + _contentHost.Padding.Bottom;
      return Math.Max(desired, 0);
    }

    private void ContentHost_ControlChanged(object sender, ControlEventArgs e)
    {
      RecalculateContentHeight();
    }

    private void ContentHost_Resize(object sender, EventArgs e)
    {
      if (_expanded && !_animating)
      {
        _targetContentHeight = CalculateDesiredContentHeight();
        _contentHost.Height = _targetContentHeight;
        UpdateOverallHeight();
      }
    }

    public void RecalculateContentHeight()
    {
      _targetContentHeight = CalculateDesiredContentHeight();

      if (_expanded && !_animating)
      {
        _contentHost.Height = _targetContentHeight;
        UpdateOverallHeight();
      }
    }

    private void HeaderPanel_Click(object sender, EventArgs e)
    {
      HeaderClicked?.Invoke(this, EventArgs.Empty);
    }

    private void HeaderPanel_MouseEnter(object sender, EventArgs e)
    {
      _headerPanel.BackColor = Color.FromArgb(245, 245, 245);
      _headerPanel.Invalidate();
    }

    private void HeaderPanel_MouseLeave(object sender, EventArgs e)
    {
      _headerPanel.BackColor = Color.White;
      _headerPanel.Invalidate();
    }

    private void HeaderPanel_Paint(object sender, PaintEventArgs e)
    {
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

      using (var pen = new Pen(Color.FromArgb(225, 225, 225), 1))
      {
        e.Graphics.DrawLine(pen, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
      }

      using (var pen = new Pen(Color.FromArgb(232, 232, 232), 1))
      {
        e.Graphics.DrawRectangle(pen, 0, 0, _headerPanel.Width - 1, _headerPanel.Height - 1);
      }
    }

    protected override void OnResize(EventArgs e)
    {
      base.OnResize(e);
      _titleLabel.Width = Math.Max(60, Width - 56);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        _animationTimer?.Dispose();
        _iconCollapsed?.Dispose();
        _iconExpanded?.Dispose();
      }
      base.Dispose(disposing);
    }

    private static Image CreateChevronBitmap(bool down, int size, Color color)
    {
      var bmp = new Bitmap(size, size);

      using var g = Graphics.FromImage(bmp);
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.Clear(Color.Transparent);

      using var pen = new Pen(color, 2.0f);
      pen.StartCap = LineCap.Round;
      pen.EndCap = LineCap.Round;
      pen.LineJoin = LineJoin.Round;

      if (down)
      {
        g.DrawLines(
          pen,
          [
            new PointF(size * 0.25f, size * 0.38f),
            new PointF(size * 0.50f, size * 0.62f),
            new PointF(size * 0.75f, size * 0.38f)
          ]);
      }
      else
      {
        g.DrawLines(
          pen,
          [
            new PointF(size * 0.38f, size * 0.25f),
            new PointF(size * 0.62f, size * 0.50f),
            new PointF(size * 0.38f, size * 0.75f)
          ]);
      }

      return bmp;
    }
  }
}