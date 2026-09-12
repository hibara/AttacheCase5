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
// Post-PPAP 鍵サーバ（attachecase-key-server.m-229.workers.dev）との
// JSON 通信に使う DTO 定義。
// プロトコルは Windows 版 PostPpapManager.cs と完全に互換。
//----------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace AttacheCase.Models;

// =====================================================================
// POST /api/register/initiate
// =====================================================================

public sealed class RegisterInitiateRequest
{
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("modulus")] public string Modulus { get; set; } = string.Empty;
    [JsonPropertyName("exponent")] public string Exponent { get; set; } = string.Empty;
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = string.Empty;
    [JsonPropertyName("algorithmId")] public byte AlgorithmId { get; set; }
    [JsonPropertyName("mlkemPublicKey")] public string? MlKemPublicKey { get; set; }
}

public sealed class RegisterInitiateResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("data")] public RegisterInitiateData? Data { get; set; }
}

public sealed class RegisterInitiateData
{
    [JsonPropertyName("pollToken")] public string PollToken { get; set; } = string.Empty;

    /// <summary>
    /// レート制限（429 / 503）で弾かれたときだけ入る、解除までの残り秒数。
    /// 同じ値が Retry-After ヘッダにも入っている。旧サーバは返さないので null 可。
    /// </summary>
    [JsonPropertyName("retryAfterSeconds")] public int? RetryAfterSeconds { get; set; }
}

// =====================================================================
// GET /api/register/status/{pollToken}
// =====================================================================

public sealed class RegisterStatusResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("data")] public RegisterStatusData? Data { get; set; }
}

public sealed class RegisterStatusData
{
    /// <summary>"pending" | "verified" | "expired"</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";

    [JsonPropertyName("deviceToken")] public string? DeviceToken { get; set; }
}

// =====================================================================
// GET /api/keys/{emailHash}
// =====================================================================

public sealed class LookupResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("data")] public LookupData? Data { get; set; }
}

public sealed class LookupData
{
    [JsonPropertyName("registered")] public bool Registered { get; set; }
    [JsonPropertyName("keys")] public LookupKeyEntry[]? Keys { get; set; }
}

public sealed class LookupKeyEntry
{
    [JsonPropertyName("modulus")] public string Modulus { get; set; } = string.Empty;
    [JsonPropertyName("exponent")] public string Exponent { get; set; } = string.Empty;
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = string.Empty;
    [JsonPropertyName("registeredAt")] public string? RegisteredAt { get; set; }
    [JsonPropertyName("mlkemPublicKey")] public string? MlKemPublicKey { get; set; }
    [JsonPropertyName("algorithmId")] public byte? AlgorithmId { get; set; }
}

// =====================================================================
// POST /api/keys/{emailHash}/heartbeat
// =====================================================================

public sealed class HeartbeatRequest
{
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = string.Empty;
    [JsonPropertyName("deviceToken")] public string DeviceToken { get; set; } = string.Empty;
}
