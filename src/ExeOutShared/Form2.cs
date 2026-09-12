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
using System.Windows.Forms;
using System.Reflection;
using System.Globalization;


namespace Exeout
{
  public partial class Form2 : Form
  {
    public Form2()
    {
      InitializeComponent();
    }

    private void Form2_Load(object sender, EventArgs e)
    {

      labelVersion.Text = "Version." + ApplicationInfo.Version;
      labelCopyright.Text = ApplicationInfo.CopyrightHolder;
      //labelBeta.Left = labelVersion.Left + labelVersion.Width;
      //labelBeta.Top = labelVersion.Top;

    }

    private void buttonOK_Click(object sender, EventArgs e)
    {
      this.Close();
    }

    private void linkLabelURL_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
      linkLabelURL.LinkVisited = true;
      try
      {
        System.Diagnostics.Process.Start("https://hibara.org");
      }
      catch (Exception)
      {
        // 既定のブラウザーが無い・関連付けが壊れている環境では起動できない。
        // UI イベント内の未処理例外でアプリごと落とさない
      }
      this.Close();
    }

  }

  /// <summary>
  /// アセンブリ情報を取得する
  /// Get assembly infomations
  /// http://stackoverflow.com/questions/909555/how-can-i-get-the-assembly-file-version
  /// </summary>
  static public class ApplicationInfo
  {
    public static Version Version { get { return Assembly.GetCallingAssembly().GetName().Version; } }

    public static string Title
    {
      get
      {
        object[] attributes = Assembly.GetCallingAssembly().GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
        if (attributes.Length > 0)
        {
          AssemblyTitleAttribute titleAttribute = (AssemblyTitleAttribute)attributes[0];
          if (titleAttribute.Title.Length > 0) return titleAttribute.Title;
        }
        return System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().CodeBase);
      }
    }

    public static string ProductName
    {
      get
      {
        object[] attributes = Assembly.GetCallingAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
        return attributes.Length == 0 ? "" : ((AssemblyProductAttribute)attributes[0]).Product;
      }
    }

    public static string Description
    {
      get
      {
        object[] attributes = Assembly.GetCallingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false);
        return attributes.Length == 0 ? "" : ((AssemblyDescriptionAttribute)attributes[0]).Description;
      }
    }

    public static string CopyrightHolder
    {
      get
      {
        object[] attributes = Assembly.GetCallingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
        return attributes.Length == 0 ? "" : ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
      }
    }

    public static string CompanyName
    {
      get
      {
        object[] attributes = Assembly.GetCallingAssembly().GetCustomAttributes(typeof(AssemblyCompanyAttribute), false);
        return attributes.Length == 0 ? "" : ((AssemblyCompanyAttribute)attributes[0]).Company;
      }
    }

  }

}
