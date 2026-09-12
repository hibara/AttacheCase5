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
using System.Text.Json.Serialization;

namespace AttacheCase.Models;

/// <summary>
/// Post-PPAP の公開鍵情報＋メタ情報。
/// accounts.json (~/Library/Application Support/AttacheCase/PostPPAP/) に
/// 配列として保存される。
///
/// 秘密鍵そのものは macOS Keychain に別途保存されるため、このクラスには
/// 公開情報（modulus/exponent/mlKemPublicKey/fingerprint）と
/// サーバ認証情報（emailHash/deviceToken）のみが含まれる。
/// </summary>
public class PostPpapKeyInfo
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("emailHash")]
    public string EmailHash { get; set; } = string.Empty;

    /// <summary>RSA 公開鍵 modulus (Base64)</summary>
    [JsonPropertyName("modulus")]
    public string Modulus { get; set; } = string.Empty;

    /// <summary>RSA 公開鍵 exponent (Base64)</summary>
    [JsonPropertyName("exponent")]
    public string Exponent { get; set; } = string.Empty;

    /// <summary>
    /// 16 文字 hex のフィンガープリント。
    /// SHA-256(RSA modulus [ || ML-KEM public key ]) の先頭 8 バイト。
    /// </summary>
    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>公開鍵暗号アルゴリズム識別子（Crypto.PkaAlgorithmId 参照）</summary>
    [JsonPropertyName("algorithmId")]
    public byte AlgorithmId { get; set; }

    /// <summary>ML-KEM-768 公開鍵 (Base64)。RSA 単体時は null。</summary>
    [JsonPropertyName("mlKemPublicKey")]
    public string? MlKemPublicKey { get; set; }

    [JsonPropertyName("registeredAt")]
    public DateTime RegisteredAt { get; set; }

    /// <summary>サーバ認証用トークン（ハートビート・鍵削除に使用）</summary>
    [JsonPropertyName("deviceToken")]
    public string? DeviceToken { get; set; }
}
