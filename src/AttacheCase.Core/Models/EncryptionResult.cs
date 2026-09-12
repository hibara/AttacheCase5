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
/// 暗号化・復号処理の結果
/// </summary>
public class EncryptionResult
{
    /// <summary>
    /// 処理が成功したかどうか
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// エラーコード
    /// </summary>
    public int ErrorCode { get; set; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// エラーが発生したファイルパス
    /// </summary>
    public string ErrorFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 出力ファイルパス
    /// </summary>
    public string OutputFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 処理時間
    /// </summary>
    public string ProcessingTime { get; set; } = string.Empty;

    /// <summary>
    /// 処理したファイル数
    /// </summary>
    public int ProcessedFileCount { get; set; }

    /// <summary>
    /// ユーザーによってキャンセルされたかどうか
    /// </summary>
    public bool WasCancelled { get; set; }

    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static EncryptionResult CreateSuccess(string outputPath, int fileCount, string processingTime)
    {
        return new EncryptionResult
        {
            Success = true,
            OutputFilePath = outputPath,
            ProcessedFileCount = fileCount,
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// エラー結果を作成
    /// </summary>
    public static EncryptionResult CreateError(int errorCode, string errorMessage, string errorFilePath = "")
    {
        return new EncryptionResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ErrorFilePath = errorFilePath
        };
    }

    /// <summary>
    /// キャンセル結果を作成
    /// </summary>
    public static EncryptionResult CreateCancelled()
    {
        return new EncryptionResult
        {
            Success = false,
            WasCancelled = true,
            ErrorCode = -1,
            ErrorMessage = Services.LocalizationService.Instance["Error_CanceledByUser"]
        };
    }
}

/// <summary>
/// エラーコード定数
/// </summary>
public static class ErrorCodes
{
    // Status code
    public const int ENCRYPT_SUCCEEDED = 1;
    public const int DECRYPT_SUCCEEDED = 2;
    public const int DELETE_SUCCEEDED = 3;
    public const int READY_FOR_ENCRYPT = 4;
    public const int READY_FOR_DECRYPT = 5;
    public const int ENCRYPTING = 6;
    public const int DECRYPTING = 7;
    public const int DELETING = 8;

    // Error code
    public const int USER_CANCELED = -1;
    public const int ERROR_UNEXPECTED = -100;
    public const int NOT_ATC_DATA = -101;
    public const int ATC_BROKEN_DATA = -102;
    public const int NO_DISK_SPACE = -103;
    public const int FILE_INDEX_NOT_FOUND = -104;
    public const int PASSWORD_TOKEN_NOT_FOUND = -105;
    public const int NOT_CORRECT_HASH_VALUE = -106;
    public const int INVALID_FILE_PATH = -107;
    public const int OS_DENIES_ACCESS = -108;
    public const int DATA_NOT_FOUND = -109;
    public const int DIRECTORY_NOT_FOUND = -110;
    public const int DRIVE_NOT_FOUND = -111;
    public const int FILE_NOT_LOADED = -112;
    public const int FILE_NOT_FOUND = -113;
    public const int PATH_TOO_LONG = -114;
    public const int CRYPTOGRAPHIC_EXCEPTION = -115;
    public const int RSA_KEY_GUID_NOT_MATCH = -116;
    public const int IO_EXCEPTION = -117;
}
