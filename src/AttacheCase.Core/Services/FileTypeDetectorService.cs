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
using System.Text;
using System.Xml.Linq;

namespace AttacheCase.Services;

public interface IFileTypeDetector
{
  bool IsBroken { get; }
  int CheckFileType(string filePath);
}

/// <summary>
///   ファイルタイプ判定サービス
/// </summary>
public class FileTypeDetectorService : IFileTypeDetector
{
  private const int HEADER_SIZE = 52;
  // 自己実行形式のトークンはスタブの直後にある。現行の Exeout スタブは約 3.3 MB
  //（BouncyCastle を Costura でマージ）なので、旧来の 50 KB では届かない。
  // 余裕を見て 8 MB まで走査する（MZ ヘッダのファイルに対してのみ行う走査）
  private const int EXE_SEARCH_LIMIT = 8 * 1024 * 1024;
  private const string ATC_SIGNATURE = "_AttacheCaseData";
  private const string ATC_BROKEN_SIGNATURE = "_Atc_Broken_Data";
  private const string RSA_SIGNATURE = "_AttacheCase_Rsa";

  // シグネチャ定義
  private static readonly byte[] ZIP_SIGNATURE = { 0x50, 0x4B, 0x03, 0x04 };

  /// <summary>
  ///   ファイルタイプを判定
  /// </summary>
  /// <returns>
  ///   0: 通常ファイル/ディレクトリ（.docx などの ZIP を容れ物にした書類を含む）
  ///   1: ATCファイル
  ///   2: EXE(ATC)ファイル
  ///   3: ZIPファイル
  ///   4: RSA暗号化データ
  ///   5: RSA公開鍵（XML）
  ///   6: RSA秘密鍵（XML）
  /// </returns>
  public int CheckFileType(string filePath)
  {
    IsBroken = false;

    // ディレクトリ判定
    if (Directory.Exists(filePath)) return 0;

    // ファイル存在確認
    if (!File.Exists(filePath)) return 0;

    try
    {
      using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

      // ファイルが小さすぎる場合
      if (fs.Length < 4) return 0;

      // 先頭52バイトを読み込む
      var headerSize = (int)Math.Min(HEADER_SIZE, fs.Length);
      var header = new byte[headerSize];
      fs.ReadExactly(header, 0, headerSize);

      // === 優先度順に判定 ===

      // 1. ZIP判定（先頭4バイト）
      //
      // .docx や .odt、.epub、.jar などは中身が ZIP なので、先頭 4 バイトだけを
      // 見ると書庫と区別がつかない。書庫として扱うと、暗号化するつもりで置いた
      // 書類が解凍画面へ回り、docProps や word といった中身がばらまかれる。
      // 利用者から見ればただの文書なので、通常ファイルとして返す
      if (headerSize >= 4 && IsZipFile(header))
      {
        return IsZipContainerDocument(filePath, header, fs) ? 0 : 3;
      }

      // 2. ATCファイル判定（オフセット4-19: 16バイトのシグネチャ）
      if (headerSize >= 20)
      {
        var atcType = CheckAtcSignature(header);
        if (atcType > 0) return atcType; // 1: ATC, 4: RSA暗号化データ
      }

      // 3. XML判定（RSA鍵データ）
      var xmlType = CheckXmlRsaKey(filePath, header);
      if (xmlType > 0) return xmlType; // 5: 公開鍵, 6: 秘密鍵

      // 4. EXE判定（先頭2バイトが "MZ"）
      if (headerSize >= 2 && IsExeFile(header))
      {
        // EXE内部にATCトークンがあるか確認（追加読み込みが必要）
        fs.Seek(0, SeekOrigin.Begin);
        if (SearchAtcTokenInExe(fs)) return 2; // EXE(ATC)
      }

      // 5. いずれにも該当しない
      return 0;
    }
    catch
    {
      return 0;
    }
  }

  /// <summary>
  ///   ファイルが破損しているかどうか
  /// </summary>
  public bool IsBroken { get; private set; }

  /// <summary>
  ///   ZIPファイル判定
  /// </summary>
  private static bool IsZipFile(byte[] header)
  {
    if (header.Length < 4) return false;

    // ZIPシグネチャ: "PK\x03\x04"
    return header[0] == ZIP_SIGNATURE[0] &&
           header[1] == ZIP_SIGNATURE[1] &&
           header[2] == ZIP_SIGNATURE[2] &&
           header[3] == ZIP_SIGNATURE[3];
  }

  /// <summary>
  ///   ZIP を容れ物にしているだけで、書庫ではない形式の拡張子。
  ///
  ///   ここに挙げたものは「解凍するもの」ではなく「開くもの」なので、
  ///   中身が ZIP でも通常ファイルとして扱う。
  /// </summary>
  private static readonly HashSet<string> ZipContainerExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    // Office Open XML（Word / Excel / PowerPoint）
    ".docx", ".docm", ".dotx", ".dotm",
    ".xlsx", ".xlsm", ".xlsb", ".xltx", ".xltm",
    ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm",
    // OpenDocument
    ".odt", ".ods", ".odp", ".odg", ".odf", ".odc", ".odm",
    ".ott", ".ots", ".otp", ".otg",
    // Apple iWork
    ".pages", ".numbers", ".key",
    // 電子書籍・その他のパッケージ
    ".epub", ".jar", ".war", ".ear", ".apk", ".aab", ".ipa",
    ".xpi", ".crx", ".vsix", ".nupkg", ".whl", ".appx", ".msix",
    ".kmz", ".3mf", ".sketch", ".mmap", ".scriv",
  };

  /// <summary>
  ///   ZIP の先頭にある項目名。ここから形式が分かる。
  /// </summary>
  private static readonly string[] ZipContainerFirstEntries =
  {
    "[Content_Types].xml",   // Office Open XML
    "mimetype",              // OpenDocument / EPUB
  };

  /// <summary>
  ///   中身は ZIP だが、書庫ではない書類・パッケージか。
  /// </summary>
  /// <remarks>
  ///   拡張子と、ZIP の最初の項目名の 2 つで見る。拡張子だけだと知らない形式を
  ///   取りこぼし、項目名だけだと拡張子を頼りにできる場合まで中を読むことになる。
  ///
  ///   どちらにも当たらなければ書庫として扱う。ここを緩めすぎると、本当の
  ///   ZIP を解凍できなくなる。
  /// </remarks>
  private static bool IsZipContainerDocument(string filePath, byte[] header, FileStream fs)
  {
    if (ZipContainerExtensions.Contains(Path.GetExtension(filePath))) return true;

    var firstEntry = ReadFirstZipEntryName(header, fs);
    if (firstEntry == null) return false;

    foreach (var name in ZipContainerFirstEntries)
    {
      if (string.Equals(firstEntry, name, StringComparison.Ordinal)) return true;
    }

    return false;
  }

  /// <summary>
  ///   ZIP の最初の項目名を読む。読めなければ null。
  /// </summary>
  /// <remarks>
  ///   ローカルファイルヘッダーは 30 バイトで、名前の長さはオフセット 26 の
  ///   2 バイト（リトルエンディアン）。名前はその直後に続く。
  ///   壊れた ZIP や細工された長さで読み込みすぎないよう、上限を設ける。
  /// </remarks>
  private static string? ReadFirstZipEntryName(byte[] header, FileStream fs)
  {
    const int LocalHeaderSize = 30;
    const int MaxEntryNameLength = 256;

    if (header.Length < LocalHeaderSize) return null;

    var nameLength = header[26] | (header[27] << 8);
    if (nameLength <= 0 || nameLength > MaxEntryNameLength) return null;
    if (fs.Length < LocalHeaderSize + nameLength) return null;

    try
    {
      var name = new byte[nameLength];
      fs.Seek(LocalHeaderSize, SeekOrigin.Begin);
      fs.ReadExactly(name, 0, nameLength);

      // 名前は UTF-8 か CP437。ここで見たいのは ASCII の範囲だけなので UTF-8 で足りる
      return Encoding.UTF8.GetString(name);
    }
    catch
    {
      return null;
    }
  }

  /// <summary>
  ///   ATCシグネチャ判定（オフセット4から16バイト）
  /// </summary>
  private int CheckAtcSignature(byte[] header)
  {
    if (header.Length < 20) return 0;

    // オフセット4-19の16バイトを文字列に変換
    var signature = Encoding.ASCII.GetString(header, 4, 16);

    // ATC通常ファイル
    if (signature == ATC_SIGNATURE) return 1;

    // ATC破損ファイル
    if (signature == ATC_BROKEN_SIGNATURE)
    {
      IsBroken = true;
      return 1;
    }

    // RSA暗号化データ
    if (signature == RSA_SIGNATURE) return 4;

    return 0;
  }

  /// <summary>
  ///   XML（RSA鍵）判定
  /// </summary>
  private int CheckXmlRsaKey(string filePath, byte[] header)
  {
    if (header.Length < 8) return 0;

    // XMLシグネチャ: "<?xml" または BOM + "<?xml"
    var hasBom = header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF;
    var offset = hasBom ? 3 : 0;

    if (header.Length < offset + 5) return 0;

    // "<?xml" のチェック
    if (header[offset] != 0x3C || //
        header[offset + 1] != 0x3F || // ?
        header[offset + 2] != 0x78 || // x
        header[offset + 3] != 0x6D || // m
        header[offset + 4] != 0x6C) // l
      return 0;

    // XMLをパースして公開鍵/秘密鍵を判定
    try
    {
      // 先頭が "<?xml" なだけの巨大なファイルを丸ごと読むと、
      // ドラッグ＆ドロップされた数 GB の XML でメモリを使い切る。
      // 鍵ファイルは数 KB なので、それを超えるものは鍵として扱わない
      const long MaxKeyFileSize = 64 * 1024;
      if (new FileInfo(filePath).Length > MaxKeyFileSize) return 0;

      var xmlContent = File.ReadAllText(filePath, Encoding.UTF8);

      // XMLとしてパース
      var xmlElement = XElement.Parse(xmlContent);

      if (xmlElement.Element("token")?.Value != "AttacheCase")
        return 0;

      var keyType = xmlElement.Element("type")?.Value;

      return keyType switch
      {
        "public" => 5, // RSA公開鍵
        "private" => 6, // RSA秘密鍵
        _ => 0
      };
    }
    catch
    {
      return 0;
    }
  }
  /// <summary>
  ///   EXEファイル判定（先頭2バイトが "MZ"）
  /// </summary>
  private static bool IsExeFile(byte[] header)
  {
    if (header.Length < 2) return false;

    // EXEシグネチャ: "MZ"
    return header[0] == 0x4D && header[1] == 0x5A;
  }

  /// <summary>
  ///   EXE内部でATCトークンを探す
  /// </summary>
  private bool SearchAtcTokenInExe(FileStream fs)
  {
    // トークンのバイト列
    var atcToken = Encoding.ASCII.GetBytes(ATC_SIGNATURE);
    var atcBrokenToken = Encoding.ASCII.GetBytes(ATC_BROKEN_SIGNATURE);

    // トークンがバッファの継ぎ目をまたいでも拾えるよう、末尾を次回へ持ち越す
    var overlap = Math.Max(atcToken.Length, atcBrokenToken.Length) - 1;
    var buffer = new byte[8192 + overlap];
    var carried = 0;   // 前回から持ち越したバイト数
    var position = 0;  // buffer[0] がファイル上のどの位置か

    while (position < EXE_SEARCH_LIMIT)
    {
      var toRead = Math.Min(buffer.Length - carried, EXE_SEARCH_LIMIT - position - carried);
      if (toRead <= 0) break;

      var bytesRead = fs.Read(buffer, carried, toRead);
      if (bytesRead == 0) break;

      var windowLen = carried + bytesRead;

      // 最後の有効なオフセットまで見る（"<" だと 1 つ手前で止まって取りこぼす）
      for (var i = 0; i + atcToken.Length <= windowLen; i++)
      {
        var absolute = position + i;

        // 通常トークンのチェック。先頭20バイト以降で見つかった場合はEXE(ATC)
        if (MatchesToken(buffer, i, atcToken) && absolute > 20)
          return true;

        // 破損トークンのチェック
        if (MatchesToken(buffer, i, atcBrokenToken))
        {
          IsBroken = true;
          if (absolute > 20) return true;
        }
      }

      carried = Math.Min(overlap, windowLen);
      Buffer.BlockCopy(buffer, windowLen - carried, buffer, 0, carried);
      position += windowLen - carried;
    }

    return false;
  }

  /// <summary>
  ///   バッファの指定位置がトークンと一致するか
  /// </summary>
  private static bool MatchesToken(byte[] buffer, int offset, byte[] token)
  {
    if (offset + token.Length > buffer.Length) return false;

    for (var i = 0; i < token.Length; i++)
      if (buffer[offset + i] != token[i])
        return false;

    return true;
  }
}