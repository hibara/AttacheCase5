//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
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
    /// キャンセルチェック用デリゲート。true を返すとキャンセルされ null が返る。
    /// null の場合はキャンセルチェックを行わない。
    /// </param>
    /// <param name="bufferSize">読み込みバッファサイズ</param>
    /// <returns>CRC32 チェックサム（4バイト、リトルエンディアン）。キャンセル時は null</returns>
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
        return null;
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
            return null;
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
