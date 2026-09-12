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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;
using ICSharpCode.SharpZipLib.Zip;

namespace AttacheCase.Services;

/// <summary>
/// パスワード付きZIPを作成（既定: ZipCrypto）
/// </summary>
public sealed class ZipPasswordEncryptionService : IEncryptionService
{
    /// <summary>表示文字列（利用者に見えるメッセージは必ずここを通す）</summary>
    private static readonly LocalizationService Localize = LocalizationService.Instance;

  private readonly ZipPasswordEncryptionMethod _method;

  public ZipPasswordEncryptionService(ZipPasswordEncryptionMethod method = ZipPasswordEncryptionMethod.ZipCrypto)
  {
    _method = method;
  }

  public async Task<EncryptionResult> EncryptAsync(
    string[] inputPaths,
    string outputPath,
    string password,
    EncryptionOptions options,
    IProgress<EncryptionProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    var sw = Stopwatch.StartNew();

    // 書きかけの一時ファイル。置き換え成功で null にする。
    // 失敗・キャンセル時は finally で必ず消す
    string? tempPath = null;

    try
    {
      if (inputPaths is null || inputPaths.Length == 0)
        return EncryptionResult.CreateError(ErrorCodes.DATA_NOT_FOUND, Localize["Error_NoInputPath"]);

      if (string.IsNullOrWhiteSpace(outputPath))
        return EncryptionResult.CreateError(ErrorCodes.INVALID_FILE_PATH, Localize["Error_InvalidOutputPath"]);

      // 「なし」はただの ZIP を作る（パスワード不要）
      if (_method != ZipPasswordEncryptionMethod.None && string.IsNullOrEmpty(password))
        return EncryptionResult.CreateError(ErrorCodes.CRYPTOGRAPHIC_EXCEPTION, Localize["Error_EmptyPassword"]);

      // 出力先がディレクトリだった場合は ArchiveName / ファイル名からzip名を決める
      outputPath = NormalizeOutputPath(outputPath, inputPaths, options);

      var fileItems = BuildFileList(inputPaths, options.ArchiveName);
      if (fileItems.Count == 0)
        return EncryptionResult.CreateError(ErrorCodes.DATA_NOT_FOUND, Localize["Error_NoFilesToEncrypt"]);

      progress?.Report(new EncryptionProgress
      {
        Type = ProcessType.Preparing,
        ProgressPercentage = 0,
        TotalFiles = fileItems.Count,
        ProcessedFiles = 0,
        StatusMessage = Localize["Progress_PreparingZip"]
      });

      cancellationToken.ThrowIfCancellationRequested();

      Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

      // 同じフォルダーの一時ファイルへ書き、Finish 成功後に最終パスへ置き換える。
      // 途中で例外・キャンセルが起きても、書きかけの壊れた .zip を出力先に残さず、
      // 既存の同名ファイルも（置き換え成功まで）壊さない
      tempPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

      await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
      await using (var zipStream = new ZipOutputStream(fs)
      {
        IsStreamOwner = false,
        // これでZipCryptoが有効（AESはZipEntry側のAESKeySizeで切替）。
        // 「なし」のときは設定しない（null のままだと暗号化されない）
        Password = _method == ZipPasswordEncryptionMethod.None ? null : password
      })
      {
        zipStream.UseZip64 = UseZip64.On;
        zipStream.SetLevel(MapCompressionLevel(options.CompressionLevel));

        int processed = 0;
        foreach (var item in fileItems)
        {
          cancellationToken.ThrowIfCancellationRequested();

          processed++;
          var percent = (int)Math.Clamp((long)processed * 10000 / fileItems.Count, 0, 10000);

          progress?.Report(new EncryptionProgress
          {
            Type = ProcessType.Encrypting,
            ProgressPercentage = percent,
            CurrentFilePath = item.FullPath,
            TotalFiles = fileItems.Count,
            ProcessedFiles = processed - 1,
            StatusMessage = Localize["Progress_AddingToZip"]
          });

          var fi = new FileInfo(item.FullPath);
          if (!fi.Exists) continue;

          var entryName = ToZipEntryName(item.EntryPath);

          var entry = new ZipEntry(entryName)
          {
            DateTime = options.KeepTimeStamp ? fi.LastWriteTime : DateTime.Now,
            Size = fi.Length,
            IsUnicodeText = true
          };

          // AESにするならここを有効化（ただしWindows Explorer互換は失われる）
          entry.AESKeySize = _method switch
          {
            ZipPasswordEncryptionMethod.Aes128 => 128,
            ZipPasswordEncryptionMethod.Aes256 => 256,
            _ => 0
          };

          zipStream.PutNextEntry(entry);

          await using (var input = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
          {
            await CopyStreamAsync(input, zipStream, cancellationToken).ConfigureAwait(false);
          }

          zipStream.CloseEntry();

          progress?.Report(new EncryptionProgress
          {
            Type = ProcessType.Encrypting,
            ProgressPercentage = percent,
            CurrentFilePath = item.FullPath,
            TotalFiles = fileItems.Count,
            ProcessedFiles = processed,
            StatusMessage = Localize["Progress_AddingToZip"]
          });
        }

        zipStream.Finish();
      }

      // ここまで来たら中身は完成。最終パスへ原子的に置き換える
      File.Move(tempPath, outputPath, overwrite: true);
      tempPath = null;
      sw.Stop();

      return EncryptionResult.CreateSuccess(outputPath, fileItems.Count, sw.Elapsed.ToString());
    }
    catch (OperationCanceledException)
    {
      return EncryptionResult.CreateCancelled();
    }
    catch (DirectoryNotFoundException ex)
    {
      return EncryptionResult.CreateError(ErrorCodes.DIRECTORY_NOT_FOUND, ex.Message);
    }
    catch (FileNotFoundException ex)
    {
      return EncryptionResult.CreateError(ErrorCodes.FILE_NOT_FOUND, ex.Message);
    }
    catch (UnauthorizedAccessException ex)
    {
      return EncryptionResult.CreateError(ErrorCodes.OS_DENIES_ACCESS, ex.Message);
    }
    catch (IOException ex)
    {
      return EncryptionResult.CreateError(ErrorCodes.IO_EXCEPTION, ex.Message);
    }
    catch (Exception ex)
    {
      return EncryptionResult.CreateError(ErrorCodes.ERROR_UNEXPECTED, ex.ToString());
    }
    finally
    {
      // 置き換えに至らなかった（例外・キャンセル）ときは、書きかけを残さない
      if (tempPath != null)
      {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); }
        catch (IOException) { /* 消せなくても既存の出力は壊していない */ }
        catch (UnauthorizedAccessException) { /* 同上 */ }
      }
    }
  }

  private static string NormalizeOutputPath(string outputPath, string[] inputPaths, EncryptionOptions options)
  {
    if (Directory.Exists(outputPath) || outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith(Path.AltDirectorySeparatorChar))
    {
      var dir = outputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      var name =
        !string.IsNullOrWhiteSpace(options.ArchiveName) ? options.ArchiveName :
        Path.GetFileNameWithoutExtension(inputPaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

      if (string.IsNullOrWhiteSpace(name))
        name = "archive";

      return Path.Combine(dir, $"{name}.zip");
    }
    return outputPath;
  }

  private static int MapCompressionLevel(System.IO.Compression.CompressionLevel level)
  {
    // SharpZipLib: 0-9
    return level switch
    {
      System.IO.Compression.CompressionLevel.NoCompression => 0,
      System.IO.Compression.CompressionLevel.Fastest => 1,
#if NET6_0_OR_GREATER
      System.IO.Compression.CompressionLevel.SmallestSize => 9,
#endif
      _ => 7 // Optimal相当
    };
  }

  private static async Task CopyStreamAsync(Stream input, Stream output, CancellationToken ct)
  {
    var buffer = new byte[1024 * 128];
    while (true)
    {
      ct.ThrowIfCancellationRequested();
      var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
      if (read <= 0) break;
      await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
    }
  }

  private static string ToZipEntryName(string path)
    => path.Replace('\\', '/').TrimStart('/');

  private static List<FileItem> BuildFileList(string[] inputPaths, string archiveName)
  {
    var list = new List<FileItem>();

    // 別フォルダーの同名ファイルを複数指定すると、単独ファイルの相対パスは
    // どちらも "memo.txt" になり、ZIP 内でエントリ名が衝突する。
    // 展開時に一方が黙って上書きされるため、正規化した名前で重複を検出して
    // "memo (2).txt" のように一意化する（比較は Windows なら大文字小文字無視）
    var comparer = OperatingSystem.IsWindows()
      ? StringComparer.OrdinalIgnoreCase
      : StringComparer.Ordinal;
    var usedEntries = new HashSet<string>(comparer);

    void AddUnique(string fullPath, string entry)
    {
      list.Add(new FileItem(fullPath, MakeUniqueEntry(entry, usedEntries)));
    }

    foreach (var raw in inputPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
    {
      var path = raw.Trim();

      if (File.Exists(path))
      {
        var baseDir = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        var rel = Path.GetRelativePath(baseDir, path);
        var entry = string.IsNullOrWhiteSpace(archiveName) ? rel : Path.Combine(archiveName, rel);
        AddUnique(path, entry);
        continue;
      }

      if (Directory.Exists(path))
      {
        var dir = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var baseDir = Path.GetDirectoryName(dir) ?? Directory.GetCurrentDirectory();

        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
          var rel = Path.GetRelativePath(baseDir, f); // 先頭にディレクトリ名を含める
          var entry = string.IsNullOrWhiteSpace(archiveName) ? rel : Path.Combine(archiveName, rel);
          AddUnique(f, entry);
        }
      }
    }

    return list;
  }

  /// <summary>
  /// ZIP エントリ名が既存と衝突しないよう、拡張子の手前に " (2)", " (3)" … を足す。
  /// 比較は <see cref="ToZipEntryName"/> と同じ正規化（区切りは "/"）で行う。
  /// </summary>
  private static string MakeUniqueEntry(string entry, HashSet<string> used)
  {
    var normalized = ToZipEntryName(entry);
    if (used.Add(normalized)) return entry;

    var dir = Path.GetDirectoryName(entry) ?? string.Empty;
    var stem = Path.GetFileNameWithoutExtension(entry);
    var ext = Path.GetExtension(entry);

    for (var i = 2; ; i++)
    {
      var candidateName = $"{stem} ({i}){ext}";
      var candidate = string.IsNullOrEmpty(dir) ? candidateName : Path.Combine(dir, candidateName);
      if (used.Add(ToZipEntryName(candidate))) return candidate;
    }
  }

  private readonly record struct FileItem(string FullPath, string EntryPath);
}
