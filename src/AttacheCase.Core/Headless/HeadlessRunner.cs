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
// GUI を介さずに暗号化・復号を実行する。
//
// Windows 版 AttacheCase\Headless\HeadlessRunner.cs の移植。
// 入力検証の順序・終了コードの写像は Windows 版の公開契約に合わせている
// （dogfooding 回帰スイートが番人）。内部は BackgroundWorker 駆動の
// エンジン群ではなく、Core の async サービス（EncryptionService /
// DecryptionService / ZipPassword*Service）を同期的に駆動する。
// コンソールには SynchronizationContext が無いため、ここで同期待ちしても
// デッドロックしない。
//
// Windows 版との既知の差（暫定。解消フェーズをコメントに記す）:
//   - v105 / v130（AC2 / AC3）の復号は未接続 → Phase 3
//   - 自己実行形式（/exeout）は未接続 → Phase 6
//----------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Cli;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.Headless;

/// <summary>
/// GUI を介さずに暗号化・復号を実行する。
/// </summary>
public sealed class HeadlessRunner
{
    //======================================================================
    // 上書き確認の応答値（FileDecrypt* 内の定数と一致させること）
    //======================================================================
    private const int LegacyOverwriteAll = 2;
    private const int LegacySkipAll = 6;

    private readonly Action<HeadlessProgress>? _onProgress;
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>
    /// 旧形式エンジン（FileDecrypt2/3）の駆動用シム。
    /// RunWorkerAsync を呼ばない BackgroundWorker の ReportProgress は
    /// ProgressChanged を呼び出しスレッド上で同期的に発火するため、
    /// コンソールでもスレッド切り替えなしに進捗を受け取れる（Windows 版と同じ手法）。
    /// </summary>
    private readonly BackgroundWorker _legacyWorker;

    /// <param name="onProgress">進捗通知のコールバック（不要なら null）</param>
    public HeadlessRunner(Action<HeadlessProgress>? onProgress = null)
    {
        _onProgress = onProgress;

        _legacyWorker = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true,
        };
        _legacyWorker.ProgressChanged += OnLegacyEngineProgressChanged;
    }

    /// <summary>
    /// 実行中の処理へ中断を要求する（Ctrl+C など）。
    /// </summary>
    public void Cancel()
    {
        _cancellation.Cancel();
        _legacyWorker.CancelAsync();
    }

    //======================================================================
    // 暗号化
    //======================================================================

    /// <summary>
    /// 指定されたファイル／フォルダーを暗号化する。
    /// </summary>
    public HeadlessResult Encrypt(HeadlessEncryptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new HeadlessResult();

        //-----------------------------------
        // 入力の検証（順序は Windows 版と同じ）
        //-----------------------------------
        if (request.InputPaths == null || request.InputPaths.Count == 0)
        {
            return Fail(result, AtcExitCode.UsageError, "No input file or folder is specified.");
        }

        foreach (var path in request.InputPaths)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return Fail(result, AtcExitCode.FileNotFound, "Input not found: " + path, path);
            }
        }

        if (string.IsNullOrEmpty(request.OutputFilePath))
        {
            return Fail(result, AtcExitCode.UsageError, "No output file path is specified.");
        }

        var usePublicKey = !string.IsNullOrEmpty(request.RsaPublicKeyFilePath)
                           || !string.IsNullOrEmpty(request.PostPpapRecipientEmail);

        if (!usePublicKey &&
            string.IsNullOrEmpty(request.Password) &&
            (request.PasswordBinary == null || request.PasswordBinary.Length == 0))
        {
            return Fail(result, AtcExitCode.UsageError, "No password is specified.");
        }

        //-----------------------------------
        // 出力先の確認
        //-----------------------------------
        // 不正な文字や長すぎるパスを渡されると Path.GetDirectoryName は例外を投げる。
        // 他の検証はすべて Fail で終了コードを返す設計なので、ここも合わせる。
        string? outDir;
        try
        {
            outDir = Path.GetDirectoryName(request.OutputFilePath);
        }
        catch (Exception ex)
        {
            return Fail(result, ExitCodeFromException(ex), ex.Message, request.OutputFilePath);
        }

        if (string.IsNullOrEmpty(outDir) || !Directory.Exists(outDir))
        {
            return Fail(result, AtcExitCode.FileNotFound, "Output directory not found: " + outDir, outDir ?? "");
        }

        if (File.Exists(request.OutputFilePath) && !request.Overwrite)
        {
            return Fail(result, AtcExitCode.OutputExists,
                "Output file already exists: " + request.OutputFilePath, request.OutputFilePath);
        }

        //-----------------------------------
        // パスワード付き ZIP は別系統のエンジンへ
        //-----------------------------------
        if (request.ZipEncryption)
        {
            return EncryptToZip(request, result);
        }

        //-----------------------------------
        // 自己実行形式（Windows 限定機能）
        //-----------------------------------
        if (request.SelfExecutable && !OperatingSystem.IsWindows())
        {
            return Fail(result, AtcExitCode.UsageError,
                "The self-executable (.exe) format can be created only on Windows.");
        }

        //-----------------------------------
        // 公開鍵方式の宛先を設定する
        //-----------------------------------
        var options = new EncryptionOptions
        {
            CreateExecutable = request.SelfExecutable,
            KeepTimeStamp = request.KeepTimeStamp,
            CompressionLevel = request.Compression,
            MissTypeLimits = NormalizeMissTypeLimits(request.MissTypeLimits),
            BreakFileWhenLimitExceeded = request.BreakFileWhenLimitExceeded,
            ArchiveName = Path.GetFileNameWithoutExtension(request.OutputFilePath),
            PasswordBinary = (request.PasswordBinary is { Length: > 0 }) ? request.PasswordBinary : null,
        };

        if (usePublicKey)
        {
            var keyError = AddRecipients(options, request);
            if (keyError != null)
            {
                return Fail(result, keyError.Item1, keyError.Item2);
            }
        }

        try
        {
            var service = new EncryptionService();
            var engineResult = service.EncryptAsync(
                request.InputPaths.ToArray(),
                request.OutputFilePath!,
                request.Password ?? "",
                options,
                CreateProgressAdapter(),
                _cancellation.Token).GetAwaiter().GetResult();

            return MapEngineResult(result, engineResult,
                fallbackMessage: "Encryption failed.",
                successOutputPath: string.IsNullOrEmpty(engineResult.OutputFilePath)
                    ? request.OutputFilePath!
                    : engineResult.OutputFilePath);
        }
        catch (OperationCanceledException)
        {
            return Fail(result, AtcExitCode.Canceled, "Canceled.");
        }
        catch (Exception ex)
        {
            return Fail(result, ExitCodeFromException(ex), ex.Message);
        }
    }

    //======================================================================
    // 復号
    //======================================================================

    /// <summary>
    /// 暗号化ファイルを復号する。データバージョンに応じて復号処理を選択する。
    /// </summary>
    public HeadlessResult Decrypt(HeadlessDecryptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new HeadlessResult();

        //-----------------------------------
        // 入力の検証
        //-----------------------------------
        if (string.IsNullOrEmpty(request.InputFilePath) || !File.Exists(request.InputFilePath))
        {
            return Fail(result, AtcExitCode.FileNotFound,
                "Encrypted file not found: " + request.InputFilePath, request.InputFilePath ?? "");
        }

        if (string.IsNullOrEmpty(request.OutputDirectory) || !Directory.Exists(request.OutputDirectory))
        {
            return Fail(result, AtcExitCode.FileNotFound,
                "Output directory not found: " + request.OutputDirectory, request.OutputDirectory ?? "");
        }

        // 公開鍵方式のファイルはパスワードを使わないため、ここでは必須にしない。
        // 方式はヘッダを読むまで分からないので、判定後にあらためて確認する。
        var hasPrivateKeySource = !string.IsNullOrEmpty(request.RsaPrivateKeyFilePath)
                                  || request.UsePostPpapAccounts;
        var hasPassword = !string.IsNullOrEmpty(request.Password)
                          || (request.PasswordBinary is { Length: > 0 });

        if (!hasPassword && !hasPrivateKeySource)
        {
            return Fail(result, AtcExitCode.UsageError, "No password is specified.");
        }

        //-----------------------------------
        // ファイル種別の判定
        //   パスワード付き ZIP は独自形式とヘッダがまったく違うので先に分岐する。
        //-----------------------------------
        var detector = new FileTypeDetectorService();
        int fileType;
        try
        {
            // CheckFileType は例外を握り潰して 0 を返すため、アクセス拒否などを
            // 終了コードへ写像できるよう、先に自前で開いて読めることを確かめる
            using (var fs = new FileStream(
                       request.InputFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var probeBuf = new byte[1];
                _ = fs.Read(probeBuf, 0, fs.Length > 0 ? 1 : 0);
            }

            fileType = detector.CheckFileType(request.InputFilePath);
        }
        catch (Exception ex)
        {
            return Fail(result, ExitCodeFromException(ex), ex.Message, request.InputFilePath);
        }

        if (fileType == 3) // ZIP
        {
            return DecryptZip(request, result);
        }

        if (detector.IsBroken)
        {
            return Fail(result, AtcExitCode.BrokenData,
                "The encrypted file is broken: " + request.InputFilePath, request.InputFilePath);
        }

        //-----------------------------------
        // データバージョンの判定と分岐
        //   種別判定（CheckFileType）は走査範囲に上限があるため、
        //   最終的な「AttacheCase のファイルかどうか」はバージョン検出を正とする
        //-----------------------------------
        int dataFileVersion;
        try
        {
            dataFileVersion = DecryptionService
                .DetectDataFileVersionAsync(request.InputFilePath, _cancellation.Token)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return Fail(result, AtcExitCode.Canceled, "Canceled.");
        }
        catch (Exception ex)
        {
            return Fail(result, ExitCodeFromException(ex), ex.Message, request.InputFilePath);
        }

        result.DataFileVersion = dataFileVersion;

        if (dataFileVersion <= 0)
        {
            // トークンが見つからない: AttacheCase のファイルではない
            //（種別判定が ATC 系（1/2/4）と言っているのに読めないなら破損）
            return Fail(result,
                fileType is 1 or 2 or 4 ? AtcExitCode.BrokenData : AtcExitCode.UnsupportedFormat,
                "Not an AttacheCase encrypted file: " + request.InputFilePath, request.InputFilePath);
        }

        //-----------------------------------
        // v105 / v130（AttacheCase 2 / 3）は Windows 版から移植したエンジンで復号する
        //-----------------------------------
        if (dataFileVersion < 140)
        {
            return DecryptLegacy(request, result, dataFileVersion);
        }

        if (dataFileVersion >= 160)
        {
            return Fail(result, AtcExitCode.UnsupportedFormat,
                "Unsupported data file version: " + dataFileVersion, request.InputFilePath);
        }

        //-----------------------------------
        // v140 / v150 の復号（DecryptionService が内部で読み分ける）
        //-----------------------------------
        var options = new DecryptionOptions
        {
            NoParentFolder = request.NoParentFolder,
            RestoreTimeStamp = !request.SetTimeStampToNow,
            PasswordBinary = (request.PasswordBinary is { Length: > 0 }) ? request.PasswordBinary : null,
            OverwriteOption = request.Overwrite ? OverwriteOption.OverwriteAll : OverwriteOption.SkipAll,
            HashMismatchOption = request.ContinueOnHashMismatch
                ? HashMismatchOption.Continue
                : HashMismatchOption.Ask,
            // DecryptionService が実際に見るのはこちら（GUI の DecryptionViewModel と同じ）。
            // HashMismatchOption だけ立てても CRC 不一致で止まってしまう
            SalvageIgnoreHashCheck = request.ContinueOnHashMismatch,
        };

        if (!string.IsNullOrEmpty(request.RsaPrivateKeyFilePath))
        {
            if (!File.Exists(request.RsaPrivateKeyFilePath))
            {
                return Fail(result, AtcExitCode.FileNotFound,
                    "Private key file not found: " + request.RsaPrivateKeyFilePath,
                    request.RsaPrivateKeyFilePath);
            }

            try
            {
                options.IsUseRsaEncryption = true;
                options.RsaPrivateKeyXml = File.ReadAllText(
                    request.RsaPrivateKeyFilePath, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return Fail(result, AtcExitCode.UsageError,
                    "Could not read the private key file: " + ex.Message, request.RsaPrivateKeyFilePath);
            }
        }

        try
        {
            var service = new DecryptionService();
            var engineResult = service.DecryptAsync(
                request.InputFilePath,
                request.OutputDirectory!,
                request.Password ?? "",
                options,
                CreateProgressAdapter(),
                _cancellation.Token).GetAwaiter().GetResult();

            return MapEngineResult(result, engineResult,
                fallbackMessage: "Decryption failed.",
                successOutputPath: request.OutputDirectory!);
        }
        catch (OperationCanceledException)
        {
            return Fail(result, AtcExitCode.Canceled, "Canceled.");
        }
        catch (Exception ex)
        {
            return Fail(result, ExitCodeFromException(ex), ex.Message, request.InputFilePath);
        }
    }

    //======================================================================
    // 旧形式（v105 / v130）の復号
    //======================================================================

    /// <summary>
    /// AttacheCase 2（v105）／ 3（v130）形式を、Windows 版から移植した
    /// FileDecrypt2 / FileDecrypt3 で復号する。挙動保存を最優先し、
    /// エンジンは BackgroundWorker シムで駆動する（Windows 版と同じ）。
    /// </summary>
    private HeadlessResult DecryptLegacy(
        HeadlessDecryptRequest request, HeadlessResult result, int dataFileVersion)
    {
        var overwriteOption = request.Overwrite ? LegacyOverwriteAll : LegacySkipAll;

        // 非対話実行なので、確認ダイアログの応答はあらかじめ確定させておく。
        // 応答が確定していればエンジンはコールバックの結果を待たない
        static void NoInteractiveDialog(int type, string path, IReadOnlyList<string> hashes) { }
        static void NoInteractiveInvalidChar(string path) { }

        try
        {
            var doWorkArgs = new DoWorkEventArgs(null);
            bool succeeded;
            int engineReturnCode;
            string errorMessage;
            string errorFilePath;
            string elapsed;

            if (dataFileVersion < 130)
            {
                var decryption = new FileDecrypt2(request.InputFilePath!)
                {
                    TempOverWriteOption = overwriteOption,
                    NumberOfFiles = 1,
                    TotalNumberOfFiles = 1,
                    fNoParentFolder = request.NoParentFolder,
                    fSameTimeStamp = request.SetTimeStampToNow,
                };

                succeeded = decryption.Decrypt(
                    _legacyWorker, doWorkArgs,
                    request.InputFilePath!, request.OutputDirectory!,
                    request.Password ?? "", request.PasswordBinary,
                    NoInteractiveDialog, NoInteractiveInvalidChar);

                engineReturnCode = decryption.ReturnCode;
                errorMessage = decryption.ErrorMessage;
                errorFilePath = decryption.ErrorFilePath;
                elapsed = decryption.DecryptionTimeString;
            }
            else
            {
                var decryption = new FileDecrypt3(request.InputFilePath!)
                {
                    TempOverWriteOption = overwriteOption,
                    NumberOfFiles = 1,
                    TotalNumberOfFiles = 1,
                    fNoParentFolder = request.NoParentFolder,
                    fSameTimeStamp = request.SetTimeStampToNow,
                };

                succeeded = decryption.Decrypt(
                    _legacyWorker, doWorkArgs,
                    request.InputFilePath!, request.OutputDirectory!,
                    request.Password ?? "", request.PasswordBinary,
                    NoInteractiveDialog);

                engineReturnCode = decryption.ReturnCode;
                errorMessage = decryption.ErrorMessage;
                errorFilePath = decryption.ErrorFilePath;
                elapsed = decryption.DecryptionTimeString;
            }

            result.EngineReturnCode = engineReturnCode;
            result.ElapsedTimeString = elapsed ?? "";

            if (doWorkArgs.Cancel)
            {
                return Fail(result, AtcExitCode.Canceled, "Canceled.");
            }

            if (!succeeded)
            {
                return Fail(result,
                    AtcExitCode.FromEngineReturnCode(engineReturnCode),
                    string.IsNullOrEmpty(errorMessage)
                        ? "Decryption failed. (code " + engineReturnCode + ")"
                        : errorMessage,
                    errorFilePath);
            }

            result.Succeeded = true;
            result.ExitCode = AtcExitCode.Success;
            result.OutputPaths.Add(request.OutputDirectory!);
            return result;
        }
        catch (Exception ex)
        {
            return Fail(result, ExitCodeFromException(ex), ex.Message, request.InputFilePath ?? "");
        }
    }

    /// <summary>
    /// 旧形式エンジンからの進捗通知（ArrayList { コード, メッセージ }）を
    /// <see cref="HeadlessProgress"/> へ翻訳する。Windows 版と同じ写像。
    /// </summary>
    private void OnLegacyEngineProgressChanged(object? sender, ProgressChangedEventArgs e)
    {
        if (_onProgress == null) return;

        var status = 0;
        var message = "";
        if (e.UserState is ArrayList { Count: > 0 } list)
        {
            if (list[0] is int code) status = code;
            if (list.Count > 1 && list[1] is string text) message = text;
        }

        var phase = status switch
        {
            ErrorCodes.ENCRYPTING => HeadlessPhase.Encrypting,
            ErrorCodes.DECRYPTING => HeadlessPhase.Decrypting,
            ErrorCodes.DELETING => HeadlessPhase.Deleting,
            ErrorCodes.ENCRYPT_SUCCEEDED or ErrorCodes.DECRYPT_SUCCEEDED or ErrorCodes.DELETE_SUCCEEDED
                => HeadlessPhase.Completed,
            _ => HeadlessPhase.Preparing,
        };

        // エンジンは 0〜10000 の整数で進捗を渡す（-1 はファイル走査中で総量不明）
        var percent = e.ProgressPercentage < 0 ? -1.0 : e.ProgressPercentage / 100.0;

        _onProgress(new HeadlessProgress
        {
            Phase = phase,
            Percent = percent,
            CurrentItem = message,
        });
    }

    //======================================================================
    // 公開鍵方式の宛先設定
    //======================================================================

    /// <summary>
    /// 暗号化オプションへ公開鍵方式の宛先を登録する。
    /// </summary>
    /// <returns>失敗したら (終了コード, メッセージ)。成功したら null</returns>
    private Tuple<int, string>? AddRecipients(EncryptionOptions options, HeadlessEncryptRequest request)
    {
        //-----------------------------------
        // 従来の RSA（公開鍵ファイルを直接指定）
        //   5.0 で一度畳んだが 5.5 で復活（Core の Crypto/RsaKeyFile 参照）。
        //   鍵ファイルの不備は使い方の誤りとして 2（UsageError）で返す
        //-----------------------------------
        if (!string.IsNullOrEmpty(request.RsaPublicKeyFilePath))
        {
            if (!File.Exists(request.RsaPublicKeyFilePath))
            {
                return Tuple.Create(AtcExitCode.FileNotFound,
                    "Public key file not found: " + request.RsaPublicKeyFilePath);
            }

            try
            {
                var xml = File.ReadAllText(request.RsaPublicKeyFilePath, System.Text.Encoding.UTF8);
                options.Recipients.Add(AttacheCase.Crypto.RsaKeyFile.CreateRecipient(xml));
            }
            catch (AttacheCase.Crypto.RsaKeyFile.UnsupportedKeySizeException ex)
            {
                return Tuple.Create(AtcExitCode.UsageError,
                    "Unsupported RSA key size: " + ex.Bits + " bits (2048 or 4096 only): " +
                    request.RsaPublicKeyFilePath);
            }
            catch (Exception ex)
            {
                return Tuple.Create(AtcExitCode.UsageError,
                    "Could not read the public key file: " + ex.Message);
            }
        }

        //-----------------------------------
        // Post-PPAP（キーサーバから宛先の公開鍵を検索する）
        //-----------------------------------
        if (!string.IsNullOrEmpty(request.PostPpapRecipientEmail))
        {
            PostPpapKeyInfo? recipient;
            try
            {
                // コンソールアプリには SynchronizationContext が無いため、
                // ここで同期的に待ってもデッドロックしない。
                recipient = new PostPpapManager()
                    .LookupPublicKeyAsync(request.PostPpapRecipientEmail, _cancellation.Token)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return Tuple.Create(AtcExitCode.GeneralError,
                    "Post-PPAP key lookup failed: " + ex.Message);
            }

            if (recipient == null)
            {
                return Tuple.Create(AtcExitCode.FileNotFound,
                    "Post-PPAP: public key not found for " + request.PostPpapRecipientEmail);
            }

            if (string.IsNullOrEmpty(recipient.MlKemPublicKey))
            {
                // RSA 単体の古いアカウント。ver.5 では RSA 単体の新規暗号化を
                // 提供しないので、相手にハイブリッド鍵での再登録を促すのが正しい対応。
                return Tuple.Create(AtcExitCode.UsageError,
                    "The recipient's key is RSA-only, which ver.5 no longer supports for new files. " +
                    "Ask the recipient to re-register with a hybrid (RSA + ML-KEM) key.");
            }

            options.Recipients.Add(recipient);
        }

        return null;
    }

    //======================================================================
    // パスワード付き ZIP
    //======================================================================

    /// <summary>パスワード付き ZIP を作る。</summary>
    private HeadlessResult EncryptToZip(HeadlessEncryptRequest request, HeadlessResult result)
    {
        // 併用できない指定を、ZIP を作り始める前に断る。
        // ここを通さないと、要求された形式・鍵方式を黙って無視した ZIP を
        // 「成功」として返してしまう。
        if (request.SelfExecutable)
        {
            return Fail(result, AtcExitCode.UsageError,
                "The self-executable format cannot be combined with the password ZIP format.");
        }

        if (!string.IsNullOrEmpty(request.RsaPublicKeyFilePath))
        {
            return Fail(result, AtcExitCode.UsageError,
                "RSA public key encryption cannot be combined with the password ZIP format.");
        }

        if (!string.IsNullOrEmpty(request.PostPpapRecipientEmail))
        {
            return Fail(result, AtcExitCode.UsageError,
                "Post-PPAP encryption cannot be combined with the password ZIP format.");
        }

        // ZIP の暗号方式は 0 / 1 / 2 のみ。範囲外を黙って 0（ZipCrypto）へ倒すと、
        // 呼び出し側が意図したより弱い方式で暗号化してしまう。
        if (request.ZipAlgorithm < 0 || request.ZipAlgorithm > 2)
        {
            return Fail(result, AtcExitCode.UsageError,
                "Invalid ZIP encryption algorithm: " + request.ZipAlgorithm +
                " (0 = ZipCrypto, 1 = WinZipAes128, 2 = WinZipAes256).");
        }

        // パスワードファイルは ZIP では成立しない。受け取る側はエクスプローラーに
        // パスワードを打ち込んで開くので、ファイルから導いた鍵は再現できない。
        if (request.PasswordBinary is { Length: > 0 })
        {
            return Fail(result, AtcExitCode.PasswordFileNotSupported,
                "A password file cannot be used with the password ZIP format.");
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return Fail(result, AtcExitCode.UsageError,
                "A text password is required for the password ZIP format.");
        }

        try
        {
            // 公開契約の 0/1/2 と enum 値（0/128/256）は別物なので明示的に写像する
            var method = request.ZipAlgorithm switch
            {
                1 => ZipPasswordEncryptionMethod.Aes128,
                2 => ZipPasswordEncryptionMethod.Aes256,
                _ => ZipPasswordEncryptionMethod.ZipCrypto,
            };
            var service = new ZipPasswordEncryptionService(method);
            var options = new EncryptionOptions
            {
                KeepTimeStamp = request.KeepTimeStamp,
                CompressionLevel = request.Compression,
                // ZIP は .atc と違い、書庫名のフォルダーで包まない（Windows 版と同じ）。
                // 指定するとエントリに余計な階層が入り、旧版と構造が食い違う
                ArchiveName = string.Empty,
            };

            var engineResult = service.EncryptAsync(
                request.InputPaths!.ToArray(),
                request.OutputFilePath!,
                request.Password!,
                options,
                CreateProgressAdapter(),
                _cancellation.Token).GetAwaiter().GetResult();

            return MapEngineResult(result, engineResult,
                fallbackMessage: "ZIP encryption failed.",
                successOutputPath: string.IsNullOrEmpty(engineResult.OutputFilePath)
                    ? request.OutputFilePath!
                    : engineResult.OutputFilePath);
        }
        catch (OperationCanceledException)
        {
            return Fail(result, AtcExitCode.Canceled, "Canceled.");
        }
        catch (Exception ex)
        {
            return Fail(result, ExitCodeFromException(ex), ex.Message);
        }
    }

    /// <summary>パスワード付き ZIP を展開する。</summary>
    private HeadlessResult DecryptZip(HeadlessDecryptRequest request, HeadlessResult result)
    {
        // 暗号化側（EncryptToZip）と同じ検査を、テキストパスワードの検査より先に行う。
        // ここを通さないと、テキストパスワードを併せて指定したときに
        // パスワードファイルの指定を黙って無視してしまい、
        // 単独指定のときも PasswordFileNotSupported ではなく UsageError になって、
        // 公開している終了コードの契約と食い違う。
        if (request.PasswordBinary is { Length: > 0 })
        {
            return Fail(result, AtcExitCode.PasswordFileNotSupported,
                "A password file cannot be used with the password ZIP format.");
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return Fail(result, AtcExitCode.UsageError,
                "A text password is required to open a password ZIP file.");
        }

        try
        {
            var service = new ZipPasswordDecryptionService();
            var options = new DecryptionOptions
            {
                NoParentFolder = request.NoParentFolder,
                RestoreTimeStamp = !request.SetTimeStampToNow,
                OverwriteOption = request.Overwrite ? OverwriteOption.OverwriteAll : OverwriteOption.SkipAll,
            };

            var engineResult = service.DecryptAsync(
                request.InputFilePath!,
                request.OutputDirectory!,
                request.Password!,
                options,
                CreateProgressAdapter(),
                _cancellation.Token).GetAwaiter().GetResult();

            return MapEngineResult(result, engineResult,
                fallbackMessage: "ZIP decryption failed.",
                successOutputPath: request.OutputDirectory!);
        }
        catch (OperationCanceledException)
        {
            return Fail(result, AtcExitCode.Canceled, "Canceled.");
        }
        catch (Exception ex)
        {
            return Fail(result, ExitCodeFromException(ex), ex.Message);
        }
    }

    //======================================================================
    // 内部処理
    //======================================================================

    /// <summary>
    /// エンジン（Core サービス）の結果をヘッドレス実行の結果へ写像する。
    /// </summary>
    private static HeadlessResult MapEngineResult(
        HeadlessResult result, EncryptionResult engineResult,
        string fallbackMessage, string successOutputPath)
    {
        result.EngineReturnCode = engineResult.ErrorCode;
        result.ElapsedTimeString = engineResult.ProcessingTime ?? "";

        if (engineResult.WasCancelled)
        {
            return Fail(result, AtcExitCode.Canceled, "Canceled.");
        }

        if (!engineResult.Success)
        {
            return Fail(result,
                AtcExitCode.FromEngineReturnCode(engineResult.ErrorCode),
                string.IsNullOrEmpty(engineResult.ErrorMessage)
                    ? fallbackMessage + " (code " + engineResult.ErrorCode + ")"
                    : engineResult.ErrorMessage,
                engineResult.ErrorFilePath);
        }

        result.Succeeded = true;
        result.ExitCode = AtcExitCode.Success;
        result.OutputPaths.Add(successOutputPath);
        return result;
    }

    private static int NormalizeMissTypeLimits(int value)
    {
        return (0 < value && value < 11) ? value : 3;
    }

    private static HeadlessResult Fail(
        HeadlessResult result, int exitCode, string message, string filePath = "")
    {
        result.Succeeded = false;
        result.ExitCode = exitCode;
        result.ErrorMessage = message ?? "";
        result.ErrorFilePath = filePath ?? "";
        return result;
    }

    private static int ExitCodeFromException(Exception ex)
    {
        switch (ex)
        {
            case UnauthorizedAccessException:
                return AtcExitCode.AccessDenied;
            case FileNotFoundException:
            case DirectoryNotFoundException:
            case DriveNotFoundException:
            case PathTooLongException:
                return AtcExitCode.FileNotFound;
            case System.Security.Cryptography.CryptographicException:
                return AtcExitCode.DecryptionFailed;
            case IOException:
                return AtcExitCode.GeneralError;
            default:
                return AtcExitCode.InternalError;
        }
    }

    /// <summary>
    /// Core サービスの進捗通知を <see cref="HeadlessProgress"/> へ翻訳するアダプタを作る。
    /// Progress&lt;T&gt; は SynchronizationContext 経由で遅延配送されるため、
    /// コンソールでは順序が保たれる直接呼び出しのアダプタを使う。
    /// </summary>
    private IProgress<EncryptionProgress>? CreateProgressAdapter()
    {
        if (_onProgress == null) return null;
        return new DirectProgress(this);
    }

    private sealed class DirectProgress(HeadlessRunner owner) : IProgress<EncryptionProgress>
    {
        public void Report(EncryptionProgress value)
        {
            var phase = value.Type switch
            {
                ProcessType.Encrypting => HeadlessPhase.Encrypting,
                ProcessType.Decrypting => HeadlessPhase.Decrypting,
                ProcessType.Deleting => HeadlessPhase.Deleting,
                _ => HeadlessPhase.Preparing,
            };

            owner._onProgress!(new HeadlessProgress
            {
                Phase = phase,
                Percent = value.ProgressPercentage < 0 ? -1.0 : value.ProgressPercentage,
                CurrentItem = string.IsNullOrEmpty(value.CurrentFilePath)
                    ? value.StatusMessage
                    : value.CurrentFilePath,
            });
        }
    }
}
