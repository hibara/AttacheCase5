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

namespace AtcSetup
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
      // DLLプリロード攻撃対策
      // Prevent DLL preloading attacks
      SetDllDirectory(null);
      SetDefaultDllDirectories(DllSearchFlags);

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
