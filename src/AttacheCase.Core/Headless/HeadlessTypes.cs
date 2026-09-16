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
// ヘッドレス実行（コマンドライン）の要求・結果・進捗の型。
// Windows 版 AttacheCase\Headless\HeadlessTypes.cs の移植。
// 公開契約（プロパティの意味・既定値）は Windows 版と一致させること。
//----------------------------------------------------------------------
using System.Collections.Generic;
using System.IO.Compression;

namespace AttacheCase.Headless;

/// <summary>
/// ヘッドレス実行の進捗種別。
/// </summary>
public enum HeadlessPhase
{
    /// <summary>準備中（ファイル走査、ヘッダ解析など）</summary>
    Preparing = 0,
    /// <summary>暗号化中</summary>
    Encrypting = 1,
    /// <summary>復号中</summary>
    Decrypting = 2,
    /// <summary>元ファイル削除中</summary>
    Deleting = 3,
    /// <summary>完了</summary>
    Completed = 4,
}

/// <summary>
/// ヘッドレス実行の進捗通知。
/// </summary>
public sealed class HeadlessProgress
{
    /// <summary>現在の処理段階</summary>
    public HeadlessPhase Phase { get; set; }

    /// <summary>進捗率（0.0〜100.0）。不明な場合は -1</summary>
    public double Percent { get; set; } = -1;

    /// <summary>処理中のファイル名またはパス</summary>
    public string CurrentItem { get; set; } = "";
}

/// <summary>
/// 暗号化の実行要求。
/// </summary>
/// <remarks>
/// <see cref="Models.AppSettings"/> を参照せず、必要な値をすべて明示的に受け取る。
/// バッチ処理の挙動が GUI 側の設定変更に引きずられないようにするため。
/// どの設定をどう解釈するかは呼び出し側（CLI）の責務。
/// </remarks>
public sealed class HeadlessEncryptRequest
{
    /// <summary>暗号化対象のファイル／フォルダーパス（絶対パス）</summary>
    public IList<string>? InputPaths { get; set; }

    /// <summary>出力する暗号化ファイルのフルパス</summary>
    public string? OutputFilePath { get; set; }

    /// <summary>
    /// 従来の RSA 公開鍵ファイル（`.atcpub`、XML 形式）のパス。
    /// RSA 鍵ファイルによる新規暗号化は ver.5 で終了したため、指定は明示的に断られる。
    /// </summary>
    public string? RsaPublicKeyFilePath { get; set; }

    /// <summary>
    /// Post-PPAP の宛先メールアドレス。
    /// 指定するとキーサーバから公開鍵を検索して公開鍵方式で暗号化する（ネットワーク通信が発生する）。
    /// </summary>
    public string? PostPpapRecipientEmail { get; set; }

    /// <summary>パスワード文字列</summary>
    public string? Password { get; set; }

    /// <summary>パスワードファイルの SHA-256（指定時はパスワード文字列より優先される）</summary>
    public byte[]? PasswordBinary { get; set; }

    /// <summary>自己実行形式（EXE）で出力するか</summary>
    public bool SelfExecutable { get; set; }

    /// <summary>パスワード付き ZIP として出力するか。</summary>
    /// <remarks>
    /// 独自形式（.atc）とはまったく別系統。Argon2id も AES-GCM も使わず、
    /// 相互運用のために ZIP の仕様に従う。<c>SelfExecutable</c> とは併用できない。
    /// </remarks>
    public bool ZipEncryption { get; set; }

    /// <summary>ZIP の暗号アルゴリズム。0: ZipCrypto / 1: WinZipAes128 / 2: WinZipAes256</summary>
    /// <remarks>
    /// 既定の 0（ZipCrypto）は暗号として弱いが、エクスプローラーで開ける唯一の方式。
    /// </remarks>
    public int ZipAlgorithm { get; set; }

    /// <summary>元ファイルのタイムスタンプを暗号化ファイルへ引き継ぐか</summary>
    public bool KeepTimeStamp { get; set; }

    /// <summary>圧縮レベル</summary>
    public CompressionLevel Compression { get; set; } = CompressionLevel.Optimal;

    /// <summary>パスワード入力回数の制限（1〜10。範囲外は 3 として扱う）</summary>
    public int MissTypeLimits { get; set; } = 3;

    /// <summary>
    /// 入力回数の制限を超えたら暗号化ファイルを破壊してよいか（/breakfile）。
    /// 平文ヘッダの 4 バイト目に記録され、復号する側がこれを見て判断する。
    /// </summary>
    public bool BreakFileWhenLimitExceeded { get; set; }

    /// <summary>出力先に同名ファイルがある場合に上書きするか</summary>
    public bool Overwrite { get; set; }
}

/// <summary>
/// 復号の実行要求。
/// </summary>
public sealed class HeadlessDecryptRequest
{
    /// <summary>復号する暗号化ファイルのフルパス</summary>
    public string? InputFilePath { get; set; }

    /// <summary>展開先ディレクトリ</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// 従来の RSA 秘密鍵ファイル（`.atcpvt`、XML 形式）のパス。
    /// 公開鍵方式で暗号化されたファイルの復号に使う。
    /// </summary>
    public string? RsaPrivateKeyFilePath { get; set; }

    /// <summary>
    /// この PC に登録済みの Post-PPAP アカウントの秘密鍵を使って復号を試みるか。
    /// 秘密鍵は OS の保護機構（Windows: DPAPI / macOS: キーチェーン）で
    /// この PC・このユーザーに紐付いて保存されているため、引数で鍵を渡す必要がない。
    /// </summary>
    public bool UsePostPpapAccounts { get; set; }

    /// <summary>パスワード文字列</summary>
    public string? Password { get; set; }

    /// <summary>パスワードファイルの SHA-256（指定時はパスワード文字列より優先される）</summary>
    public byte[]? PasswordBinary { get; set; }

    /// <summary>既存ファイルを上書きするか（false なら既存ファイルはスキップ）</summary>
    public bool Overwrite { get; set; }

    /// <summary>親フォルダーを作らずに展開するか</summary>
    public bool NoParentFolder { get; set; }

    /// <summary>
    /// 復号したファイルのタイムスタンプを「復号した日時」にするか。
    /// false（既定）なら暗号化ファイルに記録された元のタイムスタンプを復元する。
    /// 本体 GUI の「復号したファイルのタイムスタンプを生成日時にする」(/now) と同じ意味。
    /// </summary>
    public bool SetTimeStampToNow { get; set; }

    /// <summary>
    /// CRC32 が一致しなくても処理を継続するか。
    /// 既定は false（破損検出時はエラーで終了する）。
    /// </summary>
    public bool ContinueOnHashMismatch { get; set; }
}

/// <summary>
/// ヘッドレス実行の結果。
/// </summary>
public sealed class HeadlessResult
{
    /// <summary>処理が成功したか</summary>
    public bool Succeeded { get; set; }

    /// <summary>終了コード（<see cref="Cli.AtcExitCode"/>）</summary>
    public int ExitCode { get; set; } = Cli.AtcExitCode.GeneralError;

    /// <summary>暗号化・復号エンジンが返したリターンコード（診断用）</summary>
    public int EngineReturnCode { get; set; }

    /// <summary>エラーメッセージ（成功時は空文字）</summary>
    public string ErrorMessage { get; set; } = "";

    /// <summary>エラーが発生したファイルパス（不明な場合は空文字）</summary>
    public string ErrorFilePath { get; set; } = "";

    /// <summary>出力されたファイル／ディレクトリのパス</summary>
    public IList<string> OutputPaths { get; } = new List<string>();

    /// <summary>処理時間の文字列表現（エンジンが返す形式）</summary>
    public string ElapsedTimeString { get; set; } = "";

    /// <summary>暗号化ファイルのデータバージョン（復号時のみ有効。0 は不明）</summary>
    public int DataFileVersion { get; set; }
}
