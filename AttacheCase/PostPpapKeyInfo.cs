//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using System;
using Newtonsoft.Json;

namespace AttacheCase
{
  public class PostPpapKeyInfo
  {
    [JsonProperty("email")]
    public string Email { get; set; }

    [JsonProperty("emailHash")]
    public string EmailHash { get; set; }

    [JsonProperty("modulus")]
    public string Modulus { get; set; }

    [JsonProperty("exponent")]
    public string Exponent { get; set; }

    [JsonProperty("fingerprint")]
    public string Fingerprint { get; set; }

    [JsonProperty("encryptedPrivateKey")]
    public string EncryptedPrivateKey { get; set; }

    [JsonProperty("registeredAt")]
    public DateTime RegisteredAt { get; set; }

    [JsonProperty("algorithmId")]
    public byte AlgorithmId { get; set; }  // PkaAlgorithmId 定数値

    [JsonProperty("mlKemPublicKey")]
    public string MlKemPublicKey { get; set; }  // Base64 ML-KEM-768 公開鍵（RSA単体時は null）

    [JsonProperty("mlKemEncryptedPrivateKey")]
    public string MlKemEncryptedPrivateKey { get; set; }  // DPAPI 暗号化済み ML-KEM-768 秘密鍵（検索取得時は null）

    [JsonProperty("deviceToken")]
    public string DeviceToken { get; set; }  // サーバ認証用トークン（ハートビート・鍵削除に使用）
  }
}
