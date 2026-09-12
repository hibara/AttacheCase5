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

namespace AttacheCase.Crypto;

/// <summary>
/// 公開鍵暗号アルゴリズム識別子。
/// AC5 ファイルフォーマットのヘッダに記録される 1 バイトの識別子で、
/// どの公開鍵暗号で共通鍵を保護したかを表す。
/// </summary>
public static class PkaAlgorithmId
{
    public const byte None = 0x00;
    public const byte Rsa2048 = 0x01;
    public const byte Rsa4096 = 0x02;
    public const byte MlKem768 = 0x10;
    public const byte Rsa2048MlKem768 = 0x11;
    public const byte Rsa4096MlKem768 = 0x12;
}
