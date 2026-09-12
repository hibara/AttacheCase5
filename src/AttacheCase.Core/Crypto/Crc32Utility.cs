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

namespace AttacheCase.Crypto;

/// <summary>
/// CRC32 計算ユーティリティ。
/// AC5 ではファイル本体の整合性チェックに CRC32 を使用する
/// （AC4 までは MD5 だった）。
/// IEEE 802.3 多項式 0xEDB88320 を使用。
/// 出力は Windows 版 AC5 と一致するリトルエンディアンの 4 バイト。
/// </summary>
public static class Crc32Utility
{
    public const int CRC32_SIZE = 4;

    /// <summary>逐次計算（Update / FinalizeState）の初期値。</summary>
    public const uint InitialState = 0xFFFFFFFFu;

    private static readonly uint[] Table = CreateTable();

    /// <summary>逐次計算にバッファの内容を加える。</summary>
    public static uint Update(uint state, byte[] buffer, int offset, int count)
    {
        // 不正な範囲を「空の入力」として黙って通すと、未更新の state から
        // 誤った CRC32 が出る。ここで弾く
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, buffer.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length - offset);

        for (var i = offset; i < offset + count; i++)
            state = Table[(state ^ buffer[i]) & 0xFF] ^ (state >> 8);
        return state;
    }

    /// <summary>逐次計算を確定し、リトルエンディアン 4 バイトで返す。</summary>
    public static byte[] FinalizeState(uint state) => BitConverter.GetBytes(state ^ 0xFFFFFFFFu);

    private static uint[] CreateTable()
    {
        const uint poly = 0xEDB88320u;
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ poly : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    /// <summary>
    /// 指定ファイルの CRC32 を計算してリトルエンディアン 4 バイトで返す。
    /// キャンセルされた場合だけ null を返す。
    /// </summary>
    /// <remarks>
    /// 読み取りに失敗したときは例外をそのまま投げる。呼び出し側でそのファイルを
    /// 対象から外すこと。
    /// ここで例外を握り潰して null を返すと、索引に CRC32 が書かれないまま
    /// 暗号化が「成功」してしまう。復号側は「サイズ &gt; 0 のファイルなら CRC32 が
    /// 4 バイトある」前提で読むため、以降のエントリがすべて 4 バイトずれて壊れる。
    /// </remarks>
    public static byte[]? ComputeFileCrc32(string filePath, Func<bool>? cancelCheck = null, int bufferSize = 8192)
    {
        // 0 以下だと fs.Read が即 0 を返し、中身があるファイルでも
        // 空ファイルの CRC32 を返してしまう
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[bufferSize];
        var crc = InitialState;
        int bytesRead;
        while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (cancelCheck != null && cancelCheck()) return null;
            crc = Update(crc, buffer, 0, bytesRead);
        }
        return FinalizeState(crc);
    }
}
