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

namespace AttacheCase
{
  /// <summary>
  /// 表示言語の判定。
  /// </summary>
  /// <remarks>
  /// <para>
  /// 言語設定が「既定値」のとき、どの言語で表示するかを決めるために使う。
  /// </para>
  /// <para>
  /// <b>❌ <see cref="CultureInfo.CurrentCulture"/> で判定してはいけない。</b>
  /// これは「地域形式」（数値・日付・通貨の書式）であって、表示言語ではない。
  /// Windows では表示言語と地域形式を独立して設定できるため、
  /// <b>表示言語は日本語なのに地域形式が英語(米国)</b> という環境が普通に存在する。
  /// そこで <c>CurrentCulture</c> を見ると「英語」と誤判定し、
  /// 既定値を選んでいるのに英語 UI になってしまう（2026-08-19 に実機で確認）。
  /// </para>
  /// <para>
  /// <b>❌ <see cref="CultureInfo.CurrentUICulture"/> をそのまま見るのも不可。</b>
  /// アプリ自身が起動時にスレッドのカルチャを書き換えるため、
  /// 起動後の値は OS の設定ではなくアプリの選択結果を表してしまう。
  /// </para>
  /// <para>
  /// そこで Win32 の <c>GetUserDefaultUILanguage()</c> を使う。
  /// これは「ユーザーが選んだ Windows の表示言語」を返し、
  /// スレッドのカルチャをどう書き換えても影響を受けない。
  /// </para>
  /// <para>
  /// 実測例（Windows 11 / 表示言語=日本語, 地域形式=英語(米国)）:
  /// <code>
  /// CurrentCulture             : en-US   ← 地域形式。これを見ると誤判定する
  /// CurrentUICulture           : ja-JP
  /// InstalledUICulture         : ja-JP
  /// GetUserDefaultUILanguage() : ja-JP   ← これが正しい
  /// GetUserDefaultLangID()     : en-US
  /// </code>
  /// </para>
  /// </remarks>
  internal static class AppLanguage
  {
    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    /// <summary>
    /// Windows の表示言語が日本語かどうか。
    /// </summary>
    /// <remarks>
    /// 地域変種（ja-JP など）をまとめて拾うため、
    /// 完全一致ではなく 2 文字の言語コードで判定する。
    /// </remarks>
    public static bool IsOsJapanese()
    {
      try
      {
        var culture = new CultureInfo(GetUserDefaultUILanguage());
        return string.Equals(culture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase);
      }
      catch (Exception)
      {
        // LCID から CultureInfo を作れない環境向けのフォールバック。
        // InstalledUICulture は「OS にインストールされた言語」であって
        // ユーザーの選択とは限らないが、判定材料が無いよりはよい。
        return string.Equals(
          CultureInfo.InstalledUICulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase);
      }
    }
  }
}
