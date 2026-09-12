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
// パスワードファイル（/pfile, /dpfile）から鍵素材（32 バイト）を求める。
// Windows 版 AttacheCase\Headless\PasswordFileHash.cs の移植。
// バイト列は Windows 版と完全互換でなければならない（相互復号のため）。
//----------------------------------------------------------------------

using System.IO;
using System.Security.Cryptography;

namespace AttacheCase.Cli;

public static class PasswordFileHash
{
    /// <summary>
    /// データバージョン 130 以降で使用する SHA-256 ハッシュ。
    /// </summary>
    public static byte[] ComputeSha256(string filePath)
    {
        using var bs = new BufferedStream(File.OpenRead(filePath), 16 * 1024 * 1024);
        return SHA256.HashData(bs);
    }

    /// <summary>
    /// データバージョン 130 未満（アタッシェケース2 系）で使用する旧方式。
    /// SHA-1（20 バイト）＋ ファイルを 255 バイトずつ読み進めた最後のブロックの先頭 12 バイト。
    /// </summary>
    /// <remarks>
    /// 旧形式の復号互換のためだけに存在する。新規暗号化では使用しない。
    /// </remarks>
    public static byte[] ComputeLegacySha1(string filePath)
    {
        var buffer = new byte[255];
        var result = new byte[32];

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        var hash = SHA1.HashData(fs);
        for (var i = 0; i < 20; i++)
        {
            result[i] = hash[i];
        }

        fs.Seek(0, SeekOrigin.Begin);
        // 最後の 255 バイトブロックを buffer に残す（Windows 版と同じ読み方）
        while (fs.Read(buffer, 0, 255) > 0)
        {
        }

        for (var i = 0; i < 12; i++)
        {
            result[20 + i] = buffer[i];
        }

        return result;
    }

    /// <summary>
    /// 暗号化ファイルのデータバージョンに応じた方式でハッシュを求める。
    /// </summary>
    /// <param name="filePath">パスワードファイルのパス</param>
    /// <param name="dataFileVersion">対象の暗号化ファイルのデータバージョン</param>
    public static byte[] ComputeForDataFileVersion(string filePath, int dataFileVersion)
    {
        return dataFileVersion < 130
            ? ComputeLegacySha1(filePath)
            : ComputeSha256(filePath);
    }
}
