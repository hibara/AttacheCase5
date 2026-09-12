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

namespace AttacheCase.Models;

/// <summary>
/// 暗号化・復号処理の進捗状況
/// </summary>
public class EncryptionProgress
{
    /// <summary>
    /// 処理の種類
    /// </summary>
    public ProcessType Type { get; set; }

    /// <summary>
    /// 進捗率（0-10000、0.01%単位）
    /// </summary>
    public int ProgressPercentage { get; set; }

    /// <summary>
    /// 現在処理中のファイルパス
    /// </summary>
    public string CurrentFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 処理済みファイル数
    /// </summary>
    public int ProcessedFiles { get; set; }

    /// <summary>
    /// 総ファイル数
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string StatusMessage { get; set; } = string.Empty;
}

/// <summary>
/// 処理の種類
/// </summary>
public enum ProcessType
{
    /// <summary>
    /// 準備中
    /// </summary>
    Preparing,

    /// <summary>
    /// 暗号化中
    /// </summary>
    Encrypting,

    /// <summary>
    /// 復号中
    /// </summary>
    Decrypting,

    /// <summary>
    /// 削除中
    /// </summary>
    Deleting
}
