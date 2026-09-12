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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;
using ICSharpCode.SharpZipLib.Zip;

namespace AttacheCase.Services;

public sealed class ZipPasswordDecryptionService : IDecryptionService
{
    /// <summary>表示文字列（利用者に見えるメッセージは必ずここを通す）</summary>
    private static readonly LocalizationService Localize = LocalizationService.Instance;

    [Obsolete("Obsolete")]
    public async Task<EncryptionResult> DecryptAsync(
        string inputPath,
        string outputDirectory,
        string password,
        DecryptionOptions options,
        IProgress<EncryptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(inputPath))
            {
                return EncryptionResult.CreateError(
                    ErrorCodes.FILE_NOT_FOUND,
                    Localize["Error_FileNotFound"]);
            }

            Directory.CreateDirectory(outputDirectory);

            // エンコーディング設定。
            // ZipStrings.CodePage は静的な共有状態なので、同時に別の ZIP を扱うと
            // エントリ名を取り違える。ZipFile ごとに StringCodec を渡す
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var codec = StringCodec.FromCodePage(DetectCodePage(inputPath));

            await using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            using var zipFile = new ZipFile(fs, leaveOpen: true, codec);
            zipFile.Password = password;

            var totalEntries = zipFile.Count;
            var processedEntries = 0;

            foreach (ZipEntry entry in zipFile)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entryName = entry.Name;
                var outputPath = Path.Combine(outputDirectory, entryName);

                // ディレクトリトラバーサル対策。
                // 末尾に区切り文字を付けずに前方一致を見ると、出力先が /tmp/out のとき
                // ../outX/evil.txt が /tmp/outX/evil.txt に解決されて検査を通ってしまう
                var fullOutputPath = Path.GetFullPath(outputPath);
                var fullOutputDir = Path.GetFullPath(outputDirectory);
                if (!fullOutputDir.EndsWith(Path.DirectorySeparatorChar))
                    fullOutputDir += Path.DirectorySeparatorChar;

                var comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!fullOutputPath.StartsWith(fullOutputDir, comparison))
                {
                    continue; // 危険なパスはスキップ
                }

                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(fullOutputPath);
                }
                else
                {
                    // 親ディレクトリを作成
                    var parentDir = Path.GetDirectoryName(fullOutputPath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    // 上書き可否は options.OverwriteOption に従う。
                    // Skip / SkipAll のときは既存ファイルを壊さずに飛ばす
                    // （CLI が /overwrite 無しで渡す SkipAll をここで効かせないと、
                    //   確認を挟まない経路で既存ファイルを黙って上書きしてしまう）
                    if (File.Exists(fullOutputPath) &&
                        options.OverwriteOption is OverwriteOption.Skip or OverwriteOption.SkipAll)
                    {
                        processedEntries++;
                        continue;
                    }

                    // ファイルを解凍（FileMode.Create で上書きを許可）
                    await using var entryStream = zipFile.GetInputStream(entry);
                    await using var outStream = new FileStream(fullOutputPath, FileMode.Create, FileAccess.Write);

                    var buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = await entryStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await outStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    }

                    // タイムスタンプ復元
                    if (options.RestoreTimeStamp && entry.DateTime != DateTime.MinValue)
                    {
                        try
                        {
                            File.SetLastWriteTime(fullOutputPath, entry.DateTime);
                        }
                        catch
                        {
                            // タイムスタンプ設定失敗は無視
                        }
                    }
                }

                processedEntries++;
                progress?.Report(new EncryptionProgress
                {
                    Type = ProcessType.Decrypting,
                    ProgressPercentage = (int)(processedEntries * 10000 / totalEntries),
                    StatusMessage = Localize.GetString("Progress_Extracting", entryName)
                });
            }

            return EncryptionResult.CreateSuccess(outputDirectory, (int)totalEntries, "");
        }
        catch (OperationCanceledException)
        {
            return EncryptionResult.CreateCancelled();
        }
        catch (ZipException ex)
        {
            return EncryptionResult.CreateError(
                ErrorCodes.CRYPTOGRAPHIC_EXCEPTION,
                Localize.GetString("Error_ZipExtractFailed", ex.Message));
        }
        catch (Exception ex)
        {
            return EncryptionResult.CreateError(
                ErrorCodes.ERROR_UNEXPECTED,
                ex.Message);
        }
    }

    [Obsolete("Obsolete")]
    public Task<FileListResult> GetFileListAsync(
        string inputPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(inputPath))
            {
                return Task.FromResult(FileListResult.CreateError(
                    ErrorCodes.FILE_NOT_FOUND,
                    Localize["Error_FileNotFound"]));
            }

            // エンコーディング設定（上と同じ理由で StringCodec を渡す）
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var codec = StringCodec.FromCodePage(DetectCodePage(inputPath));

            using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zipFile = new ZipFile(fs, leaveOpen: true, codec);

            var filePaths = new List<string>();
            foreach (ZipEntry entry in zipFile)
            {
                filePaths.Add(entry.Name);
            }

            return Task.FromResult(FileListResult.CreateSuccess(filePaths));
        }
        catch (Exception ex)
        {
            return Task.FromResult(FileListResult.CreateError(
                ErrorCodes.ERROR_UNEXPECTED,
                ex.Message));
        }
    }

    [Obsolete("Obsolete")]
    public Task<FileListResult> GetFileListAsync(
      string inputPath,
      string password,
      DecryptionOptions options,
      CancellationToken cancellationToken = default)
    {
      // ZIPはRSAに関係ないので、既存メソッドを呼び出す
      return GetFileListAsync(inputPath, password, cancellationToken);
    }

    /// <summary>
    /// ZIPファイルのエンコーディングを自動判定
    /// </summary>
    private static int DetectCodePage(string zipPath)
    {
        try
        {
            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zipFile = new ZipFile(fs, leaveOpen: true, StringCodec.Default);

            foreach (ZipEntry entry in zipFile)
            {
                // UTF-8フラグ（ビット11）をチェック
                if ((entry.Flags & (int)GeneralBitFlags.UnicodeText) != 0)
                {
                    return 65001; // UTF-8
                }
            }

            // フラグがなければShift-JIS（Windows互換）
            return 932;
        }
        catch
        {
            return 932; // デフォルトはShift-JIS
        }
    }
}