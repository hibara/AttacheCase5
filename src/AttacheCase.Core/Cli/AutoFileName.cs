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
// 暗号化ファイル名の書式展開（/autoname）。
// Windows 版 AttacheCase\Headless\AutoFileName.cs の移植。
// 相違点は不正文字の判定のみ:
//   - macOS で実際に使えない文字（'/' と NUL）は「エラー」
//   - Windows でのみ使えない文字（\ : * ? " < > |）は「警告」
//     （暗号化ファイルは Windows へ持ち出される前提があるため知らせる）
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using AttacheCase.Models;

namespace AttacheCase.Cli;

/// <summary>
/// 自動ファイル名生成で使用する乱数の文字種。
/// </summary>
[Flags]
public enum AutoNameCharset
{
    None = 0,
    UpperCase = 1,
    LowerCase = 2,
    Numbers = 4,
    Symbols = 8,

    /// <summary>Windows 版 GUI の既定（英大文字・英小文字・数字。記号は含まない）</summary>
    Default = UpperCase | LowerCase | Numbers,
}

/// <summary>
/// 書式文字列から暗号化ファイル名を生成する。
/// </summary>
/// <remarks>
/// 書式の指定子:
/// <list type="bullet">
///   <item><c>&lt;filename&gt;</c> — 元ファイル名（拡張子なし）</item>
///   <item><c>&lt;ext&gt;</c> — 拡張子</item>
///   <item><c>&lt;date:書式&gt;</c> — 日時（<c>DateTime.ToString</c> の書式）</item>
///   <item><c>&lt;num:桁数&gt;</c> — 連番。同名ファイルが存在する限り繰り上がる</item>
///   <item><c>&lt;random:文字数&gt;</c> — ランダム文字列</item>
/// </list>
/// </remarks>
public static class AutoFileName
{
    private const string CharAlphabetUpper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string CharAlphabetLower = "abcdefghijklmnopqrstuvwxyz";
    private const string CharNumbers = "0123456789";
    /// <summary>
    /// 記号。Windows で使えない「\ / : * ? " &lt; &gt; |」と、
    /// macOS で使えない「/」「:」は含めていない（どちらの OS へ持ち出しても通る文字だけ）。
    /// </summary>
    private const string CharSymbols = "=-+!_#$%&()[]{}~^`'@";

    /// <summary>Windows のファイル名では使えないが、macOS では通ってしまう文字</summary>
    private static readonly char[] WindowsOnlyInvalidChars = ['\\', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly Random SharedRandom = new();
    private static readonly Lock RandomLock = new();

    /// <summary>
    /// 書式文字列を展開してファイル名を作る。
    /// </summary>
    /// <param name="formatString">書式文字列</param>
    /// <param name="filePath">元のファイルパス（&lt;filename&gt; と連番の衝突判定に使う）</param>
    /// <param name="charset">&lt;random:N&gt; で使用する文字種</param>
    /// <param name="containsInvalidChar">macOS のファイル名に使えない文字が含まれていたら true</param>
    public static string Format(
        string formatString, string filePath, AutoNameCharset charset, out bool containsInvalidChar)
    {
        containsInvalidChar = false;
        if (string.IsNullOrEmpty(formatString)) return "";

        var directory = Path.GetDirectoryName(filePath) ?? "";
        var hasSerialNumber = false;
        var serialNumber = 1;
        string result;

        while (true)
        {
            result = formatString;

            //-----------------------------------
            // ファイル名・拡張子
            //-----------------------------------
            result = result.Replace("<filename>", Path.GetFileNameWithoutExtension(filePath));
            result = result.Replace("<ext>", Path.GetExtension(filePath));

            //-----------------------------------
            // 日時
            //-----------------------------------
            // 置換は Regex.Replace ではなく文字列置換で行う。
            // 展開後の文字列（日付や乱数）が正規表現のメタ文字を含むと
            // パターン・置換文字列として解釈されて壊れるため。
            var match = new Regex(@"<date:(.*?)>", RegexOptions.IgnoreCase).Match(result);
            while (match.Success)
            {
                var dateTimeString = DateTime.Now.ToString(match.Groups[1].Value);
                result = result.Replace(match.Value, dateTimeString);
                match = match.NextMatch();
            }

            //-----------------------------------
            // 連番
            //-----------------------------------
            match = new Regex(@"<num:([0-9]*?)>", RegexOptions.IgnoreCase).Match(result);
            while (match.Success)
            {
                hasSerialNumber = true;
                if (int.TryParse(match.Groups[1].Value, out var figures) && figures > 0)
                {
                    result = result.Replace(match.Value, serialNumber.ToString(new string('0', figures)));
                }
                match = match.NextMatch();
            }

            //-----------------------------------
            // ランダム文字列
            //-----------------------------------
            match = new Regex(@"<random:([0-9]*?)>", RegexOptions.IgnoreCase).Match(result);
            while (match.Success)
            {
                if (!int.TryParse(match.Groups[1].Value, out var length) || length <= 0)
                {
                    length = 8;
                }

                result = result.Replace(match.Value, BuildRandomString(length, charset));
                match = match.NextMatch();
            }

            //-----------------------------------
            // 連番指定があり、かつ同名ファイルが存在するなら番号を進めてやり直す
            //-----------------------------------
            if (!hasSerialNumber || !File.Exists(Path.Combine(directory, result)))
            {
                break;
            }

            serialNumber++;
        }

        containsInvalidChar = ContainsInvalidFileNameChar(result);
        return result;
    }

    /// <summary>
    /// 設定から &lt;random:N&gt; に使う文字種を組み立てる。
    /// </summary>
    public static AutoNameCharset CharsetFrom(AppSettings settings)
    {
        var charset = AutoNameCharset.None;

        if (settings.AutoNameRandomUpperCase) charset |= AutoNameCharset.UpperCase;
        if (settings.AutoNameRandomLowerCase) charset |= AutoNameCharset.LowerCase;
        if (settings.AutoNameRandomNumbers) charset |= AutoNameCharset.Numbers;
        if (settings.AutoNameRandomSymbols) charset |= AutoNameCharset.Symbols;

        // すべて外された場合は英大文字にしておく（生成できないと困るため）
        return charset == AutoNameCharset.None ? AutoNameCharset.UpperCase : charset;
    }

    /// <summary>
    /// macOS のファイル名として使えない文字（'/' と NUL）が含まれているか。
    /// </summary>
    public static bool ContainsInvalidFileNameChar(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }

    /// <summary>
    /// Windows へ持ち出すと問題になる文字（\ : * ? " &lt; &gt; |）が含まれているか。
    /// macOS 上では有効なファイル名なので、エラーではなく警告に使う。
    /// </summary>
    public static bool ContainsWindowsInvalidFileNameChar(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return fileName.IndexOfAny(WindowsOnlyInvalidChars) >= 0;
    }

    private static string BuildRandomString(int length, AutoNameCharset charset)
    {
        var chars = "";
        if ((charset & AutoNameCharset.UpperCase) != 0) chars += CharAlphabetUpper;
        if ((charset & AutoNameCharset.LowerCase) != 0) chars += CharAlphabetLower;
        if ((charset & AutoNameCharset.Numbers) != 0) chars += CharNumbers;
        if ((charset & AutoNameCharset.Symbols) != 0) chars += CharSymbols;
        if (chars.Length == 0) chars = CharAlphabetUpper;

        var buffer = new char[length];
        lock (RandomLock)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = chars[SharedRandom.Next(chars.Length)];
            }
        }

        return new string(buffer);
    }
}
