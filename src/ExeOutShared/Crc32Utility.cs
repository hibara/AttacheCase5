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
  //======================================================================
  // CRC32（ファイル破損チェック用）
  //======================================================================
  internal static class Crc32Utility
  {
    /// <summary>CRC32 チェックサムのサイズ（バイト）</summary>
    public const int CRC32_SIZE = 4;

    /// <summary>
    /// CRC32 ルックアップテーブル（IEEE 802.3 / PKZip 系、多項式 0xEDB88320）
    /// </summary>
    private static readonly uint[] Crc32Table = GenerateCrc32Table();

    private static uint[] GenerateCrc32Table()
    {
      var table = new uint[256];

      for (uint i = 0; i < 256; i++)
      {
        uint crc = i;
        for (int j = 0; j < 8; j++)
        {
          crc = (crc & 1u) != 0
            ? (crc >> 1) ^ 0xEDB88320u
            : (crc >> 1);
        }
        table[i] = crc;
      }

      return table;
    }

    /// <summary>
    /// ファイルの CRC32 チェックサムを計算する。
    /// 暗号学的なハッシュではなく、復号・解凍後のファイル破損検出用。
    /// 改ざん検知は AES-GCM の認証タグが担うため、ここでは速度を優先する。
    /// </summary>
    /// <param name="filePath">チェックサム対象のファイルパス</param>
    /// <param name="cancelCheck">
    /// キャンセルチェック用デリゲート。true を返すと
    /// <see cref="OperationCanceledException"/> を投げる。
    /// null の場合はキャンセルチェックを行わない。
    /// </param>
    /// <param name="bufferSize">読み込みバッファサイズ</param>
    /// <returns>CRC32 チェックサム（4バイト、リトルエンディアン）。常に 4 バイトを返す。</returns>
    /// <exception cref="OperationCanceledException">キャンセルされた場合。</exception>
    /// <remarks>
    /// 以前はキャンセル時に <c>null</c> を返していた。呼び出し元
    /// （<c>FileEncrypt5</c>）は索引を書くときに <c>null</c> を検査しておらず、
    /// 「サイズ &gt; 0 のファイルなら必ず 4 バイト読む」という読み手の前提と
    /// 食い違って**索引がずれる**危険があった。
    /// 成功時の値（4 バイト）とキャンセル通知を同じ型に混ぜないため、
    /// キャンセルは例外で伝える。呼び出し元には
    /// <c>OperationCanceledException</c> を透過させる catch フィルタが既にある。
    /// </remarks>
    public static byte[] ComputeFileCrc32(string filePath, Func<bool> cancelCheck = null, int bufferSize = 8192)
    {
      if (filePath == null)
      {
        throw new ArgumentNullException(nameof(filePath));
      }

      if (bufferSize <= 0)
      {
        throw new ArgumentOutOfRangeException(nameof(bufferSize), "bufferSize は 1 以上である必要があります。");
      }

      if (cancelCheck != null && cancelCheck())
      {
        throw new OperationCanceledException();
      }

      uint crc = 0xFFFFFFFFu;

      using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
      {
        var buffer = new byte[bufferSize];
        int bytesRead;

        while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
          if (cancelCheck != null && cancelCheck())
          {
            throw new OperationCanceledException();
          }

          for (int i = 0; i < bytesRead; i++)
          {
            crc = Crc32Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
          }
        }
      }

      crc ^= 0xFFFFFFFFu;

      // リトルエンディアン（ファイルフォーマット全体と統一）
      return BitConverter.GetBytes(crc);
    }
  }
}
