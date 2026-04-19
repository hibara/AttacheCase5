//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
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

    /// <summary>
    /// アプリケーションのメイン エントリ ポイントです。
    /// </summary>
    [STAThread]
    static void Main()
    {
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
        SetDllDirectory("");
        SetDefaultDllDirectories(DllSearchFlags);
      }
      catch
      {
        // Pre-Windows 7, KB2533623 
        SetDllDirectory("");
      }

      CultureInfo ci = Thread.CurrentThread.CurrentUICulture;
      //Console.WriteLine(ci.Name);  // ja-JP
      if (ci.Name == "ja-JP")
      {
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("ja-JP", true);
      }

      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.Run(new Form1());

    }
  }
}
