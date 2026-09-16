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
// AC5 暗号化サービス（macOS / Avalonia 版）。
//   - 平文ヘッダ + 鍵保護ブロック + 暗号化ヘッダ + 暗号化本体
//   - AES-256-GCM + Argon2id (パスワード方式)
//   - DATA_FILE_VERSION = 150
//----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using AttacheCase.Crypto;
using AttacheCase.Models;
using Org.BouncyCastle.Crypto.Parameters;

namespace AttacheCase.Services;

public class EncryptionService : IEncryptionService
{
    /// <summary>表示文字列（利用者に見えるメッセージは必ずここを通す）</summary>
    private static readonly LocalizationService Localize = LocalizationService.Instance;

    private const int BUFFER_SIZE = 8192;
    private const string STRING_TOKEN_NORMAL = "_AttacheCaseData";

    private const int DATA_FILE_VERSION = 150;

    private const byte ENCRYPTION_MODE_PASSWORD = 0x00;
    private const byte ENCRYPTION_MODE_PUBLIC_KEY = 0x01;
    private const byte SYMMETRIC_ALGORITHM_AES256GCM = 0x01;
    private const byte KDF_ALGORITHM_NONE = 0x00;
    private const byte KDF_ALGORITHM_ARGON2ID = 0x01;

    public async Task<EncryptionResult> EncryptAsync(
        string[] inputPaths,
        string outputPath,
        string password,
        EncryptionOptions options,
        IProgress<EncryptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var usePublicKey = options.Recipients is { Count: > 0 };

            // RSA 鍵ファイル（.atcpub）方式。5.0 で一度畳んだが 5.5 で復活（Crypto/RsaKeyFile 参照）。
            // 公開鍵 XML を RSA 単体の宛先へ変換し、Post-PPAP と同じ鍵保護ブロックで書く。
            // ここで変換できなければ止める。素通りさせると RsaPublicKeyXml が使われないまま
            // 空パスワードで暗号化されてしまう
            if (!usePublicKey && options.IsUseRsaEncryption)
            {
                try
                {
                    options.Recipients.Add(RsaKeyFile.CreateRecipient(options.RsaPublicKeyXml));
                }
                catch (RsaKeyFile.UnsupportedKeySizeException ex)
                {
                    return EncryptionResult.CreateError(
                        ErrorCodes.ERROR_UNEXPECTED,
                        Localize.GetString("Error_RsaKeyFileUnsupportedSize", ex.Bits));
                }
                catch (FormatException ex)
                {
                    return EncryptionResult.CreateError(
                        ErrorCodes.ERROR_UNEXPECTED,
                        Localize.GetString("Error_RsaKeyFileInvalid", ex.Message));
                }
                usePublicKey = true;
            }

            // 公開鍵方式ではパスワード不要。それ以外はパスワード必須だが、
            // 空パスワード（""）も Phase 1 から許容している（往復は可能）。
            // パスワードファイル（/pfile）が指定されているときは、その鍵素材を優先する
            byte[]? passwordBinary = usePublicKey
                ? null
                : options.PasswordBinary ?? Encoding.UTF8.GetBytes(password ?? string.Empty);

            var absoluteOutputPath = Path.GetFullPath(outputPath);

            var fileList = await Task.Run(() =>
                CreateFileList(inputPaths, absoluteOutputPath, progress, cancellationToken), cancellationToken);

            if (cancellationToken.IsCancellationRequested) return EncryptionResult.CreateCancelled();

            // 重い同期処理（Deflate / GCM / FlushAsync）が UI スレッドを掴まないように
            // バックグラウンドスレッドへオフロード。Progress<T> は VM 側で UI スレッドに戻る。
            var result = await Task.Run(() => EncryptFilesV5Async(
                fileList,
                outputPath,
                passwordBinary,
                options,
                progress,
                cancellationToken), cancellationToken);

            // 設定「暗号化ファイルのタイムスタンプを元ファイルに合わせる」（/orgdt）。
            // 旧 Windows 版（FileEncrypt5）と同じく、最初の入力の日時を出力ファイルへ写す。
            // 中身（ZIP のエントリ日時に相当するもの）ではなく、書庫ファイル自体の日時
            if (result.Success && options.KeepTimeStamp)
            {
                ApplyOriginalTimeStamp(inputPaths[0],
                    string.IsNullOrEmpty(result.OutputFilePath) ? absoluteOutputPath : result.OutputFilePath);
            }

            sw.Stop();
            result.ProcessingTime =
                $"{sw.Elapsed.Hours}h{sw.Elapsed.Minutes}m{sw.Elapsed.Seconds}s{sw.Elapsed.Milliseconds}ms";

            return result;
        }
        catch (OperationCanceledException)
        {
            return EncryptionResult.CreateCancelled();
        }
        catch (Exception ex)
        {
            return EncryptionResult.CreateError(ErrorCodes.ERROR_UNEXPECTED, ex.Message);
        }
    }

    // =====================================================================
    // ファイルリスト構築
    // =====================================================================
    private List<FileSystemEntry> CreateFileList(
        string[] paths,
        string excludeFilePath,
        IProgress<EncryptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<FileSystemEntry>();

        progress?.Report(new EncryptionProgress
        {
            Type = ProcessType.Preparing,
            StatusMessage = Localize["Progress_BuildingFileList"]
        });

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(path))
            {
                ProcessDirectory(path, path, results, excludeFilePath, progress, cancellationToken);
            }
            else if (File.Exists(path))
            {
                var fullPath = Path.GetFullPath(path);
                var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (fullPath.Equals(excludeFilePath, cmp)) continue;

                var entry = GetFileInfoSingle(path, () => cancellationToken.IsCancellationRequested);
                if (entry != null) results.Add(entry);
            }
        }

        return results.OrderBy(e => e.RelativePath).ToList();
    }

    private void ProcessDirectory(
        string parentPath,
        string currentPath,
        ConcurrentBag<FileSystemEntry> results,
        string excludeFilePath,
        IProgress<EncryptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var dirEntry = GetDirectoryInfo(parentPath, currentPath);
            if (dirEntry != null) results.Add(dirEntry);

            foreach (var dir in Directory.GetDirectories(currentPath))
                ProcessDirectory(parentPath, dir, results, excludeFilePath, progress, cancellationToken);

            var files = Directory.GetFiles(currentPath);
            Parallel.ForEach(files, new ParallelOptions { CancellationToken = cancellationToken }, file =>
            {
                var fullPath = Path.GetFullPath(file);
                var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (fullPath.Equals(excludeFilePath, cmp)) return;

                var entry = GetFileInfo(parentPath, file, () => cancellationToken.IsCancellationRequested);
                if (entry != null)
                {
                    results.Add(entry);
                    progress?.Report(new EncryptionProgress
                    {
                        Type = ProcessType.Preparing,
                        StatusMessage = Localize.GetString("Progress_ReadingFileInfo", results.Count)
                    });
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            // アクセス権限がないディレクトリはスキップ
        }
    }

    // =====================================================================
    // v150 暗号化メイン
    // =====================================================================
    private async Task<EncryptionResult> EncryptFilesV5Async(
        List<FileSystemEntry> fileList,
        string outputPath,
        byte[]? passwordBinary,
        EncryptionOptions options,
        IProgress<EncryptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalFileSize = fileList.Where(f => !f.IsDirectory).Sum(f => f.Size);
        var usePublicKey = options.Recipients is { Count: > 0 };

        // ① ランダム共通鍵の生成
        var commonKey = CryptoHelper5.GenerateCommonKey();
        var success = false;

        try
        {
            await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

            // ①' 自己実行形式: 復号プログラム（Exeout スタブ）を先頭に書く。
            // スタブは本体に byte[] で埋め込まれている（Exeout48.cs の rawData。
            // ビルド時に tools\ExeToHex が Exeout.exe を差し込む）
            if (options.CreateExecutable)
            {
                WriteExecutableStub(fs);
            }

            // ② 平文ヘッダの書き込み（32 バイト）
            byte encryptionMode;
            byte pubkeyAlgorithm;
            byte kdfAlgorithm;

            if (usePublicKey)
            {
                encryptionMode = ENCRYPTION_MODE_PUBLIC_KEY;
                // ヘッダの公開鍵アルゴリズム識別子は「先頭受信者のもの」を採用する
                // （複数受信者のとき全員同じアルゴリズムを使う運用想定。鍵保護ブロック側で
                //  受信者ごとのアルゴリズムは個別に符号化されている）。
                pubkeyAlgorithm = options.Recipients[0].AlgorithmId;
                kdfAlgorithm = KDF_ALGORITHM_NONE;
            }
            else
            {
                encryptionMode = ENCRYPTION_MODE_PASSWORD;
                pubkeyAlgorithm = PkaAlgorithmId.None;
                kdfAlgorithm = KDF_ALGORITHM_ARGON2ID;
            }

            WriteV5PlainHeader(fs, options, encryptionMode, pubkeyAlgorithm, kdfAlgorithm);

            // ③ 暗号化ヘッダサイズのプレースホルダ（4 バイト）— 後で書き戻す
            var headerSizePos = fs.Position;
            fs.Write(BitConverter.GetBytes(0), 0, 4);

            // ④ 鍵保護ブロック
            if (usePublicKey)
            {
                var recipients = BuildRecipientList(options.Recipients);
                var pkBlock = CryptoHelper5.BuildPublicKeyProtectionBlock(commonKey, recipients);
                fs.Write(pkBlock, 0, pkBlock.Length);
            }
            else
            {
                var salt = CryptoHelper5.GenerateSalt();
                var derivedKey = CryptoHelper5.DeriveKeyFromPasswordBinary(
                    passwordBinary!, salt,
                    CryptoHelper5.ARGON2_DEFAULT_MEMORY_KB,
                    CryptoHelper5.ARGON2_DEFAULT_ITERATIONS,
                    CryptoHelper5.ARGON2_DEFAULT_PARALLELISM);
                var protectedKey = CryptoHelper5.ProtectCommonKeyWithPassword(commonKey, derivedKey);
                CryptoHelper5.SecureClear(derivedKey);

                // Salt(16) + Memory(4) + Iter(4) + Para(4) + EncKeySize(2) + EncKey(60)
                fs.Write(salt, 0, CryptoHelper5.ARGON2_SALT_SIZE);
                fs.Write(BitConverter.GetBytes(CryptoHelper5.ARGON2_DEFAULT_MEMORY_KB), 0, 4);
                fs.Write(BitConverter.GetBytes(CryptoHelper5.ARGON2_DEFAULT_ITERATIONS), 0, 4);
                fs.Write(BitConverter.GetBytes(CryptoHelper5.ARGON2_DEFAULT_PARALLELISM), 0, 4);
                fs.Write(BitConverter.GetBytes((short)protectedKey.Length), 0, 2);
                fs.Write(protectedKey, 0, protectedKey.Length);
            }

            // ⑤ ファイルインデックスの構築（平文）
            var indexData = CreateFileIndex(fileList, options.ArchiveName);

            // ⑥ ヘッダ暗号化用 Nonce
            var headerNonce = CryptoHelper5.GenerateNonce();
            fs.Write(headerNonce, 0, CryptoHelper5.AES_GCM_NONCE_SIZE);

            // ⑦ ファイルインデックスを AES-GCM で暗号化して書き込み
            var encryptedHeader = CryptoHelper5.AesGcmEncrypt(indexData, commonKey, headerNonce);
            var atcHeaderSize = encryptedHeader.Length;
            fs.Write(encryptedHeader, 0, atcHeaderSize);

            // 暗号化ヘッダサイズを書き戻す
            var savedPos = fs.Position;
            fs.Seek(headerSizePos, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(atcHeaderSize), 0, 4);
            fs.Seek(savedPos, SeekOrigin.Begin);

            // ⑧ 本体暗号化用 Nonce
            var bodyNonce = CryptoHelper5.GenerateNonce();
            fs.Write(bodyNonce, 0, CryptoHelper5.AES_GCM_NONCE_SIZE);

            // ⑨ 本体ストリーム: fs ← GcmEncryptingStream ← DeflateStream ← ファイル群
            long processedSize = 0;
            var fileIndex = 0;

            await using (var gcmStream = new GcmEncryptingStream(
                             fs, commonKey, bodyNonce, leaveOpen: true,
                             progressCallback: _ => !cancellationToken.IsCancellationRequested))
            await using (var deflateStream = new DeflateStream(gcmStream, options.CompressionLevel, true))
            {
                foreach (var entry in fileList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.IsDirectory || entry.Size == 0)
                    {
                        fileIndex++;
                        continue;
                    }

                    progress?.Report(new EncryptionProgress
                    {
                        Type = ProcessType.Encrypting,
                        CurrentFilePath = entry.FullPath,
                        ProcessedFiles = fileIndex,
                        TotalFiles = fileList.Count,
                        ProgressPercentage = totalFileSize > 0 ? (int)((double)processedSize / totalFileSize * 10000) : 0,
                        StatusMessage = Localize.GetString("Progress_EncryptingFile", Path.GetFileName(entry.FullPath))
                    });

                    await using var src = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var buf = new byte[BUFFER_SIZE];
                    // 索引（CreateFileIndex）には列挙時点の entry.Size が入る。復号側は
                    // その FileSize を信頼して本体を順に切り出すため、列挙後にファイルが
                    // 伸縮すると 1 件のずれで以降の全ファイルが壊れる。書き込みは
                    // entry.Size で厳密に打ち切り、足りなければエラーにする
                    var remaining = entry.Size;
                    var crcState = Crc32Utility.InitialState;
                    int n;
                    while (remaining > 0 &&
                           (n = await src.ReadAsync(
                               buf.AsMemory(0, (int)Math.Min(BUFFER_SIZE, remaining)),
                               cancellationToken)) > 0)
                    {
                        deflateStream.Write(buf, 0, n);
                        crcState = Crc32Utility.Update(crcState, buf, 0, n);
                        remaining -= n;
                        processedSize += n;
                        if (processedSize % (BUFFER_SIZE * 10) == 0)
                            progress?.Report(new EncryptionProgress
                            {
                                Type = ProcessType.Encrypting,
                                CurrentFilePath = entry.FullPath,
                                ProcessedFiles = fileIndex,
                                TotalFiles = fileList.Count,
                                ProgressPercentage = totalFileSize > 0
                                    ? (int)((double)processedSize / totalFileSize * 10000)
                                    : 0
                            });
                    }

                    if (remaining != 0)
                        throw new IOException(
                            $"File size changed during encryption: {entry.FullPath}");

                    // 同一サイズのまま内容だけ差し替わった場合も検出する。索引には
                    // 列挙時点の CRC32（entry.Crc32）が入るため、ここで照合しないと
                    // 「古い CRC32 + 新しい本体」の書庫が成功として作られ、
                    // 復号時に必ず CRC 不一致で失敗する
                    if (entry.Crc32 is { Length: Crc32Utility.CRC32_SIZE } &&
                        !Crc32Utility.FinalizeState(crcState).SequenceEqual(entry.Crc32))
                        throw new IOException(
                            $"File content changed during encryption: {entry.FullPath}");

                    fileIndex++;
                }

                // ループは BUFFER_SIZE * 10 単位で進捗報告するため端数で 99% 止まりになりがち。
                // 「完了」の確定までは 99% で頭打ちにして、完了との誤認を防ぐ。
                progress?.Report(new EncryptionProgress
                {
                    Type = ProcessType.Encrypting,
                    ProgressPercentage = 9900,
                    StatusMessage = Localize["Progress_Finishing"]
                });
            } // DeflateStream → GcmEncryptingStream の順で Dispose（残バッファ deflate + GCM 認証タグ書き出し）

            await fs.FlushAsync(cancellationToken);

            progress?.Report(new EncryptionProgress
            {
                Type = ProcessType.Encrypting,
                ProgressPercentage = 10000,
                StatusMessage = Localize["Progress_Completed"]
            });

            success = true;
            return EncryptionResult.CreateSuccess(outputPath, fileList.Count, "");
        }
        catch (OperationCanceledException)
        {
            return EncryptionResult.CreateCancelled();
        }
        catch (Exception ex)
        {
            return EncryptionResult.CreateError(ErrorCodes.ERROR_UNEXPECTED, ex.Message);
        }
        finally
        {
            CryptoHelper5.SecureClear(commonKey);
            // 暗号化が成功しなかった場合は、作りかけの .atc を削除する。
            // 例外の種類（キャンセル / I/O / その他）を問わず確実に掃除するため finally で実施。
            if (!success && File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { /* 削除失敗は無視 */ }
            }
        }
    }

    /// <summary>
    /// 出力ファイルの作成・更新・アクセス日時を元ファイル（またはフォルダー）に合わせる。
    /// 日時を写せなくても暗号化は成功しているので、失敗は握りつぶす。
    /// </summary>
    private static void ApplyOriginalTimeStamp(string sourcePath, string outputPath)
    {
        try
        {
            FileSystemInfo source = Directory.Exists(sourcePath)
                ? new DirectoryInfo(sourcePath)
                : new FileInfo(sourcePath);
            if (!source.Exists || !File.Exists(outputPath)) return;

            File.SetCreationTimeUtc(outputPath, source.CreationTimeUtc);
            File.SetLastWriteTimeUtc(outputPath, source.LastWriteTimeUtc);
            File.SetLastAccessTimeUtc(outputPath, source.LastAccessTimeUtc);
        }
        catch (Exception)
        {
            // 読み取り専用のボリュームなどで日時を変えられないことがある
        }
    }

    // =====================================================================
    // Post-PPAP 宛先リストを CryptoHelper5 のエントリ形式に変換する
    // =====================================================================
    private static List<CryptoHelper5.RecipientKeyEntry> BuildRecipientList(
        IReadOnlyList<PostPpapKeyInfo> recipients)
    {
        var result = new List<CryptoHelper5.RecipientKeyEntry>(recipients.Count);
        foreach (var r in recipients)
        {
            var rsaPub = CryptoHelper5.ParseRsaPublicKeyFromBase64(r.Modulus, r.Exponent);

            Org.BouncyCastle.Crypto.Parameters.MLKemPublicKeyParameters? mlkemPub = null;
            var isHybrid = r.AlgorithmId is PkaAlgorithmId.Rsa2048MlKem768
                                          or PkaAlgorithmId.Rsa4096MlKem768;
            if (isHybrid)
            {
                if (string.IsNullOrEmpty(r.MlKemPublicKey))
                    throw new InvalidOperationException(
                        Localize.GetString("Error_RecipientNoMlKem", r.Email));
                var mlkemBytes = Convert.FromBase64String(r.MlKemPublicKey);
                mlkemPub = MLKemPublicKeyParameters.FromEncoding(
                    Org.BouncyCastle.Crypto.Parameters.MLKemParameters.ml_kem_768, mlkemBytes);
            }

            var fpBytes = CryptoHelper5.HexStringToBytes(r.Fingerprint);

            result.Add(new CryptoHelper5.RecipientKeyEntry
            {
                Fingerprint = fpBytes,
                PublicKey = rsaPub,
                MlKemPublicKey = mlkemPub,
                AlgorithmId = r.AlgorithmId,
            });
        }
        return result;
    }

    // =====================================================================
    // v150 平文ヘッダ書き込み（32 バイト）
    //   AppVersion(2) + MissTypeLimits(1) + Broken(1) + Token(16)
    //   + DataFileVersion(4) + EncryptionMode(1) + SymAlgo(1) + PubKeyAlgo(1)
    //   + KdfAlgo(1) + Reserved(4)
    // =====================================================================
    private void WriteV5PlainHeader(
        Stream fs, EncryptionOptions options,
        byte encryptionMode, byte pubkeyAlgorithm, byte kdfAlgorithm)
    {
        // AppVersion (short, 2 bytes)
        var appVersion = GetApplicationVersion();
        fs.Write(BitConverter.GetBytes(appVersion), 0, 2);

        // MissTypeLimits (1 byte)
        fs.WriteByte((byte)options.MissTypeLimits);

        // 制限回数を超えたら破壊してよいか (1 byte)
        // アタッシェケース4 と同じ意味で使う（AC5 Windows は常に 0 を書いている）
        fs.WriteByte(options.BreakFileWhenLimitExceeded ? (byte)1 : (byte)0);

        // Token (16 bytes)
        var tokenBytes = Encoding.ASCII.GetBytes(STRING_TOKEN_NORMAL);
        fs.Write(tokenBytes, 0, 16);

        // DataFileVersion (int, 4 bytes) = 150
        fs.Write(BitConverter.GetBytes(DATA_FILE_VERSION), 0, 4);

        // EncryptionMode (1 byte)
        fs.WriteByte(encryptionMode);

        // SymmetricAlgorithm (1 byte) = AES-256-GCM
        fs.WriteByte(SYMMETRIC_ALGORITHM_AES256GCM);

        // PublicKeyAlgorithm (1 byte)
        fs.WriteByte(pubkeyAlgorithm);

        // KdfAlgorithm (1 byte)
        fs.WriteByte(kdfAlgorithm);

        // Reserved (4 bytes)
        fs.Write(new byte[4], 0, 4);
    }

    private static short GetApplicationVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            if (version != null)
                return (short)(version.Major * 1000 + version.Minor * 100 + version.Build * 10 + version.Revision);
        }
        catch { /* ignored */ }
        return 5000;
    }

    // =====================================================================
    // 自己実行形式（Windows 専用）
    // =====================================================================

    /// <summary>
    /// Exeout スタブ（復号プログラム）を出力の先頭に書き込む。
    /// スタブは本体に byte[] で埋め込まれている（Exeout48.cs の
    /// <see cref="FileEncrypt5.rawData"/>。ビルド時に tools\ExeToHex が
    /// Exeout.exe を差し込む）。埋め込みが空＝ ExeToHex を通していない不完全な
    /// ビルドなので、その場合は例外にする（呼び出し側でエラーに写像される）。
    /// </summary>
    private static void WriteExecutableStub(Stream output)
    {
        if (!OperatingSystem.IsWindows())
            throw new NotSupportedException(Localize["Error_ExeoutWindowsOnly"]);

        var stub = FileEncrypt5.rawData;
        if (stub == null || stub.Length == 0)
            throw new InvalidDataException(Localize["Error_ExeoutStubMissing"]);

        output.Write(stub, 0, stub.Length);
    }

    // =====================================================================
    // ファイルインデックスの構築（平文）
    //   各エントリ:
    //     FileNameSize(2) + FileName(可変) + FileSize(8) + Attr(4)
    //     + LastWriteDate(4) + LastWriteTime(4)
    //     + CreationDate(4) + CreationTime(4)
    //     + CRC32(4)  ※ファイルでサイズ > 0 のときのみ
    // =====================================================================
    private byte[] CreateFileIndex(List<FileSystemEntry> fileList, string archiveName = "")
    {
        using var ms = new MemoryStream();

        // 書庫名（親フォルダー）が指定されているときは、Windows 版 FileEncrypt5 と
        // 同じく先頭にディレクトリエントリを 1 つ書き、以降の全エントリの名前に
        // 「書庫名\」を前置する。復号すると書庫名のフォルダーにまとまって出てくる
        // （/nopfldr で外せる）。CLI は常に指定し、GUI は設定に従う。
        var namePrefix = string.Empty;
        if (!string.IsNullOrEmpty(archiveName))
        {
            namePrefix = archiveName.EndsWith('\\') ? archiveName : archiveName + "\\";

            var dirNameBytes = Encoding.UTF8.GetBytes(namePrefix);
            ms.Write(BitConverter.GetBytes((short)dirNameBytes.Length), 0, 2);
            ms.Write(dirNameBytes, 0, dirNameBytes.Length);
            ms.Write(BitConverter.GetBytes(0L), 0, 8);              // サイズ 0
            ms.Write(BitConverter.GetBytes(16), 0, 4);              // 属性 = ディレクトリ
            var now = DateTime.UtcNow;
            ms.Write(BitConverter.GetBytes(GetDateInt(now)), 0, 4);
            ms.Write(BitConverter.GetBytes(GetTimeInt(now)), 0, 4);
            ms.Write(BitConverter.GetBytes(GetDateInt(now)), 0, 4);
            ms.Write(BitConverter.GetBytes(GetTimeInt(now)), 0, 4);
        }

        foreach (var entry in fileList)
        {
            var pathBytes = Encoding.UTF8.GetBytes(namePrefix + entry.RelativePath);
            ms.Write(BitConverter.GetBytes((short)pathBytes.Length), 0, 2);
            ms.Write(pathBytes, 0, pathBytes.Length);

            ms.Write(BitConverter.GetBytes(entry.Size), 0, 8);
            ms.Write(BitConverter.GetBytes((int)entry.Attributes), 0, 4);

            ms.Write(BitConverter.GetBytes(entry.LastWriteDate), 0, 4);
            ms.Write(BitConverter.GetBytes(entry.LastWriteTime), 0, 4);
            ms.Write(BitConverter.GetBytes(entry.CreationDate), 0, 4);
            ms.Write(BitConverter.GetBytes(entry.CreationTime), 0, 4);

            if (!entry.IsDirectory && entry.Size > 0)
            {
                // 復号側は「サイズ > 0 のファイルなら CRC32 が 4 バイト続く」前提で読む。
                // ここで省くと以降のエントリがすべてずれて索引が壊れるので、黙って飛ばさない
                if (entry.Crc32 is not { Length: Crc32Utility.CRC32_SIZE })
                    throw new InvalidOperationException(
                        $"CRC32 を計算できませんでした: {entry.RelativePath}");

                ms.Write(entry.Crc32, 0, Crc32Utility.CRC32_SIZE);
            }
        }

        return ms.ToArray();
    }

    // =====================================================================
    // ファイル/ディレクトリ情報の取得
    // =====================================================================
    private static FileSystemEntry? GetDirectoryInfo(string parentPath, string dirPath)
    {
        try
        {
            var di = new DirectoryInfo(dirPath);
            var relativePath = CreateRelativePath(parentPath, dirPath);
            if (!relativePath.EndsWith("\\")) relativePath += "\\";

            return new FileSystemEntry
            {
                IsDirectory = true,
                FullPath = dirPath,
                RelativePath = relativePath,
                Size = 0,
                Attributes = di.Attributes,
                LastWriteDate = GetDateInt(di.LastWriteTimeUtc),
                LastWriteTime = GetTimeInt(di.LastWriteTimeUtc),
                CreationDate = GetDateInt(di.CreationTimeUtc),
                CreationTime = GetTimeInt(di.CreationTimeUtc),
                Crc32 = Array.Empty<byte>()
            };
        }
        catch
        {
            return null;
        }
    }

    private static FileSystemEntry? GetFileInfo(string parentPath, string filePath, Func<bool> cancelCheck)
    {
        try
        {
            var fi = new FileInfo(filePath);
            return new FileSystemEntry
            {
                IsDirectory = false,
                FullPath = filePath,
                RelativePath = CreateRelativePath(parentPath, filePath),
                Size = fi.Length,
                Attributes = fi.Attributes,
                LastWriteDate = GetDateInt(fi.LastWriteTimeUtc),
                LastWriteTime = GetTimeInt(fi.LastWriteTimeUtc),
                CreationDate = GetDateInt(fi.CreationTimeUtc),
                CreationTime = GetTimeInt(fi.CreationTimeUtc),
                Crc32 = fi.Length == 0 ? null : Crc32Utility.ComputeFileCrc32(filePath, cancelCheck)
            };
        }
        catch
        {
            return null;
        }
    }

    private static FileSystemEntry? GetFileInfoSingle(string filePath, Func<bool> cancelCheck)
    {
        try
        {
            var fi = new FileInfo(filePath);
            return new FileSystemEntry
            {
                IsDirectory = false,
                FullPath = filePath,
                RelativePath = fi.Name,
                Size = fi.Length,
                Attributes = fi.Attributes,
                LastWriteDate = GetDateInt(fi.LastWriteTimeUtc),
                LastWriteTime = GetTimeInt(fi.LastWriteTimeUtc),
                CreationDate = GetDateInt(fi.CreationTimeUtc),
                CreationTime = GetTimeInt(fi.CreationTimeUtc),
                Crc32 = fi.Length == 0 ? null : Crc32Utility.ComputeFileCrc32(filePath, cancelCheck)
            };
        }
        catch
        {
            return null;
        }
    }

    // =====================================================================
    // 相対パス計算（ファイルフォーマット内は Windows 区切り '\' に統一）
    // =====================================================================
    private static string CreateRelativePath(string parentPath, string fullPath)
    {
        if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(fullPath)) return string.Empty;

        parentPath = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar);
        fullPath = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar);

        // 親ディレクトリの祖父母パスを取得（例: /Users/x/Desktop/dummy → /Users/x/Desktop）
        var baseDir = Path.GetDirectoryName(parentPath);
        if (string.IsNullOrEmpty(baseDir)) baseDir = Path.GetPathRoot(parentPath) ?? "";
        baseDir = baseDir.TrimEnd(Path.DirectorySeparatorChar);

        string relativePath;
        if (fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            relativePath = fullPath.Substring(baseDir.Length).TrimStart(Path.DirectorySeparatorChar);
        else
            relativePath = Path.GetFileName(fullPath);

        return relativePath.Replace('/', '\\');
    }

    private static int GetDateInt(DateTime dt) => dt.Year * 10000 + dt.Month * 100 + dt.Day;
    private static int GetTimeInt(DateTime dt) => dt.Hour * 10000 + dt.Minute * 100 + dt.Second;
}

public class FileSystemEntry
{
    public bool IsDirectory { get; set; }
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public FileAttributes Attributes { get; set; }
    public int LastWriteDate { get; set; }
    public int LastWriteTime { get; set; }
    public int CreationDate { get; set; }
    public int CreationTime { get; set; }
    public byte[]? Crc32 { get; set; }
}
