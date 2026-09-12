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
// コマンドライン実行時の終了コード。
// Windows 版 AttacheCase\Headless\AtcExitCode.cs と同じ値を用いる。
// バッチ／シェルスクリプトから参照される契約なので、既存の値は変更せず、
// 新しい状態は末尾に追加すること。
//----------------------------------------------------------------------

namespace AttacheCase.Cli;

public static class AtcExitCode
{
    /// <summary>正常終了</summary>
    public const int Success = 0;

    /// <summary>一般的なエラー（分類できないもの）</summary>
    public const int GeneralError = 1;

    /// <summary>コマンドラインの指定が不正（不明なオプション、引数不足など）</summary>
    public const int UsageError = 2;

    /// <summary>入力ファイル／フォルダーが見つからない</summary>
    public const int FileNotFound = 3;

    /// <summary>パスワードが違う、または復号に失敗した</summary>
    public const int DecryptionFailed = 4;

    /// <summary>出力先に同名ファイルが存在し、上書きが許可されていない</summary>
    public const int OutputExists = 5;

    /// <summary>ディスクの空き容量が不足している</summary>
    public const int NoDiskSpace = 6;

    /// <summary>OS にアクセスを拒否された（権限不足、ファイルロックなど）</summary>
    public const int AccessDenied = 7;

    /// <summary>暗号化ファイルが破損している、または CRC32 が一致しない</summary>
    public const int BrokenData = 8;

    /// <summary>対応していないファイル形式・データバージョン</summary>
    public const int UnsupportedFormat = 9;

    /// <summary>利用者による中断</summary>
    public const int Canceled = 10;

    /// <summary>想定外の内部エラー</summary>
    public const int InternalError = 11;

    /// <summary>
    /// パスワードファイルは、この出力形式では使えない（パスワード付き ZIP）。
    /// </summary>
    /// <remarks>
    /// 受け取る側はエクスプローラーにパスワードを打ち込んで開くため、
    /// パスワードファイルから導いた鍵は相手には再現できない。
    /// 黙って通すと誰も開けない ZIP ができあがるので、明示的に断る。
    /// </remarks>
    public const int PasswordFileNotSupported = 12;

    //======================================================================
    // 暗号化・復号エンジンのリターンコード（AttacheCase.Models.ErrorCodes）を
    // 終了コードへ写像する。値は Windows 版と同一。
    //======================================================================

    private const int EngineUserCanceled = -1;
    private const int EngineErrorUnexpected = -100;
    private const int EngineNotAtcData = -101;
    private const int EngineAtcBrokenData = -102;
    private const int EngineNoDiskSpace = -103;
    private const int EngineFileIndexNotFound = -104;
    private const int EnginePasswordTokenNotFound = -105;
    private const int EngineNotCorrectHashValue = -106;
    private const int EngineInvalidFilePath = -107;
    private const int EngineOsDeniesAccess = -108;
    private const int EngineDataNotFound = -109;
    private const int EngineDirectoryNotFound = -110;
    private const int EngineDriveNotFound = -111;
    private const int EngineFileNotLoaded = -112;
    private const int EngineFileNotFound = -113;
    private const int EnginePathTooLong = -114;
    private const int EngineCryptographicException = -115;
    private const int EngineRsaKeyGuidNotMatch = -116;
    private const int EngineIoException = -117;
    private const int EnginePasswordFileNotSupported = -120;

    /// <summary>
    /// 暗号化・復号エンジンのリターンコードを、プロセスの終了コードへ写像する。
    /// </summary>
    public static int FromEngineReturnCode(int engineReturnCode)
    {
        // 正の値は成功系ステータス（ENCRYPT_SUCCEEDED = 1, DECRYPT_SUCCEEDED = 2 など）
        if (engineReturnCode > 0) return Success;

        return engineReturnCode switch
        {
            EngineUserCanceled => Canceled,
            EngineNoDiskSpace => NoDiskSpace,
            EngineOsDeniesAccess => AccessDenied,

            EngineAtcBrokenData or EngineNotCorrectHashValue or
                EngineFileIndexNotFound or EngineDataNotFound => BrokenData,

            EngineNotAtcData => UnsupportedFormat,

            // パスワード不一致は GCM 認証タグの検証失敗として現れる
            EngineCryptographicException or EnginePasswordTokenNotFound or
                EngineRsaKeyGuidNotMatch => DecryptionFailed,

            EngineFileNotFound or EngineDirectoryNotFound or EngineDriveNotFound or
                EngineInvalidFilePath or EnginePathTooLong or EngineFileNotLoaded => FileNotFound,

            EnginePasswordFileNotSupported => PasswordFileNotSupported,

            EngineIoException or EngineErrorUnexpected => GeneralError,

            _ => GeneralError,
        };
    }
}
