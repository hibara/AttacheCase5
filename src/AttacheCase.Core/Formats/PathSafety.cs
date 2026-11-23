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
using System.IO;

namespace AttacheCase
{
  /// <summary>
  /// ディレクトリ・トラバーサル対策のパス判定をまとめたユーティリティ。
  /// </summary>
  /// <remarks>
  /// <para>
  /// ver.5 の途中まで、復号先の検査が下記の形で各所に散らばっていた。
  /// </para>
  /// <code>
  /// OutFilePath = Path.GetFullPath(OutFilePath);   // ".." はここで解決される
  /// if (OutFilePath.StartsWith(OutDirPath))        // ← 末尾区切りが無い
  /// </code>
  /// <para>
  /// 正規化してあるからこそ、末尾区切りの欠落がそのまま穴になる。
  /// 保存先が <c>C:\Users\foo\out</c> のとき、エントリ名 <c>..\outX\evil.txt</c> は
  /// <c>C:\Users\foo\outX\evil.txt</c> に解決され、この文字列は <c>C:\Users\foo\out</c> で
  /// 始まるため検査を通過してしまう。つまり保存先の**兄弟フォルダー**へ書き込める。
  /// </para>
  /// <para>
  /// あわせて、旧実装は <see cref="StringComparison"/> を指定していなかったため
  /// カルチャ依存の比較になっていた。大文字小文字の扱いは OS で変える
  /// （<see cref="PathComparison"/> を参照）。
  /// </para>
  /// <para>
  /// 2026-08-22、Mac 版のレビュー（CodeRabbit）で同じ穴が見つかったのを受けて共通化した。
  /// </para>
  /// </remarks>
  internal static class PathSafety
  {
    /// <summary>
    /// 旧形式（v105 / v130）の書庫に入っている項目名を、実行 OS の区切り文字に直す。
    /// </summary>
    /// <remarks>
    /// 旧形式は Windows 版しか書かないので、項目名の区切りは常に「\」で、フォルダーの項目は
    /// 末尾が「\」になる。Windows 以外では「\」は区切りではなく名前の一部なので、そのまま
    /// 保存先に結合すると、階層ではなく「dir\child.txt」という名前のファイルができてしまう。
    /// Windows では何も変えない。保存先の内側かどうかの検査（<see cref="IsInsideDirectory"/>）より
    /// 前に通すこと（変換後の「..」も検査の対象にするため）。
    /// </remarks>
    public static string ToNativeSeparators(string entryName)
    {
      if (string.IsNullOrEmpty(entryName) || Path.DirectorySeparatorChar == '\\')
      {
        return entryName;
      }

      return entryName.Replace('\\', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// パスを正規化する。失敗した場合は <c>null</c> を返す。
    /// </summary>
    /// <remarks>
    /// <see cref="Path.GetFullPath(string)"/> は不正な文字・長すぎるパス・権限不足などで
    /// 例外を投げる。呼び出し側で毎回 try/catch を書かずに済むようにまとめてある。
    /// </remarks>
    public static string GetFullPathOrNull(string path)
    {
      if (string.IsNullOrEmpty(path))
      {
        return null;
      }

      try
      {
        return Path.GetFullPath(path);
      }
      catch (Exception)
      {
        return null;
      }
    }

    /// <summary>
    /// 前方一致の比較に使えるよう、ディレクトリパスを正規化して末尾に区切り文字を付ける。
    /// 正規化に失敗した場合は <c>null</c> を返す。
    /// </summary>
    public static string NormalizeDirectoryForCompare(string dirPath)
    {
      var fullDirPath = GetFullPathOrNull(dirPath);
      if (fullDirPath == null)
      {
        return null;
      }

      if (fullDirPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
          fullDirPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
      {
        return fullDirPath;
      }

      return fullDirPath + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// <paramref name="candidatePath"/> が <paramref name="dirPath"/> の内側（またはそれ自身）かどうかを返す。
    /// </summary>
    /// <param name="candidatePath">検査したいパス。正規化していなくてもよい。</param>
    /// <param name="dirPath">保存先ディレクトリ。正規化していなくてもよい。</param>
    /// <returns>
    /// 内側なら <c>true</c>。外側、またはどちらかが正規化できない場合は <c>false</c>。
    /// 判定できないものは通さない（安全側に倒す）。
    /// </returns>
    /// <summary>
    /// パス比較の大文字小文字規則。
    /// </summary>
    /// <remarks>
    /// Windows のファイルシステムは大文字小文字を区別しないので IgnoreCase が正しい。
    /// それ以外（Linux、ケースセンシティブな macOS ボリューム）で IgnoreCase を使うと、
    /// 保存先 <c>/home/u/out</c> に対して <c>/home/u/OUT/evil.txt</c>（別ディレクトリ）を
    /// 内側と誤認する。ボリュームごとの実際の規則までは判定できないため、
    /// 非 Windows は区別する側に倒す。ケースインセンシティブな macOS の既定ボリュームでは
    /// 「保存先から出て、大文字小文字を変えて戻る」攻撃形のパスだけが誤って拒否される
    /// （安全側の誤り）。正規のエントリは保存先の文字列をそのまま前置するため影響しない。
    /// </remarks>
    private static StringComparison PathComparison
      => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static bool IsInsideDirectory(string candidatePath, string dirPath)
    {
      var fullCandidatePath = GetFullPathOrNull(candidatePath);
      var compareDirPath = NormalizeDirectoryForCompare(dirPath);

      if (fullCandidatePath == null || compareDirPath == null)
      {
        return false;
      }

      // 保存先ディレクトリそのもの（末尾区切りの有無だけの違い）は内側として扱う
      if (string.Equals(fullCandidatePath + Path.DirectorySeparatorChar,
                        compareDirPath, PathComparison))
      {
        return true;
      }

      // 末尾区切りを付けたうえで前方一致を見る。
      // これを怠ると、保存先 "...\out" に対して "...\outX\evil.txt" が通ってしまう。
      return fullCandidatePath.StartsWith(compareDirPath, PathComparison);
    }

    /// <summary>
    /// 保存先から対象パスまでの途中に、シンボリックリンク／ジャンクション
    /// （再解析ポイント）が挟まっていないかを調べる。
    /// </summary>
    /// <param name="dirPath">保存先ディレクトリ。</param>
    /// <param name="candidatePath">書き込もうとしているパス。</param>
    /// <returns>途中に再解析ポイントがある、または判定できない場合は <c>true</c>。</returns>
    /// <remarks>
    /// <para>
    /// <see cref="IsInsideDirectory"/> は**パス文字列**としての包含関係しか見ない。
    /// 保存先の配下に攻撃者があらかじめジャンクション
    /// （例: <c>C:\out\link</c> → <c>C:\Windows</c>）を作っておくと、
    /// <c>C:\out\link\evil.dll</c> は文字列上は内側と判定されるが、
    /// 実体は保存先の外にある。<c>Directory.CreateDirectory</c> も
    /// <c>FileStream</c> もリンクを辿って書き込むため、これだけでは防げない。
    /// </para>
    /// <para>
    /// 保存先ディレクトリ自身は検査しない。利用者が明示的に選んだ場所であり、
    /// そこがリンクであること自体は正当だから。検査するのはその配下だけ。
    /// </para>
    /// <para>
    /// .NET Framework 4.8 には <c>FileSystemInfo.LinkTarget</c> が無いため、
    /// <see cref="FileAttributes.ReparsePoint"/> を自前で見る
    /// （<c>AttacheCase/Wipe.cs</c> と同じ方針）。
    /// </para>
    /// </remarks>
    public static bool HasReparsePointBelow(string dirPath, string candidatePath)
    {
      var root = NormalizeDirectoryForCompare(dirPath);
      var full = GetFullPathOrNull(candidatePath);

      if (root == null || full == null)
      {
        return true;   // 判定できないものは通さない
      }

      if (!IsInsideDirectory(full, dirPath))
      {
        return true;   // そもそも外側。呼び出し側の検査漏れ
      }

      // 保存先ディレクトリ自身が渡された場合は、確かめる階層が無い。
      // IsInsideDirectory は保存先自身を内側として通すので、ここへ来る。
      // full は末尾区切りなし（C:\out）、root は末尾区切りあり（C:\out\）なので、
      // 素直に Substring(root.Length) すると範囲外になる。
      // fNoParentFolder で親フォルダー名を除去した結果が空文字になると、
      // Path.Combine(OutDirPath, "") が OutDirPath そのものになって実際に到達する。
      if (full.Length < root.Length)
      {
        return false;
      }

      // 保存先の 1 つ下から順に、各階層を確かめる。
      // 末尾（書き込むファイル自身）も含める。まだ存在しない階層は素通りする。
      var relative = full.Substring(root.Length);
      var current = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

      foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
      {
        if (part.Length == 0)
        {
          continue;
        }

        current = current + Path.DirectorySeparatorChar + part;

        try
        {
          // 存在しない階層は、これから作るものなのでリンクではない
          if (!File.Exists(current) && !Directory.Exists(current))
          {
            continue;
          }

          var attributes = File.GetAttributes(current);
          if ((attributes & FileAttributes.ReparsePoint) != 0)
          {
            return true;
          }
        }
        catch (FileNotFoundException)
        {
          continue;
        }
        catch (DirectoryNotFoundException)
        {
          continue;
        }
        catch (Exception)
        {
          // 属性が読めない相手へ書き込みにいくのは危険なので、通さない側に倒す
          return true;
        }
      }

      return false;
    }
  }
}
