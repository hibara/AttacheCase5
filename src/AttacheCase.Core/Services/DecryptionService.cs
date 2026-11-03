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
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using AttacheCase.Crypto;
using AttacheCase.Models;
using Org.BouncyCastle.Crypto;

namespace AttacheCase.Services;

public class DecryptionService : IDecryptionService
{
    /// <summary>表示文字列（利用者に見えるメッセージは必ずここを通す）</summary>
    private static readonly LocalizationService Localize = LocalizationService.Instance;

  private const int PLAIN_HEADER_SIZE = 52;

  private const int BUFFER_SIZE = 1024 * 1024; // 1MB

  private const string STRING_TOKEN_NORMAL = "_AttacheCaseData";
  private const string STRING_TOKEN_BROKEN = "_Atc_Broken_Data";
  private const string STRING_TOKEN_RSA = "_AttacheCase_Rsa";

  private const int DATA_FILE_VERSION = 140; // ver.4
  private const int DATA_FILE_VERSION_V5 = 150; // ver.5
  private const string ATC_ENCRYPTED_TOKEN = "atc4";

  public async Task<EncryptionResult> DecryptAsync(
      string inputPath,
      string outputDirectory,
      string password,
      DecryptionOptions options,
      IProgress<EncryptionProgress>? progress = null,
      CancellationToken cancellationToken = default)
  {
    var sw = Stopwatch.StartNew();

    try
    {
        if (!File.Exists(inputPath))
            return EncryptionResult.CreateError(
                ErrorCodes.FILE_NOT_FOUND,
                Localize["Error_FileNotFound"],
                inputPath);

        // パスワードの入力ミスが上限を超えて破壊されたファイル
        if (IsMarkedBroken(inputPath))
            return EncryptionResult.CreateError(
                ErrorCodes.ATC_BROKEN_DATA,
                Localize["Error_FileAlreadyBroken"],
                inputPath);

        // バージョン検出: v150 (AC5) なら専用パスへ分岐、v140 (AC4) は従来パスで処理
        var detectedVersion = await DetectDataFileVersionAsync(inputPath, cancellationToken);

        // v105 / v130（AttacheCase 2 / 3）は Windows 版から移植したエンジンで復号する
        if (detectedVersion > 0 && detectedVersion < DATA_FILE_VERSION)
        {
            Directory.CreateDirectory(outputDirectory);
            var legacyResult = await Task.Run(() => DecryptLegacyFormat(
                inputPath, outputDirectory, password, options, detectedVersion,
                progress, cancellationToken), cancellationToken);
            sw.Stop();
            legacyResult.ProcessingTime =
                $"{sw.Elapsed.Hours}h{sw.Elapsed.Minutes}m{sw.Elapsed.Seconds}s{sw.Elapsed.Milliseconds}ms";
            return legacyResult;
        }

        if (detectedVersion >= DATA_FILE_VERSION_V5)
        {
            Directory.CreateDirectory(outputDirectory);
            // 重い同期処理（GCM Drain/Verify, Deflate 解凍）が UI スレッドを掴まないように
            // バックグラウンドスレッドへオフロード。Progress<T> は VM 側で UI スレッドに戻る。
            var v5Result = await Task.Run(() => DecryptV5Async(
                inputPath, outputDirectory, password, options,
                progress, cancellationToken), cancellationToken);
            sw.Stop();
            v5Result.ProcessingTime =
                $"{sw.Elapsed.Hours}h{sw.Elapsed.Minutes}m{sw.Elapsed.Seconds}s{sw.Elapsed.Milliseconds}ms";
            return v5Result;
        }

        byte[] passwordBinary;

        if (options.IsUseRsaEncryption)
        {
            // 秘密鍵からGUIDを取得
            var xmlElement = XElement.Parse(options.RsaPrivateKeyXml);
            var privateKeyGuidString = xmlElement.Element("id")?.Value;
            if (string.IsNullOrEmpty(privateKeyGuidString))
            {
                return EncryptionResult.CreateError(
                    ErrorCodes.RSA_KEY_GUID_NOT_MATCH,
                    Localize["Error_PrivateKeyNoGuid"]);
            }
            var privateKeyGuid = Guid.Parse(privateKeyGuidString);

            // 暗号化ファイルからGUIDとRSAブロックを読み取る
            await using var fsRsa = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // ヘッダー位置を確認
            if (!TryReadPlainHeaderAt(fsRsa, 0, out var rsaHeader))
            {
                if (!TryFindPlainHeader(fsRsa, out rsaHeader))
                {
                    return EncryptionResult.CreateError(
                        ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
                        Localize["Error_RsaHeaderNotFound"]);
                }
            }

            // GUIDの比較（ヘッダーから読み取ったGUIDと秘密鍵のGUID）
            var fileGuid = new Guid(rsaHeader.Guid);
            if (fileGuid != privateKeyGuid)
            {
                return EncryptionResult.CreateError(
                    ErrorCodes.RSA_KEY_GUID_NOT_MATCH,
                    Localize.GetString("Error_RsaGuidMismatchDetail", fileGuid, privateKeyGuid));
            }

            // RSAブロックがあるか確認
            if (rsaHeader.RsaBlock == null || rsaHeader.RsaBlock.Length != 256)
            {
                return EncryptionResult.CreateError(
                    ErrorCodes.RSA_KEY_GUID_NOT_MATCH,
                    Localize["Error_RsaBlockNotFound"]);
            }

            // RSAで復号してパスワードを取得
            // RSACryptoServiceProvider は Windows 専用実装。macOS では RSA.Create() を使う
            using var rsa = RSA.Create();
            rsa.FromXmlString(options.RsaPrivateKeyXml);

            try
            {
                var rsaPasswordData = rsa.Decrypt(rsaHeader.RsaBlock, RSAEncryptionPadding.OaepSHA1);
                passwordBinary = rsaPasswordData;
            }
            catch (CryptographicException)
            {
                return EncryptionResult.CreateError(
                    ErrorCodes.CRYPTOGRAPHIC_EXCEPTION,
                    Localize["Error_RsaDecryptFailed"]);
            }
        }
        else
        {
            // パスワードファイル（/dpfile）が指定されているときは、その鍵素材を優先する
            passwordBinary = options.PasswordBinary ?? Encoding.UTF8.GetBytes(password);
        }

        Directory.CreateDirectory(outputDirectory);

        var result = await DecryptFileAsync(
            inputPath,
            outputDirectory,
            passwordBinary,
            options,
            progress,
            cancellationToken).ConfigureAwait(false);

        sw.Stop();
        result.ProcessingTime =
            $"{sw.Elapsed.Hours}h{sw.Elapsed.Minutes}m{sw.Elapsed.Seconds}s{sw.Elapsed.Milliseconds}ms";

        return result;
    }
    catch (OperationCanceledException ex)
    {
        return WithLeftovers(EncryptionResult.CreateCancelled(), ex);
    }
    catch (CryptographicException ex)
    {
        return WithLeftovers(EncryptionResult.CreateError(
            ErrorCodes.CRYPTOGRAPHIC_EXCEPTION,
            Localize["Error_DecryptFailedPassword"]), ex);
    }
    catch (Exception ex)
    {
        return WithLeftovers(EncryptionResult.CreateError(
            ErrorCodes.ERROR_UNEXPECTED,
            ex.Message), ex);
    }
  }

  // =====================================================================
  // v105 / v130（AttacheCase 2 / 3）復号 — Windows 版から移植したエンジンを駆動
  // =====================================================================

  /// <summary>
  /// 旧形式（v105 / v130）を FileDecrypt2 / FileDecrypt3 で復号する。
  /// エンジンは BackgroundWorker 前提の設計なので、RunWorkerAsync を呼ばない
  /// シムを渡して同期駆動する（ReportProgress は呼び出しスレッド上で発火する）。
  /// </summary>
  private static EncryptionResult DecryptLegacyFormat(
      string inputPath,
      string outputDirectory,
      string password,
      DecryptionOptions options,
      int dataFileVersion,
      IProgress<EncryptionProgress>? progress,
      CancellationToken ct)
  {
    using var worker = new System.ComponentModel.BackgroundWorker
    {
      WorkerReportsProgress = true,
      WorkerSupportsCancellation = true,
    };

    worker.ProgressChanged += (_, e) =>
    {
      if (progress == null) return;
      var message = "";
      if (e.UserState is System.Collections.ArrayList { Count: > 1 } list && list[1] is string text)
        message = text;
      progress.Report(new EncryptionProgress
      {
        Type = ProcessType.Decrypting,
        ProgressPercentage = e.ProgressPercentage < 0 ? 0 : e.ProgressPercentage,
        StatusMessage = message,
      });
    };

    using var cancelRegistration = ct.Register(worker.CancelAsync);

    // 上書きの応答（FileDecrypt2/3 内の定数: 2 = すべて上書き, 6 = すべてスキップ）。
    // GUI の既定（Ask）は、v140/v150 の既存挙動（確認なしで上書き）に合わせて上書き扱い
    var overwriteOption = options.OverwriteOption is OverwriteOption.Skip or OverwriteOption.SkipAll ? 6 : 2;

    static void NoInteractiveDialog(int type, string path, IReadOnlyList<string> hashes) { }
    static void NoInteractiveInvalidChar(string path) { }

    var passwordBinary = options.PasswordBinary;
    var doWorkArgs = new System.ComponentModel.DoWorkEventArgs(null);
    bool succeeded;
    int engineReturnCode;
    string errorMessage;
    string errorFilePath;

    if (dataFileVersion < 130)
    {
      var decryption = new FileDecrypt2(inputPath)
      {
        TempOverWriteOption = overwriteOption,
        NumberOfFiles = 1,
        TotalNumberOfFiles = 1,
        fNoParentFolder = options.NoParentFolder,
        fSameTimeStamp = !options.RestoreTimeStamp,
      };
      succeeded = decryption.Decrypt(
        worker, doWorkArgs, inputPath, outputDirectory, password, passwordBinary,
        NoInteractiveDialog, NoInteractiveInvalidChar);
      engineReturnCode = decryption.ReturnCode;
      errorMessage = decryption.ErrorMessage;
      errorFilePath = decryption.ErrorFilePath;
    }
    else
    {
      var decryption = new FileDecrypt3(inputPath)
      {
        TempOverWriteOption = overwriteOption,
        NumberOfFiles = 1,
        TotalNumberOfFiles = 1,
        fNoParentFolder = options.NoParentFolder,
        fSameTimeStamp = !options.RestoreTimeStamp,
      };
      succeeded = decryption.Decrypt(
        worker, doWorkArgs, inputPath, outputDirectory, password, passwordBinary,
        NoInteractiveDialog);
      engineReturnCode = decryption.ReturnCode;
      errorMessage = decryption.ErrorMessage;
      errorFilePath = decryption.ErrorFilePath;
    }

    if (doWorkArgs.Cancel) return EncryptionResult.CreateCancelled();

    if (!succeeded)
    {
      // 保存先の同名ファイル（上書きしない指定）はエンジンがメッセージを持たないのでここで付ける
      if (engineReturnCode == ErrorCodes.OUTPUT_FILE_EXISTS)
        return OutputFileExistsError(errorFilePath);

      return EncryptionResult.CreateError(
        engineReturnCode,
        string.IsNullOrEmpty(errorMessage)
          ? Localize["Error_DecryptFailedPassword"]
          : errorMessage,
        errorFilePath);
    }

    return EncryptionResult.CreateSuccess(outputDirectory, 0, "");
  }

  /// <summary>
  /// v4 (DATA_FILE_VERSION = 140) の鍵導出。
  /// Windows 版と同じ PBKDF2-HMAC-SHA1 / 1000 回で、先頭 32 バイトを鍵、続く 16 バイトを IV とする。
  /// </summary>
  /// <remarks>
  /// PBKDF2 の出力は連続したストリームなので、GetBytes(32) → GetBytes(16) と
  /// 48 バイトを一度に取って分割するのは同じ結果になる。
  /// </remarks>
  private static (byte[] Key, byte[] Iv) DeriveKeyAndIvV4(byte[] passwordBinary, byte[] salt)
  {
    var material = Rfc2898DeriveBytes.Pbkdf2(passwordBinary, salt, 1000, HashAlgorithmName.SHA1, 48);
    return (material[..32], material[32..]);
  }

  // =====================================================================
  // v5 (DATA_FILE_VERSION = 150) 復号
  // =====================================================================

  /// <summary>
  /// ファイル先頭部分から DataFileVersion を読み取って判定する。
  /// 自己実行形式（先頭に EXE データがある）の場合はトークン文字列を簡易スキャンする。
  /// 失敗時は 0 を返す。
  /// </summary>
  // HeadlessRunner（コマンドライン実行）もバージョン判定に使うため internal
  internal static async Task<int> DetectDataFileVersionAsync(string inputPath, CancellationToken ct)
  {
    try
    {
      await using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
      if (fs.Length < 24) return 0;

      // 先頭 24 バイトで判定（標準位置）
      var head = new byte[24];
      if (await fs.ReadAsync(head.AsMemory(0, 24), ct) < 24) return 0;
      var tokenStr = Encoding.ASCII.GetString(head, 4, 16).TrimEnd('\0');
      if (tokenStr == STRING_TOKEN_NORMAL || tokenStr == STRING_TOKEN_RSA || tokenStr == STRING_TOKEN_BROKEN)
        return BitConverter.ToInt32(head, 20);

      // 自己実行形式: トークンをスキャン
      fs.Position = 0;
      var needles = new[]
      {
        Encoding.ASCII.GetBytes(STRING_TOKEN_NORMAL),
        Encoding.ASCII.GetBytes(STRING_TOKEN_BROKEN),
        Encoding.ASCII.GetBytes(STRING_TOKEN_RSA)
      };
      const int MAX_SCAN = 64 * 1024 * 1024;
      var scanLimit = Math.Min(fs.Length, MAX_SCAN);
      var buf = ArrayPool<byte>.Shared.Rent(1024 * 1024);
      try
      {
        long pos = 0;
        const int overlap = 64;
        Array.Clear(buf, 0, overlap);
        while (pos < scanLimit)
        {
          ct.ThrowIfCancellationRequested();
          fs.Position = pos;
          var toRead = (int)Math.Min(buf.Length - overlap, scanLimit - pos);
          var r = await fs.ReadAsync(buf.AsMemory(overlap, toRead), ct);
          if (r <= 0) break;
          var windowLen = overlap + r;
          foreach (var needle in needles)
          {
            var idx = IndexOf(buf, 0, windowLen, needle);
            if (idx >= 0)
            {
              var tokenPos = pos + idx - overlap;
              var headerStart = tokenPos - 4;
              if (headerStart < 0) continue;
              fs.Position = headerStart + 20;
              var verBuf = new byte[4];
              if (await fs.ReadAsync(verBuf.AsMemory(0, 4), ct) == 4)
                return BitConverter.ToInt32(verBuf, 0);
            }
          }
          Buffer.BlockCopy(buf, windowLen - overlap, buf, 0, overlap);
          pos += r;
        }
      }
      finally
      {
        ArrayPool<byte>.Shared.Return(buf);
      }
      return 0;
    }
    catch
    {
      return 0;
    }
  }

  /// <summary>
  /// v5 (DATA_FILE_VERSION = 150) フォーマットの暗号化ファイルを復号する。
  /// パスワード方式（Argon2id + AES-256-GCM）のみ対応（Phase 1）。
  /// </summary>
  private static async Task<EncryptionResult> DecryptV5Async(
      string inputPath,
      string outputDirectory,
      string password,
      DecryptionOptions options,
      IProgress<EncryptionProgress>? progress,
      CancellationToken ct)
  {
    var outputDirFull = EnsureTrailingSeparator(Path.GetFullPath(outputDirectory));
    var swProgress = Stopwatch.StartNew();

    await using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);

    // ① 平文ヘッダの読み取り（自己実行形式に対応）
    // この経路に来た時点でトークン（_AttacheCaseData 等）の存在は確認済みなので、
    // ここで読めないのは「暗号化ファイルではない」ではなく「破損している」
    //（切り詰め・サイズ欄の改ざんなど）。終了コードの契約では 8 に写像される
    if (!TryReadV5PlainHeader(fs, out var header))
      return EncryptionResult.CreateError(
        ErrorCodes.ATC_BROKEN_DATA,
        Localize["Error_HeaderNotFound"]);

    if (header.IsBroken)
      return EncryptionResult.CreateError(
        ErrorCodes.ATC_BROKEN_DATA,
        Localize["Error_FileAlreadyBroken"]);

    if (header.SymmetricAlgorithm != 0x01)
      return EncryptionResult.CreateError(
        ErrorCodes.ERROR_UNEXPECTED,
        Localize.GetString("Error_UnsupportedSymmetricAlgorithm", $"{header.SymmetricAlgorithm:X2}"));

    // 公開鍵方式 (encMode=0x01) のときは KDF=0x00 が正、パスワード方式 (encMode=0x00) は KDF=0x01
    if (header.EncryptionMode == 0x00 && header.KdfAlgorithm != 0x01)
      return EncryptionResult.CreateError(
        ErrorCodes.ERROR_UNEXPECTED,
        Localize.GetString("Error_UnsupportedKdf", $"{header.KdfAlgorithm:X2}"));

    // ② 鍵保護ブロックを読み取り、共通鍵を復元
    byte[] commonKey;
    try
    {
      commonKey = header.EncryptionMode == 0x01
        ? RecoverCommonKeyFromPublicKeyV5(header, options.IsUseRsaEncryption ? options.RsaPrivateKeyXml : null)
        : RecoverCommonKeyFromPasswordV5(header, password, options.PasswordBinary);
    }
    catch (InvalidCipherTextException)
    {
      return EncryptionResult.CreateError(
        ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
        header.EncryptionMode == 0x01
          ? Localize["Error_PublicKeyRecoverFailed"]
          : Localize["Error_WrongPassword"]);
    }
    catch (NoMatchingPrivateKeyException ex)
    {
      return EncryptionResult.CreateError(
        ErrorCodes.PASSWORD_TOKEN_NOT_FOUND, ex.Message);
    }
    catch (InvalidDataException ex)
    {
      // 鍵保護ブロックの Argon2 パラメータ・サイズが許容範囲外
      //（改ざん・破損。認証前に読む値なので範囲検証しか守りが無い）
      return EncryptionResult.CreateError(ErrorCodes.ATC_BROKEN_DATA, ex.Message);
    }

    try
    {
      // ③ 暗号化ヘッダ Nonce + 暗号化ヘッダ本体
      fs.Position = header.EncryptedHeaderStart;
      var headerNonce = new byte[CryptoHelper5.AES_GCM_NONCE_SIZE];
      if (await fs.ReadAsync(headerNonce.AsMemory(0, headerNonce.Length), ct) < headerNonce.Length)
        return EncryptionResult.CreateError(ErrorCodes.ATC_BROKEN_DATA, Localize["Error_HeaderNonceRead"]);

      var encryptedHeader = new byte[header.AtcHeaderSize];
      var read = 0;
      while (read < encryptedHeader.Length)
      {
        var r = await fs.ReadAsync(encryptedHeader.AsMemory(read, encryptedHeader.Length - read), ct);
        if (r <= 0) break;
        read += r;
      }
      if (read < encryptedHeader.Length)
        return EncryptionResult.CreateError(ErrorCodes.ATC_BROKEN_DATA, Localize["Error_EncryptedHeaderRead"]);

      byte[] indexBytes;
      try
      {
        indexBytes = CryptoHelper5.AesGcmDecrypt(encryptedHeader, commonKey, headerNonce);
      }
      catch (InvalidCipherTextException)
      {
        return EncryptionResult.CreateError(
          ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
          Localize["Error_HeaderTagVerify"]);
      }

      // ④ ファイルインデックスを解析
      var fileDataList = ParseV5FileIndex(indexBytes);
      if (fileDataList.Count == 0)
        return EncryptionResult.CreateError(
          ErrorCodes.FILE_INDEX_NOT_FOUND,
          Localize["Error_EmptyFileList"]);

      // 「親フォルダーを作らない」(/nopfldr)。Windows 版 FileDecrypt5 と同じ扱い
      if (options.NoParentFolder) ApplyNoParentFolder(fileDataList);

      // 上書きしない指定なら、書き出しを始める前に保存先の同名ファイルで止める
      var existingOutput = FindExistingOutputFile(outputDirFull, fileDataList, options);
      if (existingOutput != null) return OutputFileExistsError(existingOutput);

      var totalBytes = fileDataList.Where(f => !IsDirectory(f.Attributes) && f.FileSize > 0).Sum(f => f.FileSize);
      var state = new ProgressState(totalBytes, fileDataList.Count);
      var createdDirs = new HashSet<string>(GetPathComparer());
      // すべてのファイルは最終パスではなく、同じフォルダーの一時ファイル（.attmp）へ書き、
      // 本体全体の GCM タグ検証を通ってから最終パスへ移す。検証前に最終名で出すと、
      // 破損・改ざんされた書庫の平文が最終名で見え、既存ファイルなら元の内容も失う。
      // 一時ファイルは開く前に登録し、書き込み中の例外（切り詰め・I/O 失敗・キャンセル・
      // 最終ブロックで表面化するタグ失敗）でも取りこぼさずに片付ける
      var pendingOutputs = new List<PendingOutput>(fileDataList.Count);

      // ⑤ 本体 Nonce 読み取り、ストリーム復号
      var bodyNonce = new byte[CryptoHelper5.AES_GCM_NONCE_SIZE];
      if (await fs.ReadAsync(bodyNonce.AsMemory(0, bodyNonce.Length), ct) < bodyNonce.Length)
        return EncryptionResult.CreateError(ErrorCodes.ATC_BROKEN_DATA, Localize["Error_BodyNonceRead"]);

      var gcmStream = new GcmDecryptingStream(fs, commonKey, bodyNonce, leaveOpen: true);
      try
      {
        await using (var ds = new DeflateStream(gcmStream, CompressionMode.Decompress))
        {
          await ExtractV5FilesAsync(ds, outputDirFull, fileDataList, options,
            progress, swProgress, state, createdDirs, pendingOutputs, ct);
        }

        // ファイル書き出し完了。残バイトを drain して GCM 認証タグを検証するフェーズに入る。
        // 「完了」確定までは 99% で頭打ちにして、完了との誤認を防ぐ。
        progress?.Report(new EncryptionProgress
        {
          Type = ProcessType.Decrypting,
          ProgressPercentage = 9900,
          StatusMessage = Localize["Progress_Verifying"]
        });
        gcmStream.DrainAndVerify();
      }
      catch (InvalidCipherTextException)
      {
        var leftovers = CleanupPartialOutputs(pendingOutputs, createdDirs);
        return WithLeftovers(EncryptionResult.CreateError(
          ErrorCodes.ATC_BROKEN_DATA,
          Localize["Error_BodyTagVerify"]), leftovers);
      }
      catch (Exception ex)
      {
        // 切り詰め（EndOfStreamException）・I/O 失敗・キャンセルでも、
        // 作りかけの平文を残さない（v4 経路と同じ契約）。結果への写像は
        // 呼び出し元の catch に任せ、消せなかった残骸だけ例外に添える
        AttachLeftovers(ex, CleanupPartialOutputs(pendingOutputs, createdDirs));
        throw;
      }
      finally
      {
        gcmStream.Dispose();
      }

      if (state.HashMismatchCount > 0 && !options.SalvageIgnoreHashCheck)
      {
        // タグ検証は通っている＝書庫は真正なので、CRC32 不一致は書き出し後の
        // ディスク側の問題。既存ファイルを壊れた内容で置き換えず、結果も
        // エラーで返す（新規ファイルは検分用に最終名で残す。どのファイルが
        // 不一致だったかは進捗経由で報告済み）
        foreach (var output in pendingOutputs.Where(o => !o.ReplacesExisting))
        {
          try { File.Move(output.TempPath, output.FinalPath, overwrite: true); }
          catch (Exception) { /* 移せなければ一時ファイルのまま下で片付ける */ }
        }
        var leftovers = CleanupTempFiles(pendingOutputs);
        return WithLeftovers(EncryptionResult.CreateError(
          ErrorCodes.ATC_BROKEN_DATA,
          Localize["Error_Crc32Mismatch"]), leftovers);
      }

      // ⑥ 検証を通ったので一時ファイルを最終パスへ移す。
      // 既存ファイルの置き換えもここで初めて起きる（検証前に上書きすると、
      // 破損書庫で元ファイルの内容まで失う）
      foreach (var output in pendingOutputs)
      {
        try
        {
          File.Move(output.TempPath, output.FinalPath, overwrite: true);
        }
        catch (Exception ex)
        {
          // 移し終えた分は検証済みの正しい平文なので残し、
          // まだ移していない一時ファイルだけ片付ける
          var leftovers = CleanupTempFiles(pendingOutputs);
          return WithLeftovers(EncryptionResult.CreateError(
            ErrorCodes.ERROR_UNEXPECTED, ex.Message, output.FinalPath), leftovers);
        }
      }

      progress?.Report(new EncryptionProgress
      {
        Type = ProcessType.Decrypting,
        ProgressPercentage = 10000,
        StatusMessage = Localize["Progress_Completed"]
      });

      return EncryptionResult.CreateSuccess(outputDirectory, fileDataList.Count, "");
    }
    finally
    {
      CryptoHelper5.SecureClear(commonKey);
    }
  }

  /// <summary>
  /// v5 平文ヘッダ + 鍵保護ブロックを読み取り、本体読み取り開始位置を記録する。
  /// </summary>
  /// <summary>
  /// 「破壊済み」としてマークされたファイルか。
  /// 破壊するとヘッダの後半も壊れて解析できなくなるため、
  /// 先頭のトークンだけを見て判定する。
  /// </summary>
  public static bool IsMarkedBroken(string filePath)
  {
    try
    {
      using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

      var token = new byte[16];
      fs.Seek(4, SeekOrigin.Begin);
      if (fs.Read(token, 0, 16) < 16) return false;

      return Encoding.ASCII.GetString(token) == STRING_TOKEN_BROKEN;
    }
    catch (Exception)
    {
      return false;
    }
  }

  /// <summary>
  /// 暗号化ファイルに記録された「パスワード入力の制限回数」と
  /// 「制限を超えたら破壊するか」を読む。
  /// 平文ヘッダの先頭 4 バイトは v140 と v150 で共通なので、どちらでも読める。
  /// </summary>
  /// <returns>回数（読めなければ 3）と破壊フラグ</returns>
  public static (int Limit, bool BreakOnLimit) ReadPasswordLimit(string filePath)
  {
    try
    {
      using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

      var head = new byte[4];
      if (fs.Read(head, 0, 4) < 4) return (3, false);

      var limit = head[2];
      return (limit > 0 ? limit : 3, head[3] != 0);
    }
    catch (Exception)
    {
      return (3, false);
    }
  }

  /// <summary>
  /// 暗号化ファイルを破壊する（パスワード入力の制限回数を超えたとき）。
  /// </summary>
  /// <remarks>
  /// Windows 版 AttacheCase の BreakTheFile と同じ手順:
  ///   1. 先頭 4 バイト目からのトークンを「_Atc_Broken_Data」へ書き換える
  ///   2. 32 バイト目から 32 バイトを非ゼロの乱数で上書きする
  /// v150 では 32 バイト目以降に「暗号化ヘッダのサイズ」と鍵保護ブロック（Argon2 の
  /// ソルト）が並ぶため、ここを壊すと共通鍵を復元できなくなる。
  /// </remarks>
  /// <returns>破壊できたら true。すでに破壊済みの場合も true</returns>
  public static bool BreakTheFile(string filePath)
  {
    try
    {
      using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

      var token = new byte[16];
      fs.Seek(4, SeekOrigin.Begin);
      if (fs.Read(token, 0, 16) < 16) return false;

      var tokenStr = Encoding.ASCII.GetString(token);
      if (tokenStr == STRING_TOKEN_BROKEN) return true;   // すでに破壊済み
      if (tokenStr != STRING_TOKEN_NORMAL && tokenStr != STRING_TOKEN_RSA) return false;

      // トークンの書き換え
      fs.Seek(4, SeekOrigin.Begin);
      fs.Write(Encoding.ASCII.GetBytes(STRING_TOKEN_BROKEN), 0, 16);

      // 鍵保護ブロックの手前から 32 バイトを非ゼロの乱数でつぶす
      var noise = new byte[32];
      RandomNumberGenerator.Fill(noise);
      for (var i = 0; i < noise.Length; i++)
      {
        if (noise[i] == 0) noise[i] = 0xFF;
      }

      fs.Seek(32, SeekOrigin.Begin);
      fs.Write(noise, 0, noise.Length);
      fs.Flush(true);

      return true;
    }
    catch (Exception)
    {
      return false;
    }
  }

  /// <summary>
  /// v150 の暗号化ファイルが公開鍵方式（Post-PPAP / RSA ハイブリッド）で
  /// 暗号化されているかを、平文ヘッダの encryptionMode から判定する。
  /// パスワード入力 UI を出すかどうかの判断に使う。
  /// </summary>
  public static bool IsPublicKeyEncrypted(string filePath)
  {
    try
    {
      using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
      return TryReadV5PlainHeader(fs, out var header) && header.EncryptionMode == 0x01;
    }
    catch (Exception)
    {
      return false;
    }
  }

  /// <summary>
  /// v150 の公開鍵方式の種類を平文ヘッダーから判定する。
  /// RSA 鍵ファイル方式（.atcpub で暗号化、宛先は RSA 単体）と Post-PPAP（RSA + ML-KEM ハイブリッド）は
  /// 先頭 20 バイトが通常の ATC と同じで FileTypeDetector では区別できない。GUI はこの判定で
  /// 「秘密鍵（.atcpvt）待ちの画面」と「Post-PPAP アカウントで自動復号」を振り分ける。
  /// 自己実行形式（.exe）もトークン走査で同じように判定する。読めないファイルは None。
  /// </summary>
  public static PublicKeyEncryptionKind GetPublicKeyEncryptionKind(string filePath)
  {
    try
    {
      using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
      if (!TryReadV5PlainHeader(fs, out var header) || header.EncryptionMode != 0x01)
        return PublicKeyEncryptionKind.None;
      return header.PublicKeyAlgorithm is PkaAlgorithmId.Rsa2048 or PkaAlgorithmId.Rsa4096
        ? PublicKeyEncryptionKind.RsaKeyFile
        : PublicKeyEncryptionKind.PostPpap;
    }
    catch (Exception)
    {
      return PublicKeyEncryptionKind.None;
    }
  }

  private static bool TryReadV5PlainHeader(FileStream fs, out V5Header header)
  {
    header = default;

    // 標準位置を試す
    var startOffset = 0L;
    fs.Position = 0;
    if (!TryReadV5PlainHeaderAt(fs, ref startOffset, out header))
    {
      // 先頭が旧形式（v150 未満）の ATC ヘッダーなら v5 のヘッダーは無い。走査しても無駄で、
      // 大きな旧形式ファイルをドロップしたときに UI スレッドで最大 64MB を読んでしまう
      // （CodeRabbit PR#19 の指摘）
      if (HasLegacyHeaderAtStart(fs)) return false;

      // 自己実行形式: トークンスキャン
      fs.Position = 0;
      var needles = new[]
      {
        Encoding.ASCII.GetBytes(STRING_TOKEN_NORMAL),
        Encoding.ASCII.GetBytes(STRING_TOKEN_BROKEN),
        Encoding.ASCII.GetBytes(STRING_TOKEN_RSA)
      };
      const int MAX_SCAN = 64 * 1024 * 1024;
      var scanLimit = Math.Min(fs.Length, MAX_SCAN);
      var buf = ArrayPool<byte>.Shared.Rent(1024 * 1024);
      try
      {
        long pos = 0;
        const int overlap = 64;
        Array.Clear(buf, 0, overlap);
        while (pos < scanLimit)
        {
          fs.Position = pos;
          var toRead = (int)Math.Min(buf.Length - overlap, scanLimit - pos);
          var r = fs.Read(buf, overlap, toRead);
          if (r <= 0) break;
          var windowLen = overlap + r;
          foreach (var needle in needles)
          {
            var idx = IndexOf(buf, 0, windowLen, needle);
            if (idx >= 0)
            {
              var tokenPos = pos + idx - overlap;
              var headerPos = tokenPos - 4;
              if (headerPos < 0) continue;
              startOffset = headerPos;
              if (TryReadV5PlainHeaderAt(fs, ref startOffset, out header))
                return true;
            }
          }
          Buffer.BlockCopy(buf, windowLen - overlap, buf, 0, overlap);
          pos += r;
        }
      }
      finally
      {
        ArrayPool<byte>.Shared.Return(buf);
      }
      return false;
    }
    return true;
  }

  /// <summary>
  /// 先頭に旧形式（データ版 150 未満）の ATC ヘッダーがあるか。
  /// 旧形式も v5 もトークンは 4 バイト目から 16 バイト、データ版は 20 バイト目の int で同じ位置。
  /// </summary>
  private static bool HasLegacyHeaderAtStart(FileStream fs)
  {
    if (fs.Length < 24) return false;
    fs.Position = 0;
    var head = new byte[24];
    if (fs.Read(head, 0, 24) < 24) return false;
    var tokenStr = Encoding.ASCII.GetString(head, 4, 16).TrimEnd('\0');
    if (tokenStr != STRING_TOKEN_NORMAL && tokenStr != STRING_TOKEN_BROKEN && tokenStr != STRING_TOKEN_RSA)
      return false;
    return BitConverter.ToInt32(head, 20) < DATA_FILE_VERSION_V5;
  }

  private static bool TryReadV5PlainHeaderAt(FileStream fs, ref long offset, out V5Header header)
  {
    header = default;
    if (fs.Length - offset < 36) return false;

    fs.Position = offset;
    var head = new byte[36];
    if (fs.Read(head, 0, 36) < 36) return false;

    var appVersion = BitConverter.ToInt16(head, 0);
    var missTypeLimits = head[2];
    var isBroken = head[3] != 0;
    var tokenStr = Encoding.ASCII.GetString(head, 4, 16).TrimEnd('\0');
    if (tokenStr != STRING_TOKEN_NORMAL && tokenStr != STRING_TOKEN_BROKEN && tokenStr != STRING_TOKEN_RSA)
      return false;
    var dataFileVersion = BitConverter.ToInt32(head, 20);
    if (dataFileVersion < DATA_FILE_VERSION_V5) return false;

    var encMode = head[24];
    var symAlgo = head[25];
    var pubAlgo = head[26];
    var kdfAlgo = head[27];
    // Reserved 4 bytes (offset 28..31)
    var atcHeaderSize = BitConverter.ToInt32(head, 32);
    if (atcHeaderSize <= 0 || atcHeaderSize > 1024 * 1024 * 128) return false;

    // 鍵保護ブロック
    byte[] kpb;
    if (encMode == 0x00)
    {
      // パスワード方式: Salt(16) + Memory(4) + Iter(4) + Para(4) + Size(2) + EncKey(Size)
      var fixedPart = new byte[16 + 4 + 4 + 4 + 2];
      if (fs.Read(fixedPart, 0, fixedPart.Length) < fixedPart.Length) return false;
      var encKeySize = BitConverter.ToInt16(fixedPart, fixedPart.Length - 2);
      if (encKeySize < 0 || encKeySize > 4096) return false;
      var encKey = new byte[encKeySize];
      if (fs.Read(encKey, 0, encKeySize) < encKeySize) return false;
      kpb = new byte[fixedPart.Length + encKeySize];
      Buffer.BlockCopy(fixedPart, 0, kpb, 0, fixedPart.Length);
      Buffer.BlockCopy(encKey, 0, kpb, fixedPart.Length, encKeySize);
    }
    else
    {
      // 公開鍵方式: N(2) + (Fingerprint(8) + Size(2) + EncKey(Size)) × N
      var nBuf = new byte[2];
      if (fs.Read(nBuf, 0, 2) < 2) return false;
      var n = BitConverter.ToInt16(nBuf, 0);
      using var ms = new MemoryStream();
      ms.Write(nBuf, 0, 2);
      for (var i = 0; i < n; i++)
      {
        var fp = new byte[CryptoHelper5.FINGERPRINT_SIZE];
        if (fs.Read(fp, 0, fp.Length) < fp.Length) return false;
        ms.Write(fp, 0, fp.Length);
        var sizeBuf = new byte[2];
        if (fs.Read(sizeBuf, 0, 2) < 2) return false;
        ms.Write(sizeBuf, 0, 2);
        var encKeySize = BitConverter.ToInt16(sizeBuf, 0);
        if (encKeySize < 0 || encKeySize > 8192) return false;
        var encKey = new byte[encKeySize];
        if (fs.Read(encKey, 0, encKeySize) < encKeySize) return false;
        ms.Write(encKey, 0, encKeySize);
      }
      kpb = ms.ToArray();
    }

    header = new V5Header(
      offset, appVersion, missTypeLimits, isBroken, tokenStr, dataFileVersion,
      atcHeaderSize, encMode, symAlgo, pubAlgo, kdfAlgo, kpb, fs.Position);
    return true;
  }

  /// <summary>
  /// パスワード方式の鍵保護ブロックから共通鍵を復元する。
  /// </summary>
  private static byte[] RecoverCommonKeyFromPasswordV5(
    V5Header header, string password, byte[]? passwordBinary = null)
  {
    var kpb = header.KeyProtectionBlock;
    var offset = 0;
    var salt = new byte[CryptoHelper5.ARGON2_SALT_SIZE];
    Buffer.BlockCopy(kpb, offset, salt, 0, salt.Length); offset += salt.Length;

    var memoryKb = BitConverter.ToInt32(kpb, offset); offset += 4;
    var iterations = BitConverter.ToInt32(kpb, offset); offset += 4;
    var parallelism = BitConverter.ToInt32(kpb, offset); offset += 4;
    var encKeySize = BitConverter.ToInt16(kpb, offset); offset += 2;

    // Argon2 パラメータは認証前に読む値。検証せずに鍵導出へ渡すと、改ざんされた
    // 12 バイトで巨大なメモリ確保や事実上のフリーズを起こされる（Windows 版と同じ守り）
    if (!CryptoHelper5.IsValidArgon2Parameters(memoryKb, iterations, parallelism))
      throw new InvalidDataException(Localize["Error_InvalidKdfParameters"]);

    // パスワード方式の暗号化共通鍵は Nonce(12) + 鍵(32) + タグ(16) = 60 バイト固定。
    // ファイルから読んだサイズをそのまま確保に使わない
    if (encKeySize != CryptoHelper5.ENCRYPTED_COMMON_KEY_SIZE_PASSWORD ||
        offset + encKeySize > kpb.Length)
      throw new InvalidDataException(Localize["Error_InvalidKeyProtectionBlock"]);

    var protectedKey = new byte[encKeySize];
    Buffer.BlockCopy(kpb, offset, protectedKey, 0, encKeySize);

    // パスワードファイル指定時は、その鍵素材から直接導出する
    var derivedKey = passwordBinary != null
      ? CryptoHelper5.DeriveKeyFromPasswordBinary(passwordBinary, salt, memoryKb, iterations, parallelism)
      : CryptoHelper5.DeriveKeyFromPassword(password, salt, memoryKb, iterations, parallelism);
    try
    {
      return CryptoHelper5.UnprotectCommonKeyWithPassword(protectedKey, derivedKey);
    }
    finally
    {
      CryptoHelper5.SecureClear(derivedKey);
    }
  }

  /// <summary>
  /// 公開鍵方式 (Post-PPAP) の鍵保護ブロックから共通鍵を復元する。
  /// accounts.json に登録された自アカウント群を順に試行し、fingerprint が一致する
  /// 受信者エントリを見つけて Keychain から秘密鍵を取り出して復号する。
  /// 一致する秘密鍵が見つからない場合は <see cref="NoMatchingPrivateKeyException"/>。
  /// </summary>
  private static byte[] RecoverCommonKeyFromPublicKeyV5(V5Header header, string? rsaPrivateKeyXml)
  {
    // RSA 鍵ファイル（.atcpvt）が渡されていればそれで復号する（Post-PPAP の
    // アカウントには落ちない。鍵を指定したのに別の鍵で開くと利用者が混乱する）
    if (!string.IsNullOrEmpty(rsaPrivateKeyXml))
      return RecoverCommonKeyFromRsaKeyFileV5(header, rsaPrivateKeyXml);
    return RecoverCommonKeyFromPublicKeyV5(header, new PostPpapManager());
  }

  /// <summary>
  /// RSA 鍵ファイル（.atcpvt）で鍵保護ブロックから共通鍵を復元する。
  /// 宛先エントリは RSA 単体（Crypto/RsaKeyFile が書いた形）で、指紋は公開鍵部分から計算する。
  /// 鍵が読めない・ハイブリッド方式のファイル・一致する宛先が無い、は
  /// <see cref="NoMatchingPrivateKeyException"/>（利用者向けメッセージ付き）。
  /// </summary>
  private static byte[] RecoverCommonKeyFromRsaKeyFileV5(V5Header header, string rsaPrivateKeyXml)
  {
    Org.BouncyCastle.Crypto.Parameters.RsaPrivateCrtKeyParameters privateKey;
    try
    {
      privateKey = RsaKeyFile.ParsePrivateKey(rsaPrivateKeyXml);
    }
    catch (FormatException ex)
    {
      throw new NoMatchingPrivateKeyException(
        Localize.GetString("Error_RsaKeyFileInvalid", ex.Message));
    }

    var isHybrid = header.PublicKeyAlgorithm is PkaAlgorithmId.Rsa2048MlKem768
                                          or PkaAlgorithmId.Rsa4096MlKem768;
    if (isHybrid)
      throw new NoMatchingPrivateKeyException(Localize["Error_RsaKeyFileHybridFile"]);

    var myFingerprint = RsaKeyFile.ComputeFingerprint(privateKey);
    var entries = CryptoHelper5.ParsePublicKeyProtectionBlock(header.KeyProtectionBlock, 0, out _);
    foreach (var entry in entries)
    {
      if (CryptoHelper5.ConstantTimeEquals(entry.Fingerprint, myFingerprint))
        return CryptoHelper5.UnprotectCommonKeyWithRsa(entry.EncryptedCommonKey, privateKey);
    }
    throw new NoMatchingPrivateKeyException(Localize["Error_RsaKeyFileNoMatch"]);
  }

  private static byte[] RecoverCommonKeyFromPublicKeyV5(V5Header header, PostPpapManager manager)
  {
    var entries = CryptoHelper5.ParsePublicKeyProtectionBlock(header.KeyProtectionBlock, 0, out _);
    var accounts = manager.LoadAccounts();
    if (accounts.Count == 0)
      throw new NoMatchingPrivateKeyException(
        Localize["Error_PostPpapNoAccount"]);

    var isHybrid = header.PublicKeyAlgorithm is PkaAlgorithmId.Rsa2048MlKem768
                                          or PkaAlgorithmId.Rsa4096MlKem768;

    foreach (var account in accounts)
    {
      var fpBytes = CryptoHelper5.HexStringToBytes(account.Fingerprint);
      CryptoHelper5.ParsedRecipientEntry? matched = null;
      foreach (var entry in entries)
      {
        if (CryptoHelper5.ConstantTimeEquals(entry.Fingerprint, fpBytes))
        {
          matched = entry;
          break;
        }
      }
      if (matched == null) continue;

      var rsaPriv = manager.LoadRsaPrivateKey(account);
      if (isHybrid)
      {
        var mlkemPriv = manager.LoadMlKemPrivateKey(account);
        var rsaCipherSize = CryptoHelper5.GetRsaCipherSize(header.PublicKeyAlgorithm);
        return CryptoHelper5.UnprotectCommonKeyWithHybrid(
          matched.EncryptedCommonKey, rsaPriv, mlkemPriv, rsaCipherSize);
      }
      return CryptoHelper5.UnprotectCommonKeyWithRsa(matched.EncryptedCommonKey, rsaPriv);
    }
    throw new NoMatchingPrivateKeyException(
      Localize["Error_NoMatchingPrivateKey"]);
  }

  /// <summary>
  /// 公開鍵方式の復号で、対応する秘密鍵がローカルに見つからなかったことを表す。
  /// （秘密鍵はあるが復号に失敗した場合は <see cref="InvalidCipherTextException"/> が
  ///  別途投げられる）。
  /// </summary>
  internal sealed class NoMatchingPrivateKeyException : Exception
  {
    public NoMatchingPrivateKeyException(string message) : base(message) { }
  }

  /// <summary>
  /// 復号された v5 ファイルインデックスを解析する。
  ///   各エントリ:
  ///     FileNameSize(2) + FileName(可変) + FileSize(8) + Attr(4)
  ///     + LastWriteDate(4) + LastWriteTime(4)
  ///     + CreationDate(4) + CreationTime(4)
  ///     + CRC32(4)  ※ファイルでサイズ > 0 のときのみ
  /// </summary>
  private static List<FileData> ParseV5FileIndex(byte[] indexBytes)
  {
    var list = new List<FileData>(1024);
    using var ms = new MemoryStream(indexBytes, false);
    using var br = new BinaryReader(ms, Encoding.UTF8, true);

    while (ms.Position < ms.Length)
    {
      if (ms.Length - ms.Position < 2) break;
      var nameSize = br.ReadInt16();
      if (nameSize <= 0 || nameSize > 32767) break;
      if (ms.Length - ms.Position < nameSize) break;
      var nameBytes = br.ReadBytes(nameSize);
      var relPath = Encoding.UTF8.GetString(nameBytes);

      if (ms.Length - ms.Position < 8 + 4 + 16) break;
      var fileSize = br.ReadInt64();
      var attrInt = br.ReadInt32();
      var lwDate = br.ReadInt32();
      var lwTime = br.ReadInt32();
      var crDate = br.ReadInt32();
      var crTime = br.ReadInt32();

      DateTime lwUtc = DateTime.MinValue, crUtc = DateTime.MinValue;
      if (TryParseYmdHmsUtc(lwDate, lwTime, out var lwParsed)) lwUtc = lwParsed;
      if (TryParseYmdHmsUtc(crDate, crTime, out var crParsed)) crUtc = crParsed;

      byte[]? crc32 = null;
      if (fileSize > 0 && (attrInt & (int)FileAttributes.Directory) == 0)
      {
        if (ms.Length - ms.Position < 4) break;
        crc32 = br.ReadBytes(4);
      }

      list.Add(new FileData
      {
        RelativePath = relPath,
        FileSize = fileSize,
        Attributes = (FileAttributes)attrInt,
        LastWriteUtc = lwUtc,
        CreationUtc = crUtc,
        Hash = crc32
      });
    }

    return list;
  }

  /// <summary>
  /// 復号ストリーム（Deflate 展開済み）からファイル列を取り出す（v5 形式）。
  /// </summary>
  private static async Task ExtractV5FilesAsync(
      Stream deflatedStream,
      string outputDirFull,
      List<FileData> fileDataList,
      DecryptionOptions options,
      IProgress<EncryptionProgress>? progress,
      Stopwatch swProgress,
      ProgressState state,
      HashSet<string> createdDirs,
      List<PendingOutput> pendingOutputs,
      CancellationToken ct)
  {
    var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
    try
    {
      foreach (var fd in fileDataList)
      {
        ct.ThrowIfCancellationRequested();
        state.ProcessedFiles++;

        var rel = NormalizeRelativePath(fd.RelativePath);
        var outPath = GetSafeOutputPath(outputDirFull, rel);
        if (outPath == null)
        {
          await SkipEntryDataAsync(deflatedStream, fd, buffer, ct).ConfigureAwait(false);
          Report(progress, state, fd.RelativePath, Localize["Progress_SkippedUnsafePath"]);
          continue;
        }

        if (IsDirectory(fd.Attributes) || (fd.FileSize == 0 && outPath.EndsWith(Path.DirectorySeparatorChar)))
        {
          CreateDirectoryIfNeeded(outPath, createdDirs);
          if (options.RestoreTimeStamp) TrySetDirectoryTimesUtc(outPath, fd.CreationUtc, fd.LastWriteUtc);
          Report(progress, state, fd.RelativePath, null);
          continue;
        }

        var parent = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(parent)) CreateDirectoryIfNeeded(parent, createdDirs);

        // 最終パスには書かない。FileMode.Create で開いた時点で既存ファイルの内容が
        // 失われ、タグ検証失敗・切り詰め・キャンセルで巻き戻せなくなるうえ、新規
        // ファイルでも認証前の平文が最終名で見える。同じフォルダーの一時ファイルへ
        // 書き、本体全体の検証後に DecryptV5Async が最終パスへ移す。
        // 登録は開く前に行う（書き込み中の例外でも片付けの対象から漏れないように）
        var writePath = MakeTempOutputPath(outPath);
        pendingOutputs.Add(new PendingOutput(writePath, outPath, File.Exists(outPath)));

        if (fd.FileSize <= 0)
        {
          await using (var _ = new FileStream(writePath, FileMode.Create, FileAccess.Write, FileShare.None)) { }
          if (options.RestoreTimeStamp) TrySetFileTimesUtc(writePath, fd.CreationUtc, fd.LastWriteUtc);
          TrySetFileAttributes(writePath, fd.Attributes);
          Report(progress, state, fd.RelativePath, null);
          continue;
        }

        await using (var outFs = new FileStream(writePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
          var remaining = fd.FileSize;
          while (remaining > 0)
          {
            ct.ThrowIfCancellationRequested();
            var toRead = remaining > buffer.Length ? buffer.Length : (int)remaining;
            var r = await deflatedStream.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);
            if (r <= 0) throw new EndOfStreamException(Localize["Error_TruncatedData"]);
            await outFs.WriteAsync(buffer.AsMemory(0, r), ct).ConfigureAwait(false);
            remaining -= r;
            state.ProcessedBytes += r;
            if (swProgress.ElapsedMilliseconds > 100)
            {
              Report(progress, state, fd.RelativePath, null);
              swProgress.Restart();
            }
          }
        }

        // タイムスタンプと属性は書き込み先（一時ファイル側）へ付ける。
        // File.Move での置き換えはどちらも引き継ぐ
        if (options.RestoreTimeStamp) TrySetFileTimesUtc(writePath, fd.CreationUtc, fd.LastWriteUtc);
        TrySetFileAttributes(writePath, fd.Attributes);

        // CRC32 検証（あれば）— 大きなファイルではディスク全走査になり数秒かかる場合がある。
        // 直前にステータス表示を更新して 99% で「復号中...」のまま固まる挙動を防ぐ。
        // ProgressPercentage は 99% で頭打ち（完了との誤認防止）。
        if (!options.SalvageIgnoreHashCheck && fd.Hash is { Length: 4 })
        {
          progress?.Report(new EncryptionProgress
          {
            Type = ProcessType.Decrypting,
            CurrentFilePath = fd.RelativePath,
            ProcessedFiles = state.ProcessedFiles,
            TotalFiles = state.TotalFiles,
            ProgressPercentage = 9900,
            StatusMessage = Localize["Progress_Verifying"]
          });
          byte[]? computed = null;
          try
          {
            computed = Crc32Utility.ComputeFileCrc32(writePath);
          }
          catch (Exception ex)
          {
            // 書き出したファイルを読み直せなくても、復号そのものは終わっている。
            // 検証できなかったことだけ伝えて次へ進む
            Console.Error.WriteLine($"CRC32 verify skipped: {writePath}: {ex.Message}");
          }

          if (computed != null && !computed.SequenceEqual(fd.Hash))
          {
            // 進捗で場所を伝えたうえで、集計して最終結果にも反映する
            // （報告だけだと、破損した出力が「成功」として返る）
            state.HashMismatchCount++;
            Report(progress, state, fd.RelativePath, Localize["Error_Crc32Mismatch"]);
          }
        }

        Report(progress, state, fd.RelativePath, null);
      }
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buffer);
    }
  }

  /// <summary>
  /// v5 (DATA_FILE_VERSION = 150) フォーマットの暗号化ファイルから、本体を展開せずに
  /// ファイルリストのみ取得する。パスワード方式のみ対応（Phase 1）。
  /// </summary>
  private static FileListResult GetFileListV5(string inputPath, string password,
      byte[]? passwordBinary = null, string? rsaPrivateKeyXml = null)
  {
    try
    {
      using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);

      if (!TryReadV5PlainHeader(fs, out var header))
        return FileListResult.CreateError(ErrorCodes.PASSWORD_TOKEN_NOT_FOUND, Localize["Error_HeaderNotFound"]);

      if (header.IsBroken)
        return FileListResult.CreateError(
          ErrorCodes.ATC_BROKEN_DATA, Localize["Error_FileAlreadyBroken"]);

      if (header.SymmetricAlgorithm != 0x01)
        return FileListResult.CreateError(ErrorCodes.ERROR_UNEXPECTED,
          Localize.GetString("Error_UnsupportedSymmetricAlgorithm", $"{header.SymmetricAlgorithm:X2}"));
      if (header.EncryptionMode == 0x00 && header.KdfAlgorithm != 0x01)
        return FileListResult.CreateError(ErrorCodes.ERROR_UNEXPECTED,
          Localize.GetString("Error_UnsupportedKdf", $"{header.KdfAlgorithm:X2}"));

      byte[] commonKey;
      try
      {
        commonKey = header.EncryptionMode == 0x01
          ? RecoverCommonKeyFromPublicKeyV5(header, rsaPrivateKeyXml)
          : RecoverCommonKeyFromPasswordV5(header, password, passwordBinary);
      }
      catch (InvalidCipherTextException)
      {
        return FileListResult.CreateError(
          ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
          header.EncryptionMode == 0x01
            ? Localize["Error_PublicKeyRecoverFailed"]
            : Localize["Error_WrongPassword"]);
      }
      catch (NoMatchingPrivateKeyException ex)
      {
        return FileListResult.CreateError(ErrorCodes.PASSWORD_TOKEN_NOT_FOUND, ex.Message);
      }
      catch (InvalidDataException ex)
      {
        // 鍵保護ブロックの Argon2 パラメータ・サイズが許容範囲外（改ざん・破損）
        return FileListResult.CreateError(ErrorCodes.ATC_BROKEN_DATA, ex.Message);
      }

      try
      {
        fs.Position = header.EncryptedHeaderStart;
        var headerNonce = new byte[CryptoHelper5.AES_GCM_NONCE_SIZE];
        if (fs.Read(headerNonce, 0, headerNonce.Length) < headerNonce.Length)
          return FileListResult.CreateError(ErrorCodes.ATC_BROKEN_DATA, Localize["Error_HeaderNonceRead"]);

        var encHeader = new byte[header.AtcHeaderSize];
        var read = 0;
        while (read < encHeader.Length)
        {
          var r = fs.Read(encHeader, read, encHeader.Length - read);
          if (r <= 0) break;
          read += r;
        }
        if (read < encHeader.Length)
          return FileListResult.CreateError(ErrorCodes.ATC_BROKEN_DATA, Localize["Error_EncryptedHeaderRead"]);

        byte[] indexBytes;
        try
        {
          indexBytes = CryptoHelper5.AesGcmDecrypt(encHeader, commonKey, headerNonce);
        }
        catch (InvalidCipherTextException)
        {
          return FileListResult.CreateError(ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
            Localize["Error_HeaderTagVerify"]);
        }

        var fileDataList = ParseV5FileIndex(indexBytes);
        var paths = fileDataList.Select(f => NormalizeRelativePath(f.RelativePath)).ToList();
        return FileListResult.CreateSuccess(paths);
      }
      finally
      {
        CryptoHelper5.SecureClear(commonKey);
      }
    }
    catch (Exception ex)
    {
      return FileListResult.CreateError(ErrorCodes.ERROR_UNEXPECTED, ex.Message);
    }
  }

  private readonly struct V5Header(
    long headerOffset,
    short appVersion,
    byte missTypeLimits,
    bool isBroken,
    string tokenStr,
    int dataFileVersion,
    int atcHeaderSize,
    byte encryptionMode,
    byte symmetricAlgorithm,
    byte publicKeyAlgorithm,
    byte kdfAlgorithm,
    byte[] keyProtectionBlock,
    long encryptedHeaderStart)
  {
    public long HeaderOffset { get; } = headerOffset;
    public short AppVersion { get; } = appVersion;
    public byte MissTypeLimits { get; } = missTypeLimits;
    /// <summary>
    /// 制限回数を超えたら破壊してよいか（平文ヘッダ 4 バイト目）。
    /// 「すでに壊れている」ではない。壊れているかどうかはトークンで判別する。
    /// </summary>
    public bool BreakOnLimit { get; } = isBroken;

    public string TokenStr { get; } = tokenStr;

    /// <summary>破壊済みとしてマークされているか</summary>
    public bool IsBroken => TokenStr == STRING_TOKEN_BROKEN;

    public int DataFileVersion { get; } = dataFileVersion;
    public int AtcHeaderSize { get; } = atcHeaderSize;
    public byte EncryptionMode { get; } = encryptionMode;
    public byte SymmetricAlgorithm { get; } = symmetricAlgorithm;
    public byte PublicKeyAlgorithm { get; } = publicKeyAlgorithm;
    public byte KdfAlgorithm { get; } = kdfAlgorithm;
    public byte[] KeyProtectionBlock { get; } = keyProtectionBlock;
    /// <summary>暗号化ヘッダ Nonce が始まる位置（鍵保護ブロック直後）</summary>
    public long EncryptedHeaderStart { get; } = encryptedHeaderStart;
  }

  private static async Task<EncryptionResult> DecryptFileAsync(
    string inputPath,
    string outputDirectory,
    byte[] passwordBinary,
    DecryptionOptions options,
    IProgress<EncryptionProgress>? progress,
    CancellationToken cancellationToken)
  {
    var swProgress = Stopwatch.StartNew();

    var outputDirFull = EnsureTrailingSeparator(Path.GetFullPath(outputDirectory));

    var createdFiles = new List<string>(256);
    var createdDirs = new HashSet<string>(GetPathComparer());

    await using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);

    // 1) 平文ヘッダー読み取り（先頭にない場合は簡易スキャン）
    if (!TryReadPlainHeaderAt(fs, 0, out var header))
      if (!TryFindPlainHeader(fs, out header))
        return EncryptionResult.CreateError(
          ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
          Localize["Error_WrongPasswordOrBrokenHeader"]);

    if (header.DataFileVersion != DATA_FILE_VERSION)
      return EncryptionResult.CreateError(
        ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
        Localize.GetString("Error_UnsupportedDataVersion", header.DataFileVersion));

    if (header.TokenStr.TrimEnd('\0') == STRING_TOKEN_BROKEN)
      return EncryptionResult.CreateError(
        ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
        Localize["Error_MarkedBroken"]);

    // 2) PBKDF2（Windows互換：HMAC-SHA1, 1000）
    var (key, iv) = DeriveKeyAndIvV4(passwordBinary, header.Salt);

    // 3) 暗号化ヘッダー復号 → ファイルリスト取得
    var fileDataList = await ReadFileIndexAsync(
      fs,
      header,
      key,
      iv,
      cancellationToken).ConfigureAwait(false);

    if (fileDataList == null || fileDataList.Count == 0)
      return EncryptionResult.CreateError(
        ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
        Localize["Error_WrongPasswordOrBrokenList"]);

    // 「親フォルダーを作らない」(/nopfldr)。Windows 版 FileDecrypt4 と同じ扱い
    if (options.NoParentFolder) ApplyNoParentFolder(fileDataList);

    // 上書きしない指定なら、書き出しを始める前に保存先の同名ファイルで止める
    var existingOutput = FindExistingOutputFile(outputDirFull, fileDataList, options);
    if (existingOutput != null) return OutputFileExistsError(existingOutput);

    var totalBytes = fileDataList.Where(f => !IsDirectory(f.Attributes) && f.FileSize > 0).Sum(f => f.FileSize);
    var state = new ProgressState(totalBytes, fileDataList.Count);

    // 4) 本体復号：Windows互換レイアウトを先に試す → 失敗したら旧Macレイアウトにフォールバック
    try
    {
      await ExtractWindowsLayoutAsync(
        inputPath,
        fs,
        outputDirFull,
        header,
        key,
        iv,
        fileDataList,
        options,
        progress,
        swProgress,
        state,
        createdFiles,
        createdDirs,
        cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // 利用者が止めたのに旧レイアウトで復号し直すと、二度目も同じトークンで
      // 止まったうえ「復号失敗」として見えてしまう
      CleanupCreatedPaths(createdFiles, createdDirs);
      throw;
    }
    catch (Exception)
    {
      // 作りかけを掃除してからフォールバック
      CleanupCreatedPaths(createdFiles, createdDirs);

      try
      {
        await ExtractLegacyMacLayoutAsync(
          inputPath,
          fs,
          outputDirFull,
          header,
          key,
          iv,
          fileDataList,
          options,
          progress,
          swProgress,
          state,
          createdFiles,
          createdDirs,
          cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex2)
      {
        return EncryptionResult.CreateError(
          ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
          Localize.GetString("Error_DecryptFailedBothPaths", ex2.Message));
      }
    }

    progress?.Report(new EncryptionProgress
    {
      Type = ProcessType.Decrypting,
      ProgressPercentage = 10000,
      StatusMessage = Localize["Progress_Completed"]
    });

    return EncryptionResult.CreateSuccess(outputDirectory, fileDataList.Count, "");
  }

  // -------------------------
  // Windows互換レイアウト復号
  // -------------------------
  private static async Task ExtractWindowsLayoutAsync(
    string inputPath,
    FileStream fs,
    string outputDirFull,
    PlainHeader header,
    byte[] key,
    byte[] iv,
    List<FileData> fileDataList,
    DecryptionOptions options,
    IProgress<EncryptionProgress>? progress,
    Stopwatch swProgress,
    ProgressState state,
    List<string> createdFiles,
    HashSet<string> createdDirs,
    CancellationToken cancellationToken)
  {
    // RSA暗号化の場合、ヘッダーに256バイトのRSAブロックが追加されている
    var rsaBlockSize = header.RsaBlock.Length > 0 ? 256 : 0;

    // 本体開始位置：
    // 52 + rsaBlockSize(RSAの場合256) + cipherHeaderLen（= AtcHeaderSize + pad(16)）
    var bodyStart = header.HeaderOffset + PLAIN_HEADER_SIZE + rsaBlockSize +
                    GetCipherLengthZerosPadding(header.AtcHeaderSize);
    fs.Position = bodyStart;

    using var aes = Aes.Create();
    aes.KeySize = 256;
    aes.BlockSize = 128;
    aes.Mode = CipherMode.CBC;
    aes.Padding = PaddingMode.PKCS7;
    aes.Key = key;
    aes.IV = iv;

    await using var cryptoStream = new CryptoStream(fs, aes.CreateDecryptor(), CryptoStreamMode.Read, true);
    await using var deflateStream = new DeflateStream(cryptoStream, CompressionMode.Decompress, true);

    await ExtractFromDeflatedStreamAsync(
      deflateStream,
      outputDirFull,
      fileDataList,
      options,
      progress,
      swProgress,
      state,
      createdFiles,
      createdDirs,
      cancellationToken).ConfigureAwait(false);
  }

  // -------------------------
  // 旧Macレイアウト復号（連結CryptoStream）
  // -------------------------
  private static async Task ExtractLegacyMacLayoutAsync(
    string inputPath,
    FileStream fs,
    string outputDirFull,
    PlainHeader header,
    byte[] key,
    byte[] iv,
    List<FileData> fileDataList,
    DecryptionOptions options,
    IProgress<EncryptionProgress>? progress,
    Stopwatch swProgress,
    ProgressState state,
    List<string> createdFiles,
    HashSet<string> createdDirs,
    CancellationToken cancellationToken)
  {
    // RSA暗号化の場合、ヘッダーに256バイトのRSAブロックが追加されている
    var rsaBlockSize = header.RsaBlock.Length > 0 ? 256 : 0;

    // 旧方式：暗号ストリーム先頭が 52(+RSA256) から始まり、平文として [atc4+indexData] が続いてから deflate 本体が来る
    fs.Position = header.HeaderOffset + PLAIN_HEADER_SIZE + rsaBlockSize;

    using var aes = Aes.Create();
    aes.KeySize = 256;
    aes.BlockSize = 128;
    aes.Mode = CipherMode.CBC;
    aes.Padding = PaddingMode.PKCS7;
    aes.Key = key;
    aes.IV = iv;

    await using var cryptoStream = new CryptoStream(fs, aes.CreateDecryptor(), CryptoStreamMode.Read, true);

    // 先頭の平文ヘッダー（atc4 + indexData）を捨てる
    await DiscardBytesAsync(cryptoStream, header.AtcHeaderSize, cancellationToken).ConfigureAwait(false);

    await using var deflateStream = new DeflateStream(cryptoStream, CompressionMode.Decompress, true);

    await ExtractFromDeflatedStreamAsync(
      deflateStream,
      outputDirFull,
      fileDataList,
      options,
      progress,
      swProgress,
      state,
      createdFiles,
      createdDirs,
      cancellationToken).ConfigureAwait(false);
  }

  // -------------------------
  // Deflate 展開してファイル出力
  // -------------------------
  private static async Task ExtractFromDeflatedStreamAsync(
    Stream deflatedStream,
    string outputDirFull,
    List<FileData> fileDataList,
    DecryptionOptions options,
    IProgress<EncryptionProgress>? progress,
    Stopwatch swProgress,
    ProgressState state,
    List<string> createdFiles,
    HashSet<string> createdDirs,
    CancellationToken cancellationToken)
  {
    var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
    try
    {
      foreach (var fd in fileDataList)
      {
        cancellationToken.ThrowIfCancellationRequested();
        state.ProcessedFiles++;

        var rel = NormalizeRelativePath(fd.RelativePath);
        var outPath = GetSafeOutputPath(outputDirFull, rel);

        if (outPath == null)
        {
          // 危険パスはデータだけ読み飛ばす
          await SkipEntryDataAsync(deflatedStream, fd, buffer, cancellationToken).ConfigureAwait(false);
          Report(progress, state, fd.RelativePath, Localize["Progress_SkippedUnsafePath"]);
          continue;
        }

        if (IsDirectory(fd.Attributes) || (fd.FileSize == 0 && outPath.EndsWith(Path.DirectorySeparatorChar)))
        {
          CreateDirectoryIfNeeded(outPath, createdDirs);
          if (options.RestoreTimeStamp) TrySetDirectoryTimesUtc(outPath, fd.CreationUtc, fd.LastWriteUtc);

          Report(progress, state, fd.RelativePath, null);
          continue;
        }

        // 親ディレクトリ作成
        var parent = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(parent)) CreateDirectoryIfNeeded(parent, createdDirs);

        // 0バイトファイル
        if (fd.FileSize <= 0)
        {
          var existed = File.Exists(outPath);
          await using (var outFs0 = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
          {
          }

          if (!existed) createdFiles.Add(outPath);

          if (options.RestoreTimeStamp) TrySetFileTimesUtc(outPath, fd.CreationUtc, fd.LastWriteUtc);
          TrySetFileAttributes(outPath, fd.Attributes);

          Report(progress, state, fd.RelativePath, null);
          continue;
        }

        // 通常ファイル
        var existedFile = File.Exists(outPath);
        await using (var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
          var remaining = fd.FileSize;
          while (remaining > 0)
          {
            cancellationToken.ThrowIfCancellationRequested();

            var toRead = remaining > buffer.Length ? buffer.Length : (int)remaining;
            var r = await deflatedStream.ReadAsync(buffer, 0, toRead, cancellationToken).ConfigureAwait(false);
            if (r <= 0) throw new EndOfStreamException(Localize["Error_TruncatedBody"]);

            await outFs.WriteAsync(buffer, 0, r, cancellationToken).ConfigureAwait(false);
            remaining -= r;

            state.ProcessedBytes += r;
            if (swProgress.ElapsedMilliseconds > 100)
            {
              Report(progress, state, fd.RelativePath, null);
              swProgress.Restart();
            }
          }
        }

        if (!existedFile) createdFiles.Add(outPath);

        if (options.RestoreTimeStamp) TrySetFileTimesUtc(outPath, fd.CreationUtc, fd.LastWriteUtc);
        TrySetFileAttributes(outPath, fd.Attributes);

        // MD5 検証（あれば）
        if (!options.SalvageIgnoreHashCheck && fd.Hash is { Length: 16 })
        {
          var computed = GetMd5Hash(outPath);
          if (computed != null && !computed.SequenceEqual(fd.Hash))
            // ここは「失敗」で止めず、情報として進捗に出す（Windows版に合わせて寛容に）
            Report(progress, state, fd.RelativePath, Localize["Error_Md5Mismatch"]);
        }

        Report(progress, state, fd.RelativePath, null);
      }
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buffer);
    }
  }

  private static async Task SkipEntryDataAsync(Stream deflatedStream, FileData fd, byte[] buffer, CancellationToken ct)
  {
    if (IsDirectory(fd.Attributes) || fd.FileSize <= 0) return;

    var remaining = fd.FileSize;
    while (remaining > 0)
    {
      ct.ThrowIfCancellationRequested();
      var toRead = remaining > buffer.Length ? buffer.Length : (int)remaining;
      var r = await deflatedStream.ReadAsync(buffer, 0, toRead, ct).ConfigureAwait(false);
      if (r <= 0) throw new EndOfStreamException(Localize["Error_TruncatedWhileSkipping"]);
      remaining -= r;
    }
  }

  // -------------------------
  // ファイルリスト（暗号化ヘッダー）復号＆解析
  // -------------------------
  private static async Task<List<FileData>?> ReadFileIndexAsync(
    FileStream fs,
    PlainHeader header,
    byte[] key,
    byte[] iv,
    CancellationToken cancellationToken)
  {
    if (header.AtcHeaderSize < 4 || header.AtcHeaderSize > 1024 * 1024 * 64) return null;

    // 暗号化ヘッダー開始位置（平文ヘッダー直後）
    var rsaBlockSize = header.RsaBlock.Length > 0 ? 256 : 0;
    fs.Position = header.HeaderOffset + PLAIN_HEADER_SIZE + rsaBlockSize;

    // Windows 版 AC4 は PKCS7、旧 Mac AC4 は Zeros でヘッダを暗号化しているため両方を試す。
    var basePos = fs.Position;
    byte[]? headerPlain = TryReadFileIndexHeader(fs, basePos, key, iv, header.AtcHeaderSize, PaddingMode.PKCS7);
    if (headerPlain == null || !IsAtc4Token(headerPlain))
    {
      headerPlain = TryReadFileIndexHeader(fs, basePos, key, iv, header.AtcHeaderSize, PaddingMode.Zeros);
      if (headerPlain == null) return null;
      if (!IsAtc4Token(headerPlain)) return null;
    }
    var readTotal = headerPlain.Length;

    // 4byte tokenの後ろがFileIndex本体
    using var ms = new MemoryStream(headerPlain, 4, readTotal - 4, false);
    using var br = new BinaryReader(ms, Encoding.UTF8, true);

    var list = new List<FileData>(1024);

    while (ms.Position < ms.Length)
    {
      cancellationToken.ThrowIfCancellationRequested();

      // FileNameSize (short)
      if (ms.Length - ms.Position < 2) break;
      var nameSize = br.ReadInt16();
      if (nameSize <= 0 || nameSize > 32767) break;

      if (ms.Length - ms.Position < nameSize) break;
      var pathBytes = br.ReadBytes(nameSize);
      if (pathBytes.Length != nameSize) break;

      var relPath = Encoding.UTF8.GetString(pathBytes);

      // FileSize (Int64)
      if (ms.Length - ms.Position < 8) break;
      var fileSize = br.ReadInt64();

      // Attr (int)
      if (ms.Length - ms.Position < 4) break;
      var attrInt = br.ReadInt32();

      // Time block (16 bytes) ＝ (date/time×4) または (ToBinary×2)
      if (ms.Length - ms.Position < 16) break;
      var timeBlock = br.ReadBytes(16);
      if (timeBlock.Length != 16) break;

      var lwUtc = DateTime.MinValue;
      var crUtc = DateTime.MinValue;

      var lwDate = BitConverter.ToInt32(timeBlock, 0);
      var lwTime = BitConverter.ToInt32(timeBlock, 4);
      var crDate = BitConverter.ToInt32(timeBlock, 8);
      var crTime = BitConverter.ToInt32(timeBlock, 12);

      if (TryParseYmdHmsUtc(lwDate, lwTime, out var lwParsed) && TryParseYmdHmsUtc(crDate, crTime, out var crParsed))
      {
        lwUtc = lwParsed;
        crUtc = crParsed;
      }
      else
      {
        var lwBin = BitConverter.ToInt64(timeBlock, 0);
        var crBin = BitConverter.ToInt64(timeBlock, 8);

        if (TryParseDateTimeBinaryUtc(lwBin, out var lwFromBin)) lwUtc = lwFromBin;
        if (TryParseDateTimeBinaryUtc(crBin, out var crFromBin)) crUtc = crFromBin;
      }

      // MD5（ある場合のみ 16 bytes）
      byte[]? md5 = null;
      if (fileSize > 0 && ms.Length - ms.Position >= 16)
      {
        var before = ms.Position;
        var candidate = br.ReadBytes(16);

        // 次のFileNameSizeの整合性で「MD5がある/ない」を判定（無い場合にズレるのを防ぐ）
        var accept = true;
        if (ms.Position < ms.Length)
          if (ms.Length - ms.Position >= 2)
          {
            var nextNameSize = BitConverter.ToInt16(headerPlain, 4 + (int)ms.Position);
            if (nextNameSize <= 0 || nextNameSize > 32767)
            {
              accept = false;
            }
            else
            {
              var remainAfter = ms.Length - ms.Position;
              if (remainAfter < 2 + nextNameSize + 8 + 4 + 16)
                // 以降に最低限の項目が入らないならおかしい
                // （ただし終端の場合もあるので、終端はOK）
                accept = ms.Position == ms.Length;
            }
          }

        if (accept)
          md5 = candidate;
        else
          // MD5が無い形式っぽいので巻き戻す
          ms.Position = before;
      }

      list.Add(new FileData
      {
        RelativePath = relPath,
        FileSize = fileSize,
        Attributes = (FileAttributes)attrInt,
        LastWriteUtc = lwUtc,
        CreationUtc = crUtc,
        Hash = md5
      });
    }

    return list;
  }

  // -------------------------
  // 平文ヘッダー読み取り
  // -------------------------
  private static bool TryReadPlainHeaderAt(FileStream fs, long offset, out PlainHeader header)
  {
    header = default;
    if (offset < 0 || fs.Length - offset < PLAIN_HEADER_SIZE) return false;

    fs.Position = offset;

    // まず52バイトを読み込む
    var buf = new byte[PLAIN_HEADER_SIZE];
    var read = 0;
    while (read < buf.Length)
    {
      var r = fs.Read(buf, read, buf.Length - read);
      if (r <= 0) return false;
      read += r;
    }

    var appVersion = BitConverter.ToInt16(buf, 0);
    var missTypeLimits = buf[2];
    var isBroken = buf[3] != 0;

    var tokenStr = Encoding.ASCII.GetString(buf, 4, 16);
    var isRsa = tokenStr.TrimEnd('\0') == STRING_TOKEN_RSA;

    var version = BitConverter.ToInt32(buf, 20);
    var atcHeaderSize = BitConverter.ToInt32(buf, 24);

    var guid = new byte[16];
    Buffer.BlockCopy(buf, 28, guid, 0, 16);

    var salt = new byte[8];
    Buffer.BlockCopy(buf, 44, salt, 0, 8);

    // RSAブロック（256バイト）を読み取る
    byte[] rsaBlock;
    if (isRsa)
    {
      // RSAの場合、追加で256バイトを読み込む
      rsaBlock = new byte[256];
      read = 0;
      while (read < rsaBlock.Length)
      {
        var r = fs.Read(rsaBlock, read, rsaBlock.Length - read);
        if (r <= 0) return false;
        read += r;
      }
    }
    else
    {
      rsaBlock = Array.Empty<byte>();
    }

    var tokenTrim = tokenStr.TrimEnd('\0');

    if (tokenTrim != STRING_TOKEN_NORMAL && tokenTrim != STRING_TOKEN_BROKEN &&
        tokenTrim != STRING_TOKEN_RSA) return false;

    if (version <= 0 || version > 10000) return false;
    if (atcHeaderSize < 4 || atcHeaderSize > 1024 * 1024 * 128) return false;

    header = new PlainHeader(offset, appVersion, missTypeLimits, isBroken, tokenStr, version, atcHeaderSize, guid, salt,
      rsaBlock);
    return true;
  }

  private static bool TryFindPlainHeader(FileStream fs, out PlainHeader header)
  {
    header = default;

    // 先頭〜64MBだけ簡易スキャン（自己実行形式などの救済）
    const int MAX_SCAN = 64 * 1024 * 1024;
    var scanLimit = Math.Min(fs.Length, MAX_SCAN);

    var needles = new[]
    {
      Encoding.ASCII.GetBytes(STRING_TOKEN_NORMAL),
      Encoding.ASCII.GetBytes(STRING_TOKEN_BROKEN),
      Encoding.ASCII.GetBytes(STRING_TOKEN_RSA)
    };

    var buf = ArrayPool<byte>.Shared.Rent(1024 * 1024);
    try
    {
      long pos = 0;
      var overlap = 64;

      // 先頭 overlap は0埋め
      Array.Clear(buf, 0, overlap);

      while (pos < scanLimit)
      {
        fs.Position = pos;
        var toRead = (int)Math.Min(buf.Length - overlap, scanLimit - pos);
        var r = fs.Read(buf, overlap, toRead);
        if (r <= 0) break;

        var windowLen = overlap + r;

        foreach (var needle in needles)
        {
          var idx = IndexOf(buf, 0, windowLen, needle);
          if (idx >= 0)
          {
            var tokenPos = pos + idx - overlap;
            var headerPos = tokenPos - 4; // AppVersion(2)+miss(1)+broken(1)
            if (TryReadPlainHeaderAt(fs, headerPos, out header)) return true;
          }
        }

        // 末尾 overlap を先頭へ
        Buffer.BlockCopy(buf, windowLen - overlap, buf, 0, overlap);
        pos += r;
      }
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buf);
    }

    return false;
  }

  private static int IndexOf(byte[] haystack, int start, int length, byte[] needle)
  {
    if (needle.Length == 0) return -1;
    var last = start + length - needle.Length;
    for (var i = start; i <= last; i++)
    {
      var ok = true;
      for (var j = 0; j < needle.Length; j++)
        if (haystack[i + j] != needle[j])
        {
          ok = false;
          break;
        }

      if (ok) return i;
    }

    return -1;
  }

  /// <summary>
  /// 暗号化ファイルに含まれるファイルリストを取得（復号せずにリストのみ）
  /// </summary>
  public async Task<FileListResult> GetFileListAsync(
      string inputPath,
      string password,
      CancellationToken cancellationToken = default)
  {
      try
      {
          if (!File.Exists(inputPath))
          {
              return FileListResult.CreateError(
                  ErrorCodes.FILE_NOT_FOUND,
                  Localize["Error_FileNotFound"]);
          }

          // パスワードの入力ミスが上限を超えて破壊されたファイル
          if (IsMarkedBroken(inputPath))
          {
              return FileListResult.CreateError(
                  ErrorCodes.ATC_BROKEN_DATA,
                  Localize["Error_FileAlreadyBroken"]);
          }

          // v150 (AC5) なら v5 用のリスト取得へ
          var detectedVersion = await DetectDataFileVersionAsync(inputPath, cancellationToken);
          if (detectedVersion >= DATA_FILE_VERSION_V5)
          {
              return GetFileListV5(inputPath, password);
          }

          var passwordBinary = Encoding.UTF8.GetBytes(password);

          await using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);

          // 平文ヘッダー読み取り
          if (!TryReadPlainHeaderAt(fs, 0, out var header))
          {
              if (!TryFindPlainHeader(fs, out header))
              {
                  return FileListResult.CreateError(
                      ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
                      Localize["Error_HeaderNotFound"]);
              }
          }

          if (header.DataFileVersion != DATA_FILE_VERSION)
          {
              return FileListResult.CreateError(
                  ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
                  Localize.GetString("Error_UnsupportedDataVersion", header.DataFileVersion));
          }

          // PBKDF2で鍵生成
          var (key, iv) = DeriveKeyAndIvV4(passwordBinary, header.Salt);

          // ファイルリスト取得
          var fileDataList = await ReadFileIndexAsync(
              fs,
              header,
              key,
              iv,
              cancellationToken).ConfigureAwait(false);

          if (fileDataList == null || fileDataList.Count == 0)
          {
              return FileListResult.CreateError(
                  ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
                  Localize["Error_WrongPasswordOrBroken"]);
          }

          // 相対パスのリストを返す
          var filePaths = fileDataList
              .Select(f => NormalizeRelativePath(f.RelativePath))
              .ToList();

          return FileListResult.CreateSuccess(filePaths);
      }
      catch (CryptographicException)
      {
          return FileListResult.CreateError(
              ErrorCodes.CRYPTOGRAPHIC_EXCEPTION,
              Localize["Error_WrongPassword"]);
      }
      catch (Exception ex)
      {
          return FileListResult.CreateError(
              ErrorCodes.ERROR_UNEXPECTED,
              ex.Message);
      }
  }

  /// <summary>
  /// 暗号化ファイルに含まれるファイルリストを取得（RSAモード対応）
  /// </summary>
  public async Task<FileListResult> GetFileListAsync(
      string inputPath,
      string password,
      DecryptionOptions options,
      CancellationToken cancellationToken = default)
  {
    try
    {
        if (!File.Exists(inputPath))
        {
            return FileListResult.CreateError(
                ErrorCodes.FILE_NOT_FOUND,
                Localize["Error_FileNotFound"]);
        }

        // パスワードの入力ミスが上限を超えて破壊されたファイル
        if (IsMarkedBroken(inputPath))
        {
            return FileListResult.CreateError(
                ErrorCodes.ATC_BROKEN_DATA,
                Localize["Error_FileAlreadyBroken"]);
        }

        // v150 (AC5) なら v5 用のリスト取得へ。
        // 公開鍵方式かパスワード方式かはヘッダの encryptionMode で判別される。
        // RSA 鍵ファイル（.atcpvt）が渡されていれば、その鍵で鍵保護ブロックを開く
        var detectedVersion = await DetectDataFileVersionAsync(inputPath, cancellationToken);
        if (detectedVersion >= DATA_FILE_VERSION_V5)
        {
            return GetFileListV5(inputPath, password, options.PasswordBinary,
                options.IsUseRsaEncryption ? options.RsaPrivateKeyXml : null);
        }

        byte[] passwordBinary;

        await using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // 平文ヘッダー読み取り
        if (!TryReadPlainHeaderAt(fs, 0, out var header))
        {
            if (!TryFindPlainHeader(fs, out header))
            {
                return FileListResult.CreateError(
                    ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
                    Localize["Error_HeaderNotFound"]);
            }
        }

        if (header.DataFileVersion != DATA_FILE_VERSION)
        {
            return FileListResult.CreateError(
                ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
                Localize.GetString("Error_UnsupportedDataVersion", header.DataFileVersion));
        }

        // RSAモードの場合
        if (options.IsUseRsaEncryption)
        {
            // 秘密鍵からGUIDを取得
            var xmlElement = XElement.Parse(options.RsaPrivateKeyXml);
            var privateKeyGuidString = xmlElement.Element("id")?.Value;
            if (string.IsNullOrEmpty(privateKeyGuidString))
            {
                return FileListResult.CreateError(
                    ErrorCodes.RSA_KEY_GUID_NOT_MATCH,
                    Localize["Error_PrivateKeyNoGuid"]);
            }
            var privateKeyGuid = Guid.Parse(privateKeyGuidString);

            // GUIDの比較
            var fileGuid = new Guid(header.Guid);
            if (fileGuid != privateKeyGuid)
            {
                return FileListResult.CreateError(
                    ErrorCodes.RSA_KEY_GUID_NOT_MATCH,
                    Localize["Error_RsaGuidMismatch"]);
            }

            // RSAブロックがあるか確認
            if (header.RsaBlock == null || header.RsaBlock.Length != 256)
            {
                return FileListResult.CreateError(
                    ErrorCodes.RSA_KEY_GUID_NOT_MATCH,
                    Localize["Error_RsaBlockMissing"]);
            }

            // RSAで復号してパスワードを取得
            // RSACryptoServiceProvider は Windows 専用実装。macOS では RSA.Create() を使う
            using var rsa = RSA.Create();
            rsa.FromXmlString(options.RsaPrivateKeyXml);

            try
            {
                passwordBinary = rsa.Decrypt(header.RsaBlock, RSAEncryptionPadding.OaepSHA1);
            }
            catch (CryptographicException)
            {
                return FileListResult.CreateError(
                    ErrorCodes.CRYPTOGRAPHIC_EXCEPTION,
                    Localize["Error_RsaDecryptFailed"]);
            }
        }
        else
        {
            // パスワードファイル（/dpfile）が指定されているときは、その鍵素材を優先する
            passwordBinary = options.PasswordBinary ?? Encoding.UTF8.GetBytes(password);
        }

        // PBKDF2で鍵生成
        var (key, iv) = DeriveKeyAndIvV4(passwordBinary, header.Salt);

        // ファイルリスト取得
        var fileDataList = await ReadFileIndexAsync(
            fs,
            header,
            key,
            iv,
            cancellationToken).ConfigureAwait(false);

        if (fileDataList == null || fileDataList.Count == 0)
        {
            return FileListResult.CreateError(
                ErrorCodes.PASSWORD_TOKEN_NOT_FOUND,
                Localize["Error_WrongPasswordOrBroken"]);
        }

        // 相対パスのリストを返す
        var filePaths = fileDataList
            .Select(f => NormalizeRelativePath(f.RelativePath))
            .ToList();

        return FileListResult.CreateSuccess(filePaths);
    }
    catch (CryptographicException)
    {
        return FileListResult.CreateError(
            ErrorCodes.CRYPTOGRAPHIC_EXCEPTION,
            Localize["Error_WrongPassword"]);
    }
    catch (Exception ex)
    {
        return FileListResult.CreateError(
            ErrorCodes.ERROR_UNEXPECTED,
            ex.Message);
    }
  }

  // -------------------------
  // ユーティリティ
  // -------------------------
  private static bool IsDirectory(FileAttributes attrs)
  {
    return (attrs & FileAttributes.Directory) != 0;
  }

  private static bool IsAtc4Token(byte[] headerPlain)
  {
    if (headerPlain.Length < 4) return false;
    return headerPlain[0] == (byte)'a' && headerPlain[1] == (byte)'t'
        && headerPlain[2] == (byte)'c' && headerPlain[3] == (byte)'4';
  }

  /// <summary>
  /// 指定されたパディングモードでファイルインデックス（暗号化ヘッダ）を読み取る。
  /// Windows AC4 は PKCS7、旧 Mac AC4 は Zeros で暗号化されている可能性があるため両方試す。
  /// 暗号文をいったんバッファに読み込んで AES-CBC で復号する（CryptoStream 経由ではないので
  /// 部分読み出しの挙動差に依存しない）。
  /// </summary>
  private static byte[]? TryReadFileIndexHeader(
      FileStream fs, long basePos, byte[] key, byte[] iv, int atcHeaderSize, PaddingMode padding)
  {
    try
    {
      // 必要な暗号文長を計算（CBC-PKCS7 は常に 1〜16 バイトのパディングが付くため
      // 16 の倍数 +16 まで取り得る; Zeros は 0〜15 のパディングのみ）。
      // ここでは安全側に最大ブロック分多めに読む。
      var blocks = (atcHeaderSize / 16) + (atcHeaderSize % 16 == 0 ? 1 : 1);
      var cipherLen = blocks * 16;
      if (fs.Length - basePos < cipherLen) cipherLen = (int)(fs.Length - basePos);
      // 16 の倍数に切り詰め
      cipherLen = (cipherLen / 16) * 16;
      if (cipherLen < 16) return null;

      fs.Position = basePos;
      var cipherBuf = new byte[cipherLen];
      var read = 0;
      while (read < cipherBuf.Length)
      {
        var r = fs.Read(cipherBuf, read, cipherBuf.Length - read);
        if (r <= 0) break;
        read += r;
      }
      if (read < 16) return null;

      using var aes = Aes.Create();
      aes.KeySize = 256;
      aes.BlockSize = 128;
      aes.Mode = CipherMode.CBC;
      aes.Padding = padding;
      aes.Key = key;
      aes.IV = iv;
      using var decryptor = aes.CreateDecryptor();

      // 一気に復号
      byte[] plain;
      try
      {
        plain = decryptor.TransformFinalBlock(cipherBuf, 0, read);
      }
      catch (CryptographicException)
      {
        return null;
      }

      // plain は atcHeaderSize 以上の長さを持つはず（PKCS7 でも Zeros でも）
      if (plain.Length < 4) return null;
      // 期待するヘッダサイズに切り詰め
      if (plain.Length > atcHeaderSize)
      {
        var trimmed = new byte[atcHeaderSize];
        Buffer.BlockCopy(plain, 0, trimmed, 0, atcHeaderSize);
        return trimmed;
      }
      return plain;
    }
    catch
    {
      return null;
    }
  }

  private static long GetCipherLengthPkcs7Style(int plainLen)
  {
    var mod = plainLen % 16;
    var pad = 16 - mod;
    if (pad == 0) pad = 16;
    return (long)plainLen + pad;
  }

  private static long GetCipherLengthZerosPadding(long plainLen)
  {
    var mod = plainLen % 16;
    if (mod == 0) return plainLen; // 既に16の倍数ならパディングなし
    return plainLen + (16 - mod);
  }

  private static async Task DiscardBytesAsync(Stream s, int bytes, CancellationToken ct)
  {
    if (bytes <= 0) return;

    var buf = ArrayPool<byte>.Shared.Rent(81920);
    try
    {
      var remaining = bytes;
      while (remaining > 0)
      {
        ct.ThrowIfCancellationRequested();
        var toRead = remaining > buf.Length ? buf.Length : remaining;
        var r = await s.ReadAsync(buf, 0, toRead, ct).ConfigureAwait(false);
        if (r <= 0) throw new EndOfStreamException(Localize["Error_SkipHeaderFailed"]);
        remaining -= r;
      }
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buf);
    }
  }

  private static string NormalizeRelativePath(string p)
  {
    // インデックスは Windows区切りが多いので両対応
    p = p.Replace('/', '\\').TrimStart('\\');
    return p.Replace('\\', Path.DirectorySeparatorChar);
  }

  private static string EnsureTrailingSeparator(string path)
  {
    if (string.IsNullOrEmpty(path)) return path;
    return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
  }

  private static StringComparer GetPathComparer()
  {
    return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
  }

  private static string? GetSafeOutputPath(string outputDirFull, string relativePath)
  {
    if (string.IsNullOrWhiteSpace(relativePath)) return outputDirFull;

    // ":" を含むとWindowsドライブ指定など危険
    if (relativePath.Contains(':')) return null;

    string combined;
    try
    {
      combined = Path.Combine(outputDirFull, relativePath);
    }
    catch
    {
      return null;
    }

    string full;
    try
    {
      full = Path.GetFullPath(combined);
    }
    catch
    {
      return null;
    }

    var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    return !full.StartsWith(outputDirFull, cmp) ? null : full;
  }

  /// <summary>
  /// 上書きしない指定（Skip / SkipAll ＝ 確認ダイアログを出せない実行での「上書きを確認する」）のとき、
  /// 書庫の一覧のうち保存先に既にあるファイルを探す。暗号化側が出力先の同名ファイルで
  /// 何も書かずに終了コード 5 で止まるのと同じく、復号も 1 つでもあれば書き出しを始める前に止める
  /// （黙って飛ばすと「復号成功」に見えてしまう）。該当が無ければ null。
  /// GUI（Ask）には効かない（復号画面が自前で確認している）。
  /// </summary>
  private static string? FindExistingOutputFile(
    string outputDirFull, List<FileData> fileDataList, DecryptionOptions options)
  {
    if (options.OverwriteOption is not (OverwriteOption.Skip or OverwriteOption.SkipAll)) return null;

    foreach (var fd in fileDataList)
    {
      if (IsDirectory(fd.Attributes)) continue;
      var outPath = GetSafeOutputPath(outputDirFull, NormalizeRelativePath(fd.RelativePath));
      if (outPath == null || outPath.EndsWith(Path.DirectorySeparatorChar)) continue;
      if (File.Exists(outPath)) return outPath;
    }

    return null;
  }

  private static EncryptionResult OutputFileExistsError(string path)
    => EncryptionResult.CreateError(
      ErrorCodes.OUTPUT_FILE_EXISTS,
      Localize.GetString("Error_OutputFileExists", path),
      path);

  private static void CreateDirectoryIfNeeded(string path, HashSet<string> createdDirs)
  {
    if (string.IsNullOrEmpty(path)) return;

    // ディレクトリ項目は末尾区切りがつく場合があるので正規化
    path = path.TrimEnd(Path.DirectorySeparatorChar);

    if (Directory.Exists(path)) return;
    Directory.CreateDirectory(path);
    createdDirs.Add(Path.GetFullPath(path));
  }

  /// <summary>
  /// v5 復号で書き出し中・検証待ちの出力。<see cref="TempPath"/> に実体があり、
  /// 本体全体のタグ検証を通ってから <see cref="FinalPath"/> へ移す。
  /// </summary>
  /// <param name="ReplacesExisting">最終パスに既存ファイルがあった（置き換えになる）か</param>
  private sealed record PendingOutput(string TempPath, string FinalPath, bool ReplacesExisting);

  /// <summary>検証待ちの出力を書く一時ファイルのパス。最終パスと同じフォルダーに作る
  /// （別ボリュームだと最終パスへの移動が原子的でなくなるため）。</summary>
  /// 名前は最終名から作らず GUID だけにする（最終名に文字を足すと、ファイル名長の上限に近い
  /// 名前で「最終名なら作れたのに一時ファイルが作れない」失敗が起きる。CodeRabbit PR#21）。
  private static string MakeTempOutputPath(string finalPath)
    => Path.Combine(Path.GetDirectoryName(finalPath) ?? string.Empty, Guid.NewGuid().ToString("N") + ".attmp");

  /// <summary>
  /// 作りかけの出力（検証待ちの一時ファイルと、この復号で作ったフォルダー）を片付ける。
  /// </summary>
  /// <returns>消せずに残ったパス（利用者に知らせて手で消してもらう）</returns>
  private static List<string> CleanupPartialOutputs(List<PendingOutput> pending, HashSet<string> createdDirs)
  {
    var leftovers = CleanupTempFiles(pending);
    leftovers.AddRange(CleanupCreatedPaths([], createdDirs));
    return leftovers;
  }

  /// <summary>検証待ちの一時ファイルを消す（最終パスへ移し終えたものは対象外）。</summary>
  /// <returns>消せずに残った一時ファイルのパス</returns>
  private static List<string> CleanupTempFiles(List<PendingOutput> pending)
  {
    var leftovers = new List<string>();
    foreach (var output in pending)
    {
      var tmp = output.TempPath;
      try
      {
        if (!File.Exists(tmp)) continue;
        File.SetAttributes(tmp, FileAttributes.Normal); // ReadOnly 属性を写していても消せるように
        File.Delete(tmp);
      }
      catch (Exception)
      {
        // 一時ファイルが残るだけで、実データは壊していない。場所だけ伝える
      }
      if (File.Exists(tmp)) leftovers.Add(tmp);
    }
    return leftovers;
  }

  /// <summary>
  /// この復号で新しく作ったファイルとフォルダーを消す（旧形式の経路と、v5 のフォルダー）。
  /// </summary>
  /// <returns>消せずに残ったパス</returns>
  private static List<string> CleanupCreatedPaths(List<string> createdFiles, HashSet<string> createdDirs)
  {
    var leftovers = new List<string>();
    for (var i = createdFiles.Count - 1; i >= 0; i--)
    {
      var path = createdFiles[i];
      try
      {
        if (!File.Exists(path)) continue;
        File.SetAttributes(path, FileAttributes.Normal); // 書庫の ReadOnly 属性を写していても消せるように
        File.Delete(path);
      }
      catch (Exception)
      {
        // 消せないものは残し、場所だけ伝える
      }
      if (File.Exists(path)) leftovers.Add(path);
    }

    var dirs = createdDirs.OrderByDescending(d => d.Length).ToList();
    foreach (var d in dirs)
    {
      try
      {
        if (Directory.Exists(d)) Directory.Delete(d, true);
      }
      catch (Exception)
      {
      }
      if (Directory.Exists(d)) leftovers.Add(d);
    }

    createdFiles.Clear();
    createdDirs.Clear();
    return leftovers;
  }

  /// <summary>例外に添えた「消せなかった残骸」を取り出すためのキー（<see cref="Exception.Data"/>）</summary>
  private const string LeftoverPathsDataKey = "AttacheCase.DecryptionService.LeftoverPaths";

  /// <summary>
  /// 片付けきれなかった作りかけの出力を例外に添える。呼び出し元が例外を結果へ写すときに
  /// <see cref="WithLeftovers(EncryptionResult, Exception)"/> で結果に載せる
  /// （例外の種類を変えないので、キャンセル・暗号例外の写像はそのまま）。
  /// </summary>
  private static void AttachLeftovers(Exception ex, List<string> leftovers)
  {
    if (leftovers.Count > 0) ex.Data[LeftoverPathsDataKey] = leftovers;
  }

  private static EncryptionResult WithLeftovers(EncryptionResult result, Exception ex)
    => ex.Data[LeftoverPathsDataKey] is List<string> leftovers ? WithLeftovers(result, leftovers) : result;

  /// <summary>
  /// 消せなかった作りかけの出力（認証前の平文）を結果に載せる。黙って残すと利用者が
  /// 気づけないので、メッセージにも場所を書く。
  /// </summary>
  private static EncryptionResult WithLeftovers(EncryptionResult result, List<string> leftovers)
  {
    if (leftovers.Count == 0) return result;
    result.LeftoverPaths = leftovers;
    result.ErrorMessage += Environment.NewLine +
      Localize.GetString("Error_LeftoverFiles", string.Join(Environment.NewLine, leftovers));
    return result;
  }

  private static void TrySetFileAttributes(string path, FileAttributes attrs)
  {
    try
    {
      // Directoryフラグはファイルにセットしない
      var normalized = attrs & ~FileAttributes.Directory;
      File.SetAttributes(path, normalized);
    }
    catch
    {
    }
  }

  private static void TrySetFileTimesUtc(string path, DateTime creationUtc, DateTime lastWriteUtc)
  {
    try
    {
      if (creationUtc != DateTime.MinValue)
        File.SetCreationTimeUtc(path, EnsureUtc(creationUtc));
    }
    catch
    {
    }

    try
    {
      if (lastWriteUtc != DateTime.MinValue)
        File.SetLastWriteTimeUtc(path, EnsureUtc(lastWriteUtc));
    }
    catch
    {
    }
  }

  private static void TrySetDirectoryTimesUtc(string path, DateTime creationUtc, DateTime lastWriteUtc)
  {
    try
    {
      if (creationUtc != DateTime.MinValue)
        Directory.SetCreationTimeUtc(path, EnsureUtc(creationUtc));
    }
    catch
    {
    }

    try
    {
      if (lastWriteUtc != DateTime.MinValue)
        Directory.SetLastWriteTimeUtc(path, EnsureUtc(lastWriteUtc));
    }
    catch
    {
    }
  }

  private static DateTime EnsureUtc(DateTime dt)
  {
    return dt.Kind switch
    {
      DateTimeKind.Utc => dt,
      DateTimeKind.Local => dt.ToUniversalTime(),
      _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
    };
  }

  private static void Report(IProgress<EncryptionProgress>? progress, ProgressState state, string current,
    string? status)
  {
    progress?.Report(new EncryptionProgress
    {
      Type = ProcessType.Decrypting,
      CurrentFilePath = current,
      ProcessedFiles = state.ProcessedFiles,
      TotalFiles = state.TotalFiles,
      ProgressPercentage = state.TotalBytes > 0 ? (int)(state.ProcessedBytes * 10000 / state.TotalBytes) : 0,
      StatusMessage = status ?? Localize["Progress_Decrypting"]
    });
  }

  private static byte[]? GetMd5Hash(string filePath)
  {
    try
    {
      using var f = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
      using var md5 = MD5.Create();
      return md5.ComputeHash(f);
    }
    catch
    {
      return null;
    }
  }

  private static bool TryParseYmdHmsUtc(int yyyymmdd, int hhmmss, out DateTime utc)
  {
    utc = DateTime.MinValue;

    if (yyyymmdd < 10101 || yyyymmdd > 99991231) return false;
    if (hhmmss < 0 || hhmmss > 235959) return false;

    var year = yyyymmdd / 10000;
    var month = yyyymmdd / 100 % 100;
    var day = yyyymmdd % 100;

    var hour = hhmmss / 10000;
    var minute = hhmmss / 100 % 100;
    var second = hhmmss % 100;

    try
    {
      utc = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static bool TryParseDateTimeBinaryUtc(long binary, out DateTime utc)
  {
    utc = DateTime.MinValue;
    try
    {
      var dt = DateTime.FromBinary(binary);
      utc = EnsureUtc(dt);
      return utc.Year is >= 1601 and <= 9999;
    }
    catch
    {
      return false;
    }
  }

  private struct FileData
  {
    public string RelativePath;
    public long FileSize;
    public FileAttributes Attributes;
    public DateTime LastWriteUtc;
    public DateTime CreationUtc;
    public byte[]? Hash;
  }

  /// <summary>
  /// 「親フォルダーを作らない」(/nopfldr)。
  /// 先頭エントリがディレクトリ（書庫名のフォルダー）のとき、そのエントリを外し、
  /// 以降のエントリ名から前置分を取り除く。Windows 版 FileDecrypt4/5 の
  /// fNoParentFolder と同じ解釈。先頭がファイルなら何もしない。
  /// ディレクトリエントリはデータ本体を持たないため、外しても読み取り位置はずれない。
  /// </summary>
  private static void ApplyNoParentFolder(List<FileData> fileDataList)
  {
    if (fileDataList.Count == 0) return;

    var parentPrefix = fileDataList[0].RelativePath;
    if (!parentPrefix.EndsWith('\\') && !parentPrefix.EndsWith('/')) return;

    for (var i = 1; i < fileDataList.Count; i++)
    {
      if (!fileDataList[i].RelativePath.StartsWith(parentPrefix, StringComparison.Ordinal)) continue;
      var fd = fileDataList[i];
      fd.RelativePath = fd.RelativePath.Substring(parentPrefix.Length);
      fileDataList[i] = fd;
    }

    fileDataList.RemoveAt(0);
  }

  private readonly struct PlainHeader(
    long headerOffset,
    short appVersion,
    byte missTypeLimits,
    bool isBroken,
    string tokenStr,
    int dataFileVersion,
    int atcHeaderSize,
    byte[] guid,
    byte[] salt,
    byte[] rsaBlock)
  {
    public long HeaderOffset { get; } = headerOffset;
    public short AppVersion { get; } = appVersion;
    public byte MissTypeLimits { get; } = missTypeLimits;
    public bool IsBroken { get; } = isBroken;
    public string TokenStr { get; } = tokenStr;
    public int DataFileVersion { get; } = dataFileVersion;
    public int AtcHeaderSize { get; } = atcHeaderSize;
    public byte[] Guid { get; } = guid;
    public byte[] Salt { get; } = salt;
    public byte[] RsaBlock { get; } = rsaBlock;
  }

  private sealed class ProgressState(long totalBytes, int totalFiles)
  {
    public long TotalBytes { get; } = totalBytes;
    public int TotalFiles { get; } = totalFiles;

    public long ProcessedBytes { get; set; }
    public int ProcessedFiles { get; set; }

    /// <summary>書き出し後の CRC32 検証で不一致だったファイル数。</summary>
    public int HashMismatchCount { get; set; }
  }
}

/// <summary>v150 の公開鍵方式の種類（<see cref="DecryptionService.GetPublicKeyEncryptionKind"/>）</summary>
public enum PublicKeyEncryptionKind
{
  /// <summary>公開鍵方式ではない（パスワード方式、または v150 以外・読めない）</summary>
  None,
  /// <summary>RSA 鍵ファイル（.atcpub / .atcpvt）方式。復号には秘密鍵ファイルが要る</summary>
  RsaKeyFile,
  /// <summary>Post-PPAP（鍵サーバー方式）。登録済みアカウントで復号する</summary>
  PostPpap
}
