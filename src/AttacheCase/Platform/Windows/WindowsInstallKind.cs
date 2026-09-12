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
//
// いま動いているのがインストーラ版か、ZIP 版か。
//
// 更新の扱いが違う。
//   インストーラ版 … 新しいインストーラを落として実行する（上書きインストール）
//   ZIP 版         … 何も落とさない。配布ページをブラウザで開くだけ
//
// ZIP 版を自動更新しないのは、展開先が利用者任せで特定できないから。
// 「置き場所も含めて自分で管理する」ことを承知で ZIP を選んでいる利用者の
// ファイルを、こちらの判断で書き換えるべきではない。
//----------------------------------------------------------------------
using System;
using System.IO;
using Microsoft.Win32;

namespace AttacheCase.Platform.Windows
{
  public enum InstallKind
  {
    /// <summary>インストーラで入れたもの。上書きインストールで更新できる。</summary>
    Installer,

    /// <summary>ZIP を展開したもの、または判別できないもの。自動更新しない。</summary>
    Portable,
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  public static class WindowsInstallKind
  {
    /// <summary>
    /// アンインストール情報のキー名。Inno Setup の AppId に <c>_is1</c> が付いたもの。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>閉じ括弧が 2 つあるのは誤記ではない。実際のレジストリがそうなっている。</b>
    /// </para>
    /// <para>
    /// <c>_installer\AttacheCase.iss</c> の定義はこうなっている。
    /// <code>#define MyAppId "{{5001F7AE-16EC-4FDA-80AA-5CB34D066034}}"</code>
    /// Inno Setup では先頭の <c>{</c> が定数の開始と解釈されるため <c>{{</c> と重ねて
    /// 逃がす決まりだが、末尾の <c>}</c> は逃がす必要が無い。にもかかわらず重ねてあるので、
    /// <b>AppId の実体は末尾に <c>}</c> が 1 つ余分に付いた文字列</b>になり、
    /// 出来上がるキーは次のようになる。
    /// <code>...\Uninstall\{5001F7AE-16EC-4FDA-80AA-5CB34D066034}}_is1</code>
    /// </para>
    /// <para>
    /// <b>.iss 側を「直して」はいけない。</b>AppId は版をまたいだ製品の同一性そのもので、
    /// 変えると既存の利用者に対して上書き更新ではなく別製品として入り、
    /// 古いアンインストール情報が残る。
    /// </para>
    /// <para>
    /// 2026-09-05、実機にインストールして判定が効かないことで発覚した。それまでは
    /// この PC にインストール版が無く、<see cref="Detect"/> は常に
    /// <see cref="InstallKind.Portable"/> を返していた（＝インストーラ版の利用者にも
    /// 「ダウンロードページを開く」しか出なかった）。
    /// </para>
    /// </remarks>
    private const string AppId = "{5001F7AE-16EC-4FDA-80AA-5CB34D066034}}_is1";

    /// <summary>
    /// 将来 <c>.iss</c> の重複した <c>}</c> が取り除かれた場合に備えた、括弧 1 つの名前。
    /// </summary>
    /// <remarks>
    /// いま入っている利用者は上の名前、入れ直した利用者はこちら、という状態が
    /// ありうるので、両方を見る。無いほうは単に見つからないだけで害は無い。
    /// </remarks>
    private const string AppIdSingleBrace = "{5001F7AE-16EC-4FDA-80AA-5CB34D066034}_is1";

    private const string UninstallPath =
      @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";

    /// <summary>
    /// いま動いている実行ファイルが、インストーラで入れられたものかを判定する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// インストーラのアンインストール情報にある <c>InstallLocation</c> と、
    /// 動作中の実行ファイルの場所が一致するかで見る。キーが在るだけでは足りない。
    /// インストール済みの環境で ZIP 版を別の場所に展開して動かす、という使い方が
    /// 現にありうるため。
    /// </para>
    /// <para>
    /// <b>判別できないときは <see cref="InstallKind.Portable"/> を返す。</b>
    /// 確信が持てないまま自動でインストーラを走らせるより、
    /// 配布ページへ誘導して利用者に選ばせるほうが安全側。
    /// </para>
    /// <para>
    /// 見る場所が 3 つあるのは、<c>AttacheCase.iss</c> に
    /// <c>ArchitecturesInstallIn64BitMode</c> の指定が無く、64bit Windows では
    /// インストーラが 32bit として動いて <c>WOW6432Node</c> 側へ書くため。
    /// 将来 64bit 指定を足したときのために、素の HKLM も見る。
    /// </para>
    /// </remarks>
    public static InstallKind Detect()
    {
      try
      {
        // Assembly.Location は単一ファイル発行では空文字列になる。
        // ProcessPath なら常に実行中の exe の場所を指す
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);

        if (string.IsNullOrEmpty(exeDir)) return InstallKind.Portable;

        foreach (var location in ReadInstallLocations())
        {
          if (IsSameDirectory(location, exeDir))
          {
            return InstallKind.Installer;
          }
        }

        return InstallKind.Portable;
      }
      catch (Exception)
      {
        // 判別に失敗したら自動更新しない側へ倒す
        return InstallKind.Portable;
      }
    }

    /// <summary>アンインストール情報に登録されている場所を、在るぶんだけ返す。</summary>
    private static string[] ReadInstallLocations()
    {
      var results = new System.Collections.Generic.List<string>();

      // 64bit Windows では WOW6432Node 側に入る（上の remarks を参照）
      foreach (var appId in new[] { AppId, AppIdSingleBrace })
      {
        AddIfFound(results, RegistryHive.LocalMachine, RegistryView.Registry32, appId);
        AddIfFound(results, RegistryHive.LocalMachine, RegistryView.Registry64, appId);
        AddIfFound(results, RegistryHive.CurrentUser, RegistryView.Default, appId);
      }

      return results.ToArray();
    }

    private static void AddIfFound(
      System.Collections.Generic.List<string> results, RegistryHive hive, RegistryView view,
      string appId)
    {
      try
      {
        using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
        using (var key = baseKey.OpenSubKey(UninstallPath + appId))
        {
          if (key == null) return;

          var location = key.GetValue("InstallLocation") as string;
          if (!string.IsNullOrEmpty(location))
          {
            results.Add(location);
          }
        }
      }
      catch (Exception)
      {
        // 読めない view は飛ばす
      }
    }

    /// <summary>2 つのパスが同じフォルダーを指すか。末尾の区切りと大文字小文字を吸収する。</summary>
    internal static bool IsSameDirectory(string a, string b)
    {
      if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;

      try
      {
        var na = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var nb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
      }
      catch (Exception)
      {
        return false;
      }
    }

    /// <summary>この形式に対応するマニフェストの <c>builds</c> のキー。</summary>
    public static string BuildKeyFor(InstallKind kind) =>
      kind == InstallKind.Installer
        ? AttacheCase.Services.UpdateService.BuildKeyInstaller
        : AttacheCase.Services.UpdateService.BuildKeyZip;
  }
}
