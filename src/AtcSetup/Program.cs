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
      // DLLプリロード攻撃対策
      // Prevent DLL preloading attacks
      //
      // SetDllDirectory(null) は「既定の検索順序に戻す」= カレントディレクトリを
      // 含める、という逆の意味になる。空文字を渡してカレントディレクトリを外す。
      //
      // 2 本の API は独立に呼ぶこと。片方の失敗でもう片方を飛ばすと対策が外れる。
      // GetLastWin32Error は各呼び出しの直後に取る（次の P/Invoke で上書きされる）。
      // SetDefaultDllDirectories は KB2533623 未適用の環境では解決できないので、
      // ExeOutShared/Program.cs と同じく try/catch で囲む。
      try
      {
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
      Application.Run(new Form1());
    }
  }
}
