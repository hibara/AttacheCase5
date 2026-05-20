//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using AttacheCase.Properties;
using Microsoft.Win32;
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Windows.Forms;

namespace AttacheCase
{
  public partial class Form2 : Form
  {
    private const string AttacheCaseKey = $@"Software\Hibara\{"AttacheCase5"}";
    private const string RegistryLicenseKey = $@"Software\Hibara\{"AttacheCase5"}\Registration";
    
    //===================================
    const bool BETA_VERSION = true; // ベータ版であるかどうかのフラグ
    //===================================

    public Form2()
    {
      InitializeComponent();

      SetupInitialLayout();
    }

    private void SetupInitialLayout()
    {
      panelVersion.Parent = this.panelMain;
      panelRegistration.Parent = this.panelMain;
      panelRegistration.Visible = false;
      panelVersion.Visible = true;
      tabControl1.Visible = false;

      // アタッシェケースアイコン
      pictureBoxApplicationIcon.Left = 32;
      pictureBoxApplicationIcon.Top = 32;

      // アプリケーション名
      labelAppName.Left = pictureBoxApplicationIcon.Left + pictureBoxApplicationIcon.Width + 20;
      labelAppName.Top = pictureBoxApplicationIcon.Top;
      // バージョン
      labelVersion.Left = labelAppName.Left;
      labelVersion.Top = labelAppName.Top + labelAppName.Height + 4;
      // 著作権表示
      labelCopyright.Left = labelAppName.Left;
      labelCopyright.Top = labelVersion.Top + labelVersion.Height + 4;

      // ホームページアイコン
      pictureBoxHomeIcon.Left = labelAppName.Left;
      pictureBoxHomeIcon.Top = labelCopyright.Top + labelCopyright.Height + 12;
      // ホームページリンクラベル
      linkLabelHomePage.Left = pictureBoxHomeIcon.Left + pictureBoxHomeIcon.Width + 4;
      linkLabelHomePage.Top = pictureBoxHomeIcon.Top;
      // GitHubアイコン
      pictureBoxGitHubIcon.Left = labelAppName.Left;
      pictureBoxGitHubIcon.Top = pictureBoxHomeIcon.Top + pictureBoxHomeIcon.Height + 8;
      // GitHubリンクラベル
      linkLabelGitHub.Left = pictureBoxGitHubIcon.Left + pictureBoxGitHubIcon.Width + 4;
      linkLabelGitHub.Top = pictureBoxGitHubIcon.Top;

      // 商用利用ライセンス登録ボタン
      buttonRegisterLicense.Left = labelAppName.Left;
      buttonRegisterLicense.Top = pictureBoxGitHubIcon.Top + pictureBoxGitHubIcon.Height + 40;
      labelFreeLicence.Left = buttonRegisterLicense.Left;
      labelFreeLicence.Top = buttonRegisterLicense.Top - labelFreeLicence.Height - 4;
      labelFreeLicence.Width = buttonRegisterLicense.Width;

      // アカデミック利用宣言ボタン
      buttonDeclareStudentOrEducator.Left = buttonRegisterLicense.Left;
      buttonDeclareStudentOrEducator.Top = buttonRegisterLicense.Top + buttonRegisterLicense.Height + 4;

      // 商用ライセンスパネル
      panelCommercialLicense.Left = pictureBoxGitHubIcon.Left;
      panelCommercialLicense.Top = pictureBoxGitHubIcon.Top + pictureBoxGitHubIcon.Height + 16;
      // アカデミックライセンスパネル
      panelAcademicLicense.Left = pictureBoxGitHubIcon.Left;
      panelAcademicLicense.Top = pictureBoxGitHubIcon.Top + pictureBoxGitHubIcon.Height + 16;

      // アップデート確認リンク
      linkLabelCheckForUpdates.Left = pictureBoxApplicationIcon.Left * pictureBoxApplicationIcon.Width + 4;

      this.Height = 320;

    }
    
    private void Form2_Load(object sender, EventArgs e)
    {
      // Resources.LabelBetaTestVersion / " Beta Test Version" : "（βテスト版）" 
      labelAppName.Text = Resources.AppName + (BETA_VERSION ? Resources.LabelBetaTestVersion : "");
      labelVersion.Text = @"Version." + ApplicationInfo.Version;
      labelCopyright.Text = ApplicationInfo.CopyrightHolder;

      // レジストレーションコードのチェック
      // まずレジストリ(AttacheCase5\Registration)から読み込み、見つからなければ
      // _AtcCase.ini 由来の一時設定 (AppSettings.RegistrationCodeString) をフォールバックとして利用する。
      // これにより INI 一時設定モードでも、レジストリを汚染することなく商用ライセンスを画面に表示できる。
      var lcr = new LicenseRegister();
      var isLicensed = lcr.GetCommercialLicense();
      if (isLicensed == false)
      {
        var staged = AppSettings.Instance.RegistrationCodeString;
        if (string.IsNullOrEmpty(staged) == false)
        {
          lcr = new LicenseRegister(staged);
          isLicensed = lcr.Decrypt(false);
        }
      }
      if (isLicensed)
      {
        // 商用ライセンス適用
        // Commercial license applicable
        labelUserName.Text = lcr.UserNameString;
        labelEmailAddress.Text = lcr.EmailAddressString;
        labelUserNameTitle.Visible = true;
        labelEmailTitle.Visible = true;
        labelUserName.Visible = true;
        labelEmailAddress.Visible = true;

        labelFreeLicence.Visible = false;      // フリーライセンス文字を非表示
        buttonDeclareStudentOrEducator.Visible = false; // アカデミック利用ボタンを非表示
        buttonRegisterLicense.Visible = false; // 登録ボタンの消去

        // 商用利用ライセンスパネルの表示
        panelCommercialLicense.Visible = true;

      }
      else if (lcr.GetAcademicLicense())
      {
        // アカデミックライセンスパネル表示
        panelAcademicLicense.Visible = true;
        // バージョン情報ページを表示する
        panelRegistration.Visible = false;
        panelVersion.Visible = true;

        labelFreeLicence.Visible = false;      // フリーライセンス文字を非表示
        buttonDeclareStudentOrEducator.Visible = false; // アカデミック利用ボタンを非表示
        buttonRegisterLicense.Visible = false; // 登録ボタンの消去

      }

      panelVersion.Focus();

    }

    private void buttonOK_Click(object sender, EventArgs e)
    {
      this.Close();
    }

    private void linkLabelHomePage_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
      linkLabelHomePage.LinkVisited = true;
      System.Diagnostics.Process.Start(linkLabelHomePage.Text);
      this.Close();
    }

    private void linkLabelGitHub_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
      linkLabelGitHub.LinkVisited = true;
      System.Diagnostics.Process.Start(linkLabelGitHub.Text);
      this.Close();
    }

    /// <summary>
    /// 「アップデートを確認する」リンクがクリックされた際のイベントを処理します。
    /// アプリケーションの最新バージョンを確認するプロセスを開始します。
    /// </summary>
    /// <param name="sender">イベントのソース。通常はリンクラベルです。</param>
    /// <param name="e"><see cref="LinkLabelLinkClickedEventArgs"/> 型のイベントデータを含みます。</param>
    private void linkLabelCheckForUpdates_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
      if (pictureBoxProgressCircle.Image == pictureBoxExclamationMark.Image)
      {
        System.Diagnostics.Process.Start("https://hibara.jp/software/attachecase/");
        this.Close();
        return;
      }

      pictureBoxProgressCircle.Visible = true;
      linkLabelCheckForUpdates.Left = pictureBoxProgressCircle.Left + pictureBoxProgressCircle.Width;
      // "Checking for update..."
      linkLabelCheckForUpdates.Text = Resources.LinkLabelCheckingForUpdates;
      this.Refresh();

      try
      {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
        using var webClient = new WebClient();
        var stream = webClient.OpenRead(new Uri("https://hibara.jp/software/attachecase/current/"));
        using var sr = new StreamReader(stream);
        var content = sr.ReadToEnd();
        var current = int.Parse(content);
        if (current > AppSettings.Instance.AppVersion)
        {
          pictureBoxProgressCircle.Image = pictureBoxExclamationMark.Image;
          // "The latest version is released!"
          linkLabelCheckForUpdates.Text = Resources.LinkLabelLatestVersionReleased;
        }
        else
        {
          pictureBoxProgressCircle.Image = pictureBoxCheckMark.Image;
          // "Your version is latest."
          linkLabelCheckForUpdates.Text = Resources.LinkLabelLatestVersion;
          linkLabelCheckForUpdates.Enabled = false;
        }
      }
      catch (Exception ex)
      {
        // "Getting updates information is failed."
        linkLabelCheckForUpdates.Text = Resources.LinkLabelCheckForUpdatesFailed;
        linkLabelCheckForUpdates.Enabled = false;
        MessageBox.Show(ex.Message);
      }

    }

    private void buttonRegisterLicense_Click(object sender, EventArgs e)
    {
      // レジストレーションコード入力ページを表示
      panelVersion.Visible = false;
      panelRegistration.Visible = true;
      panelRegistration.Focus();
    }

    private void buttonRegister_Click(object sender, EventArgs e)
    {
      var lcr = new LicenseRegister(textBox1.Text.Trim());
      if (lcr.Decrypt(true) == true)
      {
        // 商用利用適用
        // Commercial Use applicable
        labelUserName.Text = lcr.UserNameString;
        labelEmailAddress.Text = lcr.EmailAddressString;
        labelUserNameTitle.Visible = true;
        labelEmailTitle.Visible = true;
        labelUserName.Visible = true;
        labelEmailAddress.Visible = true;

        labelFreeLicence.Visible = false;      // フリー利用文字を非表示
        buttonRegisterLicense.Visible = false; // 登録ボタンの消去

        // 商用利用登録パネルの表示
        panelCommercialLicense.Visible = true;
        // バージョン情報ページを表示する
        panelRegistration.Visible = false;
        panelVersion.Visible = true;
        // 非表示項目
        labelFreeLicence.Visible = false;
        buttonRegisterLicense.Visible = false;
        buttonDeclareStudentOrEducator.Visible = false;
        panelVersion.Focus();
      }
      else
      {
        labelValidation.ForeColor = Color.FromName("Red");
        // コードが正しくありません。
        // The code is incorrect.
        labelValidation.Text = Resources.LabelCodeIncorrect;
      }
    }

    private void buttonCancel_Click(object sender, EventArgs e)
    {
      // レジストレーションコード入力欄を空にする
      // Empty the registration code entry field
      textBox1.Text = "";

      // バージョン情報ページを表示
      panelRegistration.Visible = false;
      panelVersion.Visible = true;
      panelVersion.Focus();
    }

    private void buttonDeclareStudentOrEducator_Click(object sender, EventArgs e)
    {
      var lcr = new LicenseRegister();
      // アカデミックライセンスを有効にします。
      // あなたは学生または教育関係者ですか？
      // ---
      // Activate your academic license.
      // Are you a student or an educator?
      var ret = MessageBox.Show(
        Resources.DialogMessageAcademicUseLicense, Resources.DialogTitleQuestion,
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Question,
        MessageBoxDefaultButton.Button2);

      if (ret == DialogResult.Yes)
      {
        // アカデミックライセンスを有効にする
        // Activate the academic license
        // Open the key (\HKEY_CURRENT_USER\SOFTWARE\Hibara\AttacheCase5\Registration）
        lcr.SetAcademicLicense();
        
        // アカデミックライセンス登録パネルの表示
        panelAcademicLicense.Visible = true;

        // 非表示項目
        labelFreeLicence.Visible = false;
        buttonRegisterLicense.Visible = false;
        buttonDeclareStudentOrEducator.Visible = false;
      }
    }


    private void textBox1_TextChanged(object sender, EventArgs e)
    {
      var lcr = new LicenseRegister(textBox1.Text.Trim());
      if (lcr.Decrypt(false) == true)  // レジストリへは書き込まずに判定だけ行う
      {
        labelValidation.ForeColor = Color.FromName("ForestGreen");
        // Valid code.
        // 有効なコードです。
        labelValidation.Text = Resources.LabelValidCode;
        labelValidation.Visible = true;
      }
      else
      {
        labelValidation.ForeColor = Color.FromName("Red");
        // コードが正しくありません。
        // The code is incorrect.
        labelValidation.Text = Resources.LabelCodeIncorrect;
        labelValidation.Visible = true;
      }
    }

    private void linkLabelPurchase_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
      System.Diagnostics.Process.Start("https://hibara.jp/software/attachecase/buy/");
    }

    // 商用ライセンスの削除メニュー
    private void ToolStripMenuItemDeleteLicense_Click(object sender, EventArgs e)
    {
      // Remove this commercial license?
      // この商用ライセンスを削除しますか？
      var result = MessageBox.Show(
        Resources.DialogMessageDeleteCommercialLicense, Resources.DialogTitleQuestion,
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Exclamation,
        MessageBoxDefaultButton.Button2);

      if (result == DialogResult.No)
      {
        //「いいえ」は抜ける
        return;
      }

      var lcr = new LicenseRegister("");
      if (lcr.DeleteLicense() == true)
      {
        // 商用利用表示を削除する
        // Remove the commercial use mark
        labelUserName.Text = "";
        labelEmailAddress.Text = "";
        labelUserNameTitle.Visible = false;
        labelEmailTitle.Visible = false;
        labelUserName.Visible = false;
        labelEmailAddress.Visible = false;

        // レジストレーションコード入力欄を空にする
        // Empty the registration code entry field
        textBox1.Text = "";

        labelFreeLicence.Visible = true;      // フリー利用文字を再表示
        buttonRegisterLicense.Visible = true; // 登録ボタンの表示

        // 商用利用登録パネルの非表示
        panelCommercialLicense.Visible = false;
        // バージョン情報ページを表示する
        panelRegistration.Visible = true;
        panelVersion.Visible = true;
        panelVersion.Focus();

      }

    }

    private void pictureBoxLicenseIcon_Click(object sender, EventArgs e)
    {
    }
    
    private void pictureBoxLicenseIcon_MouseDown(object sender, MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Right)
      {
        contextMenuStrip1.Show(Cursor.Position);
      }
    }

    private void pictureBox2_MouseDown(object sender, MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Right)
      {
        contextMenuStrip1.Show(Cursor.Position);
      }
    }
  }

  /// <summary>
  /// アセンブリ情報を取得する
  /// Get assembly information
  /// http://stackoverflow.com/questions/909555/how-can-i-get-the-assembly-file-version
  /// </summary>
  public static class ApplicationInfo
  {
    public static Version Version => Assembly.GetCallingAssembly().GetName().Version;

    public static string Title
    {
      get
      {
        var attributes = Assembly.GetCallingAssembly().GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
        if (attributes.Length > 0)
        {
          var titleAttribute = (AssemblyTitleAttribute)attributes[0];
          if (titleAttribute.Title.Length > 0) return titleAttribute.Title;
        }
        return System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().CodeBase);
      }
    }

    public static string ProductName
    {
      get
      {
        var attributes = Assembly.GetCallingAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
        return attributes.Length == 0 ? "" : ((AssemblyProductAttribute)attributes[0]).Product;
      }
    }

    public static string Description
    {
      get
      {
        var attributes = Assembly.GetCallingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false);
        return attributes.Length == 0 ? "" : ((AssemblyDescriptionAttribute)attributes[0]).Description;
      }
    }

    public static string CopyrightHolder
    {
      get
      {
        var attributes = Assembly.GetCallingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
        return attributes.Length == 0 ? "" : ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
      }
    }

    public static string CompanyName
    {
      get
      {
        var attributes = Assembly.GetCallingAssembly().GetCustomAttributes(typeof(AssemblyCompanyAttribute), false);
        return attributes.Length == 0 ? "" : ((AssemblyCompanyAttribute)attributes[0]).Company;
      }
    }

  }

}
