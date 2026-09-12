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

using System.IO.Compression;
using System.Security.Cryptography;
using AttacheCase.Models;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class EncryptionServiceTests
{
  [Fact]
  public async Task EncryptDecrypt_NoCompression_ShouldWork()
  {
    // Arrange
    var testData = "Hello, World! 日本語テスト"u8.ToArray();
    var password = "TestPassword123";
    var encryptionService = new EncryptionService();
    var decryptionService = new DecryptionService();

    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);

    var tempInput = Path.Combine(tempDir, "input.txt");
    var tempEncrypted = Path.Combine(tempDir, "encrypted.atc");
    var outputDir = Path.Combine(tempDir, "output");

    await File.WriteAllBytesAsync(tempInput, testData);

    Console.WriteLine($"元ファイルパス: {tempInput}");
    Console.WriteLine($"暗号化ファイルパス: {tempEncrypted}");
    Console.WriteLine($"出力ディレクトリ: {outputDir}");

    // Act: 暗号化（圧縮なし）
    var encryptResult = await encryptionService.EncryptAsync(
        new[] { tempInput },
        tempEncrypted,
        password,
        new EncryptionOptions { CompressionLevel = CompressionLevel.NoCompression } // ← 圧縮なし
    );

    Console.WriteLine($"暗号化結果: {encryptResult.Success}");
    if (!encryptResult.Success)
    {
        Console.WriteLine($"暗号化エラー: {encryptResult.ErrorMessage}");
    }

    Assert.True(encryptResult.Success, $"暗号化失敗: {encryptResult.ErrorMessage}");
    Assert.True(File.Exists(tempEncrypted), "暗号化ファイルが作成されていない");

    // Act: 復号
    var decryptResult = await decryptionService.DecryptAsync(
        tempEncrypted,
        outputDir,
        password,
        new DecryptionOptions()
    );

    Console.WriteLine($"復号結果: {decryptResult.Success}");
    if (!decryptResult.Success)
    {
        Console.WriteLine($"復号エラー: {decryptResult.ErrorMessage}");
        Console.WriteLine($"復号エラーコード: {decryptResult.ErrorCode}");
    }

    Assert.True(decryptResult.Success, $"復号失敗: {decryptResult.ErrorMessage}");

    // 復号されたファイルを確認
    var decryptedFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
    Assert.NotEmpty(decryptedFiles);

    var decryptedFile = decryptedFiles[0];
    var decryptedContent = await File.ReadAllTextAsync(decryptedFile);
    var originalContent = await File.ReadAllTextAsync(tempInput);

    Console.WriteLine($"元の内容: {originalContent}");
    Console.WriteLine($"復号後の内容: {decryptedContent}");

    Assert.Equal(originalContent, decryptedContent);

    // Cleanup
    Directory.Delete(tempDir, true);
  }

  [Fact]
  public async Task EncryptDecrypt_SmallFile_ShouldPreserveIntegrity()
  {
    // Arrange
    var testData = "Hello, World! 日本語テスト"u8.ToArray();
    var password = "TestPassword123";
    var encryptionService = new EncryptionService();
    var decryptionService = new DecryptionService();

    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);

    var tempInput = Path.Combine(tempDir, "input.txt");
    var tempEncrypted = Path.Combine(tempDir, "encrypted.atc");
    var outputDir = Path.Combine(tempDir, "output");

    await File.WriteAllBytesAsync(tempInput, testData);

    // デバッグ: 元ファイルの内容確認
    Console.WriteLine($"元ファイルサイズ: {new FileInfo(tempInput).Length} bytes");
    Console.WriteLine($"元ファイル内容: {await File.ReadAllTextAsync(tempInput)}");

    // 元ファイルのハッシュ計算
    var originalMD5 = ComputeMD5(tempInput);
    var originalSHA256 = ComputeSHA256(tempInput);

    Console.WriteLine($"元ファイルMD5: {originalMD5}");
    Console.WriteLine($"元ファイルSHA256: {originalSHA256}");

    // Act: 暗号化
    var encryptResult = await encryptionService.EncryptAsync(
        new[] { tempInput },
        tempEncrypted,
        password,
        new EncryptionOptions { CompressionLevel = CompressionLevel.Optimal }
    );

    // Assert: 暗号化成功
    Assert.True(encryptResult.Success, $"暗号化失敗: {encryptResult.ErrorMessage}");
    Assert.True(File.Exists(tempEncrypted), "暗号化ファイルが作成されていない");

    Console.WriteLine($"暗号化ファイルサイズ: {new FileInfo(tempEncrypted).Length} bytes");

    // Act: 復号
    var decryptResult = await decryptionService.DecryptAsync(
        tempEncrypted,
        outputDir,
        password,
        new DecryptionOptions()
    );

    // Assert: 復号成功
    Assert.True(decryptResult.Success, $"復号失敗: {decryptResult.ErrorMessage}");

    // デバッグ: 復号されたファイルを全て表示
    Console.WriteLine($"出力ディレクトリ: {outputDir}");
    var allFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
    Console.WriteLine($"復号されたファイル数: {allFiles.Length}");
    foreach (var file in allFiles)
    {
        Console.WriteLine($"  - {file} ({new FileInfo(file).Length} bytes)");
    }

    // 復号されたファイルを探す
    var decryptedFile = allFiles.FirstOrDefault();
    Assert.NotNull(decryptedFile);

    // デバッグ: 復号ファイルの内容確認
    Console.WriteLine($"復号ファイル内容: {await File.ReadAllTextAsync(decryptedFile)}");

    // Assert: チェックサム検証
    var decryptedMD5 = ComputeMD5(decryptedFile);
    var decryptedSHA256 = ComputeSHA256(decryptedFile);

    Console.WriteLine($"復号ファイルMD5: {decryptedMD5}");
    Console.WriteLine($"復号ファイルSHA256: {decryptedSHA256}");

    Assert.Equal(originalMD5, decryptedMD5);
    Assert.Equal(originalSHA256, decryptedSHA256);

    // Cleanup
    Directory.Delete(tempDir, true);
  }

  private static string ComputeMD5(string filePath)
  {
    using var md5 = MD5.Create();
    using var stream = File.OpenRead(filePath);
    var hash = md5.ComputeHash(stream);
    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
  }

  private static string ComputeSHA256(string filePath)
  {
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);
    var hash = sha256.ComputeHash(stream);
    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
  }

  [Theory]
  [InlineData(CompressionLevel.NoCompression)]
  [InlineData(CompressionLevel.Fastest)]
  [InlineData(CompressionLevel.Optimal)]
  [InlineData(CompressionLevel.SmallestSize)]
  public async Task EncryptDecrypt_VariousCompressionLevels_ShouldWork(CompressionLevel compressionLevel)
  {
    // Arrange
    var testData = "Hello, World! 日本語テスト 🎉"u8.ToArray();
    var password = "TestPassword123";
    var encryptionService = new EncryptionService();
    var decryptionService = new DecryptionService();

    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);

    var tempInput = Path.Combine(tempDir, "input.txt");
    var tempEncrypted = Path.Combine(tempDir, "encrypted.atc");
    var outputDir = Path.Combine(tempDir, "output");

    await File.WriteAllBytesAsync(tempInput, testData);

    var originalMD5 = ComputeMD5(tempInput);

    // Act: 暗号化
    var encryptResult = await encryptionService.EncryptAsync(
      new[] { tempInput },
      tempEncrypted,
      password,
      new EncryptionOptions { CompressionLevel = compressionLevel }
    );

    // Act: 復号
    var decryptResult = await decryptionService.DecryptAsync(
      tempEncrypted,
      outputDir,
      password,
      new DecryptionOptions()
    );

    // Assert
    Assert.True(encryptResult.Success);
    Assert.True(decryptResult.Success);

    var decryptedFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
    Assert.NotEmpty(decryptedFiles);

    var decryptedMD5 = ComputeMD5(decryptedFiles[0]);
    Assert.Equal(originalMD5, decryptedMD5);

    // Cleanup
    Directory.Delete(tempDir, true);
  }

  [Fact]
  public async Task Decrypt_WrongPassword_ShouldFail()
  {
      // Arrange
      var testData = "Test Data"u8.ToArray();
      var correctPassword = "CorrectPassword";
      var wrongPassword = "WrongPassword";

      var encryptionService = new EncryptionService();
      var decryptionService = new DecryptionService();

      var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
      Directory.CreateDirectory(tempDir);

      var tempInput = Path.Combine(tempDir, "input.txt");
      var tempEncrypted = Path.Combine(tempDir, "encrypted.atc");
      var outputDir = Path.Combine(tempDir, "output");

      await File.WriteAllBytesAsync(tempInput, testData);

      // Act: 正しいパスワードで暗号化
      await encryptionService.EncryptAsync(
          new[] { tempInput },
          tempEncrypted,
          correctPassword,
          new EncryptionOptions()
      );

      // Act: 間違ったパスワードで復号
      var decryptResult = await decryptionService.DecryptAsync(
          tempEncrypted,
          outputDir,
          wrongPassword,
          new DecryptionOptions()
      );

      // Assert: 復号失敗
      Assert.False(decryptResult.Success);
      Assert.Equal(ErrorCodes.PASSWORD_TOKEN_NOT_FOUND, decryptResult.ErrorCode);

      // Cleanup
      Directory.Delete(tempDir, true);
  }

  [Fact]
  public async Task EncryptDecrypt_LargeFile_ShouldWork()
  {
      // Arrange: 10MBのランダムデータ
      var testData = new byte[10 * 1024 * 1024];
      Random.Shared.NextBytes(testData);

      var password = "TestPassword123";
      var encryptionService = new EncryptionService();
      var decryptionService = new DecryptionService();

      var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
      Directory.CreateDirectory(tempDir);

      var tempInput = Path.Combine(tempDir, "large_file.bin");
      var tempEncrypted = Path.Combine(tempDir, "encrypted.atc");
      var outputDir = Path.Combine(tempDir, "output");

      await File.WriteAllBytesAsync(tempInput, testData);

      Console.WriteLine($"元ファイルサイズ: {new FileInfo(tempInput).Length:N0} bytes");

      var originalMD5 = ComputeMD5(tempInput);
      var originalSHA256 = ComputeSHA256(tempInput);

      // Act: 暗号化
      var sw = System.Diagnostics.Stopwatch.StartNew();
      var encryptResult = await encryptionService.EncryptAsync(
          new[] { tempInput },
          tempEncrypted,
          password,
          new EncryptionOptions { CompressionLevel = CompressionLevel.Fastest }
      );
      sw.Stop();
      Console.WriteLine($"暗号化時間: {sw.ElapsedMilliseconds}ms");
      Console.WriteLine($"暗号化ファイルサイズ: {new FileInfo(tempEncrypted).Length:N0} bytes");

      // Act: 復号
      sw.Restart();
      var decryptResult = await decryptionService.DecryptAsync(
          tempEncrypted,
          outputDir,
          password,
          new DecryptionOptions()
      );
      sw.Stop();
      Console.WriteLine($"復号時間: {sw.ElapsedMilliseconds}ms");

      // Assert
      Assert.True(encryptResult.Success, $"暗号化失敗: {encryptResult.ErrorMessage}");
      Assert.True(decryptResult.Success, $"復号失敗: {decryptResult.ErrorMessage}");

      var decryptedFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
      Assert.NotEmpty(decryptedFiles);

      var decryptedMD5 = ComputeMD5(decryptedFiles[0]);
      var decryptedSHA256 = ComputeSHA256(decryptedFiles[0]);

      Assert.Equal(originalMD5, decryptedMD5);
      Assert.Equal(originalSHA256, decryptedSHA256);

      // Cleanup
      Directory.Delete(tempDir, true);
  }

  [Fact]
  public async Task EncryptDecrypt_MultipleFiles_ShouldWork()
  {
      // Arrange
      var password = "TestPassword123";
      var encryptionService = new EncryptionService();
      var decryptionService = new DecryptionService();

      var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
      Directory.CreateDirectory(tempDir);

      var sourceDir = Path.Combine(tempDir, "source");
      Directory.CreateDirectory(sourceDir);

      var file1 = Path.Combine(sourceDir, "file1.txt");
      var file2 = Path.Combine(sourceDir, "file2.txt");
      var file3 = Path.Combine(sourceDir, "日本語ファイル名.txt");

      await File.WriteAllTextAsync(file1, "File 1 Content");
      await File.WriteAllTextAsync(file2, "File 2 Content with 日本語");
      await File.WriteAllTextAsync(file3, "日本語コンテンツ 🎉");

      var originalHashes = new Dictionary<string, string>
      {
          ["file1.txt"] = ComputeMD5(file1),
          ["file2.txt"] = ComputeMD5(file2),
          ["日本語ファイル名.txt"] = ComputeMD5(file3)
      };

      var tempEncrypted = Path.Combine(tempDir, "encrypted.atc");
      var outputDir = Path.Combine(tempDir, "output");

      // Act: 暗号化（複数ファイルを個別に指定）
      var encryptResult = await encryptionService.EncryptAsync(
          new[] { file1, file2, file3 },
          tempEncrypted,
          password,
          new EncryptionOptions()
      );

      // Act: 復号
      var decryptResult = await decryptionService.DecryptAsync(
          tempEncrypted,
          outputDir,
          password,
          new DecryptionOptions()
      );

      // Assert
      Assert.True(encryptResult.Success);
      Assert.True(decryptResult.Success);

      var decryptedFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
      Assert.Equal(3, decryptedFiles.Length);

      // 各ファイルのMD5チェック
      foreach (var decryptedFile in decryptedFiles)
      {
          var fileName = Path.GetFileName(decryptedFile);
          Assert.Contains(fileName, originalHashes.Keys);

          var decryptedMD5 = ComputeMD5(decryptedFile);
          Assert.Equal(originalHashes[fileName], decryptedMD5);

          Console.WriteLine($"✅ {fileName}: MD5一致");
      }

      // Cleanup
      Directory.Delete(tempDir, true);
  }

  [Fact]
  public async Task EncryptDecrypt_FolderWithSubdirectories_ShouldWork()
  {
      // Arrange
      var password = "TestPassword123";
      var encryptionService = new EncryptionService();
      var decryptionService = new DecryptionService();

      var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
      Directory.CreateDirectory(tempDir);

      var sourceDir = Path.Combine(tempDir, "source");
      Directory.CreateDirectory(sourceDir);

      var subDir1 = Path.Combine(sourceDir, "subdir1");
      var subDir2 = Path.Combine(sourceDir, "subdir2");
      Directory.CreateDirectory(subDir1);
      Directory.CreateDirectory(subDir2);

      var file1 = Path.Combine(sourceDir, "root.txt");
      var file2 = Path.Combine(subDir1, "sub1.txt");
      var file3 = Path.Combine(subDir2, "sub2.txt");

      await File.WriteAllTextAsync(file1, "Root file");
      await File.WriteAllTextAsync(file2, "Subdir1 file");
      await File.WriteAllTextAsync(file3, "Subdir2 file");

      var tempEncrypted = Path.Combine(tempDir, "encrypted.atc");
      var outputDir = Path.Combine(tempDir, "output");

      // Act: フォルダー全体を暗号化
      var encryptResult = await encryptionService.EncryptAsync(
          new[] { sourceDir },
          tempEncrypted,
          password,
          new EncryptionOptions()
      );

      // Act: 復号
      var decryptResult = await decryptionService.DecryptAsync(
          tempEncrypted,
          outputDir,
          password,
          new DecryptionOptions()
      );

      // Assert
      Assert.True(encryptResult.Success);
      Assert.True(decryptResult.Success);

      // ディレクトリ構造の確認
      var decryptedFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
      Assert.Equal(3, decryptedFiles.Length);

      // サブディレクトリが復元されているか確認
      var hasSubdir1 = decryptedFiles.Any(f => f.Contains("subdir1"));
      var hasSubdir2 = decryptedFiles.Any(f => f.Contains("subdir2"));

      Assert.True(hasSubdir1, "subdir1が復元されていません");
      Assert.True(hasSubdir2, "subdir2が復元されていません");

      Console.WriteLine("復号されたファイル:");
      foreach (var file in decryptedFiles)
      {
          Console.WriteLine($"  {file}");
      }

      // Cleanup
      Directory.Delete(tempDir, true);
  }

  [Fact]
  public async Task EncryptDecrypt_EmptyFile_ShouldWork()
  {
      // Arrange: 0バイトファイル
      var password = "TestPassword123";
      var encryptionService = new EncryptionService();
      var decryptionService = new DecryptionService();

      var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
      Directory.CreateDirectory(tempDir);

      var tempInput = Path.Combine(tempDir, "empty.txt");
      await File.WriteAllTextAsync(tempInput, string.Empty);

      var tempEncrypted = Path.Combine(tempDir, "encrypted.atc");
      var outputDir = Path.Combine(tempDir, "output");

      // Act
      var encryptResult = await encryptionService.EncryptAsync(
          new[] { tempInput },
          tempEncrypted,
          password,
          new EncryptionOptions()
      );

      var decryptResult = await decryptionService.DecryptAsync(
          tempEncrypted,
          outputDir,
          password,
          new DecryptionOptions()
      );

      // Assert
      Assert.True(encryptResult.Success);
      Assert.True(decryptResult.Success);

      var decryptedFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
      Assert.NotEmpty(decryptedFiles);

      var decryptedFile = decryptedFiles[0];
      Assert.Equal(0, new FileInfo(decryptedFile).Length);

      // Cleanup
      Directory.Delete(tempDir, true);
  }

  [Theory]
  [InlineData("simple.txt")]
  [InlineData("日本語ファイル名.txt")]
  [InlineData("file with spaces.txt")]
  [InlineData("very_long_filename_that_exceeds_normal_limits_to_test_edge_cases_in_filename_handling.txt")]
  public async Task EncryptDecrypt_VariousFilenames_ShouldWork(string filename)
  {
      // Arrange
      var password = "TestPassword123";
      var encryptionService = new EncryptionService();
      var decryptionService = new DecryptionService();

      var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
      Directory.CreateDirectory(tempDir);

      var tempInput = Path.Combine(tempDir, filename);
      await File.WriteAllTextAsync(tempInput, "Test content");

      var originalMD5 = ComputeMD5(tempInput);

      var tempEncrypted = Path.Combine(tempDir, "encrypted.atc");
      var outputDir = Path.Combine(tempDir, "output");

      // Act
      var encryptResult = await encryptionService.EncryptAsync(
          new[] { tempInput },
          tempEncrypted,
          password,
          new EncryptionOptions()
      );

      var decryptResult = await decryptionService.DecryptAsync(
          tempEncrypted,
          outputDir,
          password,
          new DecryptionOptions()
      );

      // Assert
      Assert.True(encryptResult.Success);
      Assert.True(decryptResult.Success);

      var decryptedFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
      Assert.NotEmpty(decryptedFiles);

      var decryptedMD5 = ComputeMD5(decryptedFiles[0]);
      Assert.Equal(originalMD5, decryptedMD5);

      Console.WriteLine($"✅ {filename}: 成功");

      // Cleanup
      Directory.Delete(tempDir, true);
  }
}