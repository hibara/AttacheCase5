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
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using AtcSetup.Properties;

namespace AtcSetup
{
  public partial class Form1 : Form
  {

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    // Shield icon
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(HandleRef hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    private const int BCM_FIRST = 0x1600;
    private const int BCM_SETSHIELD = BCM_FIRST + 0x000C;

    // Refresh desktop window.
    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public Dictionary<string, string> CommandData = new Dictionary<string, string>();

    public Form1()
    {
      InitializeComponent();
    }

    private void Form1_Load(object sender, EventArgs e)
    {
      //-----------------------------------
      // Get the version of this abdication from assembly infos.
      var asm = Assembly.GetExecutingAssembly();
      var ver = asm.GetName().Version;
      toolStripStatusLabel1.Text = @"ver. " + ver;

      //-----------------------------------
      // on the center of the screen.
      this.Left = Screen.GetBounds(this).Width / 2 - this.Width / 2;
      this.Top = Screen.GetBounds(this).Height / 2 - this.Height / 2;

      //-----------------------------------
      // Shield icon
      // 盾アイコン
      //-----------------------------------

      //Windows Vista later?
      if (Environment.OSVersion.Platform != PlatformID.Win32NT || Environment.OSVersion.Version.Major < 6)
      {
      }
      else
      {
        //FlatStyle=System
        buttonAssociation.FlatStyle = FlatStyle.System;
        buttonUnAssociation.FlatStyle = FlatStyle.System;
        //Shield icon
        SendMessage(new HandleRef(buttonAssociation, buttonAssociation.Handle), BCM_SETSHIELD, IntPtr.Zero, new IntPtr(1));
        SendMessage(new HandleRef(buttonUnAssociation, buttonUnAssociation.Handle), BCM_SETSHIELD, IntPtr.Zero, new IntPtr(1));

      }

      //-----------------------------------
      //コマンドライン引数を連想配列へ
      var cmds = Environment.GetCommandLineArgs();
      foreach (var cmd in cmds)
      {
        // 最初の '=' でだけ分割する（値に '=' が含まれても壊さない）。
        // 同じキーが 2 回現れても Add で例外にならないよう、添字で上書きする。
        var separatorIndex = cmd.IndexOf('=');
        if (separatorIndex > 0)
        {
          CommandData[cmd.Substring(0, separatorIndex)] = cmd.Substring(separatorIndex + 1);
        }
      }

      // Association, or UnAssociation ?
      if (CommandData.ContainsKey("-t") == true)
      {
        if (CommandData["-t"] == "0")
        {
          if (AssociateAtcFileToAttacheCase() == true)
          {
            labelInfo.Text = Resources.AssociationComplete;
            Application.Exit();
          }
          else
          {
            labelInfo.Text = Resources.AssociationFailed;
          }
        }
        else if (CommandData["-t"] == "1")
        {
          if (UnAssociateAtcFileToAttacheCase() == true)
          {
            labelInfo.Text = Resources.UnAssociationComplete;
            Application.Exit();
          }
          else
          {
            labelInfo.Text = Resources.UnAssociationFailed;
          }
        }
        else
        { // Other
          Application.Exit();
        }

      }

    }

    private void Form1_Shown(object sender, EventArgs e)
    {
      // 本体から -t 付きで呼ばれた場合は単体起動ではないので、確認は出さない。
      // 出してしまうと、-t の処理が失敗して labelInfo に理由を表示していても、
      // 「いいえ」でフォームごと閉じられて読めなくなる
      if (CommandData.ContainsKey("-t"))
      {
        return;
      }

      // このツールは通常単体では起動せず、アタッシェケース本体から呼び出されます。
      // 単体で操作するには関連付けに関する知識が必要です。それでも単体起動しますか？
      //
      // This tool usually does not run by itself, but is called from the AttachéCase itself.
      // You require some knowledge of Associations files to operate on this own. 
      // Can you still run it by itself?
      //
      var result = MessageBox.Show(Resources.MsgTextWhenApplicationLaunched,
        Resources.DialogAlert, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation,
        MessageBoxDefaultButton.Button2);
      if (result == DialogResult.No)
      {
        Application.Exit();
      }
    }

    //======================================================================
    /// <summary>
    /// Settings of Associating file to AttacheCase ( Executable file )
    /// </summary>
    /// <returns>[Boolean] true: Succeeded, false: failed</returns>
    //======================================================================
    private bool AssociateAtcFileToAttacheCase()
    {
      this.Text = Resources.LabelAssociation;
      progressBar1.Style = ProgressBarStyle.Marquee;
      // "It is setting to associate the '.atc' file to the AttacheCase application..."
      labelInfo.Text = Resources.Associating;

      //-----------------------------------
      var AttacheCaseFilePath = "";
      if (CommandData.TryGetValue("-p", out var value))
      {
        AttacheCaseFilePath = value;
      }
      else
      {
        AttacheCaseFilePath = getAttacheCaseExeFilePath();
      }

      // 本体が実在するかまで確認する（パスだけ取れても、消えていれば関連付けは無意味）
      if (string.IsNullOrEmpty(AttacheCaseFilePath) || File.Exists(AttacheCaseFilePath) == false)
      {
        // 注意
        // Alert
        //
        // "AttacheCase.exe" is not found!
        // アタッシェケース本体が見つかりません！
        var ret = MessageBox.Show(Resources.DialogMessageAttacheCaseNotFound + Environment.NewLine + AttacheCaseFilePath,
        Resources.DialogMessageAttacheCaseNotFound, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

        progressBar1.Style = ProgressBarStyle.Continuous;
        labelInfo.Text = "-";
        progressBar1.Value = 100;
        return (false);

      }

      //-----------------------------------
      var IconIndex = 1;
      var MyIconFilePath = "";

      if (CommandData.ContainsKey("-icn"))
      {
        if (int.TryParse(CommandData["-icn"], out IconIndex) == false)
        {
          MyIconFilePath = CommandData["-icn"];
          if (File.Exists(MyIconFilePath) == false)
          {
            MyIconFilePath = "";
            IconIndex = 1;
          }
        }
      }
      else
      {
        IconIndex = 1;
      }

      // 鍵ファイル（.atcpvt / .atcpub）のアイコンも .ico のパスで受け取れるようにする。
      // 統合版（5.5）の本体 exe はアイコンを 1 つしか持たないため、旧来の
      // 「本体 exe のアイコン番号 5 / 6」では白紙アイコンになってしまう。
      // 引数が無い・ファイルが無い場合は旧来どおり本体 exe の番号にフォールバックする
      // （旧版 4.x/5.0 から単体で呼ばれても挙動が変わらないように）
      var PvtIconFilePath = "";
      var PubIconFilePath = "";
      if (CommandData.TryGetValue("-pvticn", out var pvtIcon) && File.Exists(pvtIcon))
      {
        PvtIconFilePath = pvtIcon;
      }
      if (CommandData.TryGetValue("-pubicn", out var pubIcon) && File.Exists(pubIcon))
      {
        PubIconFilePath = pubIcon;
      }

      //-----------------------------------
      // 昇格していても ACL・グループポリシー・レジストリの状態次第で
      // CreateSubKey / SetValue は例外を投げる。未処理例外で落とさず、
      // 失敗として呼び出し元へ返す（表示は呼び出し側が行う）
      try
      {
      //-----------------------------------
      // HKEY_CLASSES_ROOT\.atc
      using (var regkey = Registry.ClassesRoot.CreateSubKey(@".atc"))
      {
        regkey?.SetValue("", "AttacheCase.DataFile");
      }
      //-----------------------------------
      // HKEY_CLASSES_ROOT\AttacheCase.DataFile
      using (var regkey = Registry.ClassesRoot.CreateSubKey(@"AttacheCase.DataFile\DefaultIcon"))
      {
        if (MyIconFilePath == "")
        {
          regkey?.SetValue("", "\"" + AttacheCaseFilePath + "\"," + IconIndex);
        }
        else
        {
          regkey?.SetValue("", "\"" + MyIconFilePath + "\"");  // My Icon
        }
      }
      using (var regkey = Registry.ClassesRoot.CreateSubKey(@"AttacheCase.DataFile\Shell\decode"))
      {
        // エクスプローラーの右クリックメニューに表示される。表示言語に追従させる
        regkey?.SetValue("", Resources.verbDescription);
      }
      using (var regkey = Registry.ClassesRoot.CreateSubKey(@"AttacheCase.DataFile\Shell\decode\command"))
      {
        // 実行ファイルと "%1" はカンマではなく空白で区切る（シェルのコマンド行として解釈されるため）
        regkey?.SetValue("", "\"" + AttacheCaseFilePath + "\" \"%1\"");
      }
      using (var regkey = Registry.ClassesRoot.CreateSubKey(@"AttacheCase.DataFile\Shell\open\command"))
      {
        regkey?.SetValue("", "\"" + AttacheCaseFilePath + "\" \"%1\"");
      }

      //-----------------------------------
      // HKEY_CLASSES_ROOT\.atcpvt
      using (var regkey = Registry.ClassesRoot.CreateSubKey(@".atcpvt"))
      {
        regkey?.SetValue("", "AttacheCase.PrivateKeyFile");
      }
      //-----------------------------------
      // HKEY_CLASSES_ROOT\AttacheCase.KeyFile
      using (var regkey = Registry.ClassesRoot.CreateSubKey(@"AttacheCase.PrivateKeyFile\DefaultIcon"))
      {
        if (PvtIconFilePath == "")
        {
          regkey?.SetValue("", "\"" + AttacheCaseFilePath + "\"," + 5);
        }
        else
        {
          regkey?.SetValue("", "\"" + PvtIconFilePath + "\"");
        }
      }

      //-----------------------------------
      // HKEY_CLASSES_ROOT\.atcpub
      using (var regkey = Registry.ClassesRoot.CreateSubKey(@".atcpub"))
      {
        regkey?.SetValue("", "AttacheCase.PublicKeyFile");
      }
      //-----------------------------------
      // HKEY_CLASSES_ROOT\AttacheCase.LockFile
      using (var regkey = Registry.ClassesRoot.CreateSubKey(@"AttacheCase.PublicKeyFile\DefaultIcon"))
      {
        if (PubIconFilePath == "")
        {
          regkey?.SetValue("", "\"" + AttacheCaseFilePath + "\"," + 6);
        }
        else
        {
          regkey?.SetValue("", "\"" + PubIconFilePath + "\"");
        }
      }
      }
      catch (Exception)
      {
        progressBar1.Style = ProgressBarStyle.Continuous;
        progressBar1.Value = 100;
        return (false);
      }

      progressBar1.Style = ProgressBarStyle.Continuous;
      progressBar1.Value = 100;
      labelInfo.Text = Resources.AssociationComplete;

      // Refresh desktop window icons
      SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

      return (true);

    }

    //======================================================================
    /// <summary>
    /// UnAssociating file from AttacheCase
    /// </summary>
    /// <returns></returns>
    //======================================================================
    private bool UnAssociateAtcFileToAttacheCase()
    {
      progressBar1.Style = ProgressBarStyle.Marquee;
      // "It is setting to Unassociated the '.atc' file in the AttacheCase application..."
      labelInfo.Text = Resources.UnAssociating;

      // 削除は「そのキーが在るか」だけで決める。
      // 以前は .atcpvt / .atcpub の削除まで .atc と AttacheCase.DataFile の
      // 存在で条件づけていたため、.atc だけ在る環境では
      // DeleteSubKeyTree(".atcpvt") が ArgumentException を投げていた。
      // throwOnMissingSubKey: false を渡し、無い場合は黙って通す。
      string[] subKeyNames =
      {
        ".atc",
        "AttacheCase.DataFile",
        // RSA key
        ".atcpvt",
        "AttacheCase.PrivateKeyFile",
        ".atcpub",
        "AttacheCase.PublicKeyFile",
      };

      // 昇格していても ACL・ポリシー・レジストリの状態次第で
      // DeleteSubKeyTree は例外を投げる。未処理例外で落とさず失敗として返す
      try
      {
        foreach (var subKeyName in subKeyNames)
        {
          Registry.ClassesRoot.DeleteSubKeyTree(subKeyName, false);
        }

        // In addition, delete all AttacheCase settings from the Windows registry.
        if (checkBoxDeleteAllSettingsFromRegistry.Checked == true)
        {
          if (Registry.CurrentUser.OpenSubKey(@"Software\Hibara\AttacheCase4", false) != null)
          {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Hibara\AttacheCase4");
          }
        }
      }
      catch (Exception)
      {
        progressBar1.Style = ProgressBarStyle.Continuous;
        progressBar1.Value = 100;
        return (false);
      }

      progressBar1.Style = ProgressBarStyle.Continuous;
      progressBar1.Value = 100;
      labelInfo.Text = Resources.UnAssociationComplete;

      // Refresh desktop window icons.
      SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

      return (true);

    }

    /// <summary>
    /// アタッシェケース4の方のレジストリデータから本体のフルパスを取得する
    /// Get full path of executable file from the registry of different application AttacheCase#3.
    /// </summary>
    /// <returns></returns>
    private string getAttacheCaseExeFilePath()
    {
      var AppFilePath = string.Empty;
      using (var regkey = Registry.CurrentUser.OpenSubKey(@"Software\Hibara\AttacheCase4\AppInfo"))
      {
        if (regkey?.GetValue("AppPath") != null)
        {
          AppFilePath = (string)regkey.GetValue("AppPath");
        }
      }
      return (AppFilePath);
    }

    private void buttonAssociation_Click(object sender, EventArgs e)
    {
      if (AssociateAtcFileToAttacheCase() == false)
      {
        labelInfo.Text = Resources.AssociationFailed;
      }
    }

    private void buttonUnAssociation_Click(object sender, EventArgs e)
    {
      if (UnAssociateAtcFileToAttacheCase() == false)
      {
        labelInfo.Text = Resources.UnAssociationFailed;
      }
    }

    private void buttonExit_Click(object sender, EventArgs e)
    {
      Application.Exit();
    }

  }

}
