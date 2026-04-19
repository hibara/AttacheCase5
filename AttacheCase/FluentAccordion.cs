//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace AttacheCase
{
  public sealed class FluentAccordion : FlowLayoutPanel
  {
    private bool _singleExpand = true;

    public FluentAccordion()
    {
      Dock = DockStyle.Fill;
      FlowDirection = FlowDirection.TopDown;
      WrapContents = false;
      AutoScroll = true;
      Padding = new Padding(12);
      BackColor = System.Drawing.Color.FromArgb(249, 249, 249);
    }

    [Category("Behavior")]
    [DefaultValue(true)]
    public bool SingleExpand
    {
      get => _singleExpand;
      set => _singleExpand = value;
    }

    public void AddItem(FluentAccordionItem item)
    {
      if (item == null) return;

      item.Width = ClientSize.Width - Padding.Horizontal - (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
      item.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
      item.HeaderClicked += Item_HeaderClicked;

      Controls.Add(item);
    }

    private void Item_HeaderClicked(object sender, EventArgs e)
    {
      if (sender is not FluentAccordionItem clicked) return;

      var nextState = !clicked.Expanded;

      if (_singleExpand && nextState)
      {
        foreach (var item in Controls.OfType<FluentAccordionItem>())
        {
          if (!ReferenceEquals(item, clicked) && item.Expanded)
          {
            item.SetExpanded(false, true);
          }
        }
      }

      clicked.SetExpanded(nextState, true);
    }

    protected override void OnResize(EventArgs eventargs)
    {
      base.OnResize(eventargs);

      var width = ClientSize.Width - Padding.Horizontal;
      if (VerticalScroll.Visible)
      {
        width -= SystemInformation.VerticalScrollBarWidth;
      }

      foreach (Control control in Controls)
      {
        control.Width = Math.Max(100, width - control.Margin.Horizontal);
      }
    }
  }
}