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
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Exeout
{

  static class Program
  {
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool SetDllDirectory(string lpPathName);
    [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool SetDefaultDllDirectories(uint directoryFlags);
    // LOAD_LIBRARY_SEARCH_APPLICATION_DIR : 0x00000200
    // LOAD_LIBRARY_SEARCH_DEFAULT_DIRS    : 0x00001000
    // LOAD_LIBRARY_SEARCH_SYSTEM32        : 0x00000800
    // LOAD_LIBRARY_SEARCH_USER_DIRS       : 0x00000400
    private const uint DllSearchFlags = 0x00000800;

    // 表示言語の判定は AttacheCase/AppLanguage.cs に集約している。
    // 同じ実装を各 Program.cs に持たせると、将来どれかだけ直して
    // 判定結果が食い違う。アセンブリ参照はせず、csproj でソースを
    // リンク取り込みしているだけなので、実行ファイルは自己完結のまま。

    /// <summary>
    /// アプリケーションのメイン エントリ ポイントです。
    /// </summary>
    [STAThread]
    static void Main()
    {
      // 受信者の PC で黙って落ちると原因を追えない。未捕捉例外は %TEMP% に痕跡を残す
      AppDomain.CurrentDomain.UnhandledException += (s, ue) =>
      {
        try
        {
          System.IO.File.WriteAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AttacheCase_sfx_error.txt"),
            ue.ExceptionObject == null ? "(null)" : ue.ExceptionObject.ToString());
        }
        catch { /* ログも書けないなら諦める */ }
      };

      var os = Environment.OSVersion;
      if (os.Version.Major <= 6 && os.Version.Minor <= 1)
      {
        // Countermeasure that "Font '?' cannot be found" error for Win7 only
        // ref. https://chowdera.com/2022/03/202203241328277504.html
        var font = System.Drawing.SystemFonts.DefaultFont; // Load first 
      }

      // DLLプリロード攻撃対策
      // Prevent DLL preloading attacks
      try
      {
        // 短絡評価にしないこと。|| だと SetDllDirectory が失敗した時点で
        // SetDefaultDllDirectories が呼ばれず、対策が丸ごと外れる。
        // GetLastWin32Error は各呼び出しの直後に取る（次の P/Invoke で上書きされる）。
        var isDllDirectorySet = SetDllDirectory("");
        var dllDirectoryError = Marshal.GetLastWin32Error();
        var isDefaultDirectoriesSet = SetDefaultDllDirectories(DllSearchFlags);
        var defaultDirectoriesError = Marshal.GetLastWin32Error();

        if (isDllDirectorySet == false)
        {
          System.Diagnostics.Debug.WriteLine(
            "SetDllDirectory(\"\") failed: " + dllDirectoryError);
        }

        if (isDefaultDirectoriesSet == false)
        {
          System.Diagnostics.Debug.WriteLine(
            "SetDefaultDllDirectories failed: " + defaultDirectoriesError);
        }
      }
      catch
      {
        // Pre-Windows 7, KB2533623 
        SetDllDirectory("");
      }

      if (AttacheCase.AppLanguage.IsOsJapanese())
      {
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("ja-JP", true);
      }

      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      try
      {
        Application.Run(new Form1());
      }
      catch (Exception ex)
      {
        // 受信者の PC で黙って落ちると原因を追えない。%TEMP% に痕跡を残して知らせる
        try
        {
          var log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AttacheCase_sfx_error.txt");
          System.IO.File.WriteAllText(log, ex.ToString());
        }
        catch { /* ログも書けないなら諦める */ }
        MessageBox.Show(ex.Message, "AttacheCase SFX",
          MessageBoxButtons.OK, MessageBoxIcon.Error);
      }

    }
  }
}
