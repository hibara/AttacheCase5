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

using AttacheCase.Models;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

/// <summary>
/// Windows 版で生成された AC4 (v140) ファイルが Mac 版 AC5 で復号できることを確認するテスト。
/// 検体は tests/AttacheCase.Tests/TestData/ に置かれている（旧版の実物で生成したもの）。
/// </summary>
public class CompatibilityTests
{
    private const string Password = "TestPassword123";

    // AC4 (v140) 後方互換のサンプル。Windows 版 ver.4（公式配布の最新版）で暗号化した実物を
    // TestData/ に置いてある。パスワードは "sample"、中身は sample.txt 1 件。
    //
    // 旧 CompatibilityTest/ のデータ（test_mac.txt.atc / TestPictures.atc / はるの近影.atc）は
    // ヘッダこそ v140 だが、どのパスワード候補でも復号できず、旧 Mac 版 AC4 が実験段階で
    // 生成した不正データと判断してテスト対象から外した。
    private static string Ac4SamplePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "sample.atc");

    private const string Ac4SamplePassword = "sample";

    /// <summary>
    /// Windows 版 AC4 (v140) で暗号化されたファイルのファイルリストが読めること。
    /// UI は復号前に GetFileListAsync を呼ぶので、ここが通らないと復号まで到達しない。
    /// </summary>
    [Fact]
    public async Task Ac4_Sample_GetFileList_ShouldWork()
    {
        Assert.True(File.Exists(Ac4SamplePath), $"テストデータが見つからない: {Ac4SamplePath}");

        var ds = new DecryptionService();
        var result = await ds.GetFileListAsync(
            Ac4SamplePath, Ac4SamplePassword, new DecryptionOptions(), CancellationToken.None);

        Assert.True(result.Success,
            $"GetFileListAsync 失敗: {result.ErrorMessage} (code={result.ErrorCode})");
        Assert.Contains("sample.txt", result.FilePaths);
    }

    /// <summary>
    /// Windows 版 AC4 (v140) で暗号化されたファイルが復号できること。
    /// v140 の鍵導出（PBKDF2-HMAC-SHA1 / 1000）とパディング処理の Windows 互換性を担保する。
    /// </summary>
    [Fact]
    public async Task Ac4_Sample_ShouldDecrypt()
    {
        Assert.True(File.Exists(Ac4SamplePath), $"テストデータが見つからない: {Ac4SamplePath}");

        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var ds = new DecryptionService();
            var result = await ds.DecryptAsync(
                Ac4SamplePath, outputDir, Ac4SamplePassword, new DecryptionOptions());

            Assert.True(result.Success, $"復号失敗: {result.ErrorMessage}");

            var decrypted = Path.Combine(outputDir, "sample.txt");
            Assert.True(File.Exists(decrypted), "sample.txt が出力されていない");
            Assert.NotEmpty(await File.ReadAllTextAsync(decrypted));
        }
        finally
        {
            try { Directory.Delete(outputDir, true); } catch { /* ignored */ }
        }
    }

    /// <summary>
    /// v140 でも、誤ったパスワードはきちんと弾かれること。
    /// </summary>
    [Fact]
    public async Task Ac4_Sample_WrongPassword_ShouldFail()
    {
        Assert.True(File.Exists(Ac4SamplePath), $"テストデータが見つからない: {Ac4SamplePath}");

        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var ds = new DecryptionService();
            var result = await ds.DecryptAsync(
                Ac4SamplePath, outputDir, "WRONG_PASSWORD", new DecryptionOptions());

            Assert.False(result.Success, "誤ったパスワードでも成功してしまっている");
        }
        finally
        {
            try { Directory.Delete(outputDir, true); } catch { /* ignored */ }
        }
    }

    /// <summary>
    /// Mac 版で AC5 (v150) 暗号化 → Mac 版で復号 のラウンドトリップが、
    /// パスワードのみ／圧縮ありで正しく機能することを確認する。
    /// </summary>
    [Fact]
    public async Task Ac5_RoundTrip_WithCompression_ShouldWork()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var input = Path.Combine(tempDir, "input.txt");
            var encrypted = Path.Combine(tempDir, "out.atc");
            var outputDir = Path.Combine(tempDir, "decrypted");

            // 圧縮性のあるデータを作る
            var data = new string('A', 10_000) + "🎉" + new string('B', 10_000) + "日本語テスト";
            await File.WriteAllTextAsync(input, data);

            var es = new EncryptionService();
            var enc = await es.EncryptAsync(new[] { input }, encrypted, Password,
                new EncryptionOptions { CompressionLevel = System.IO.Compression.CompressionLevel.Optimal });
            Assert.True(enc.Success, $"暗号化失敗: {enc.ErrorMessage}");

            var ds = new DecryptionService();
            var dec = await ds.DecryptAsync(encrypted, outputDir, Password, new DecryptionOptions());
            Assert.True(dec.Success, $"復号失敗: {dec.ErrorMessage}");

            var files = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
            var content = await File.ReadAllTextAsync(files[0]);
            Assert.Equal(data, content);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignored */ }
        }
    }

    /// <summary>
    /// UI 経路と同じく GetFileListAsync → DecryptAsync を順に呼ぶラウンドトリップ。
    /// （DecryptionViewModel.ExecuteDecryptionAsync は GetFileListAsync を先に呼ぶ。
    /// v150 サポートがなかった頃はここで誤って "パスワード違い" 判定されていた）
    /// </summary>
    [Fact]
    public async Task Ac5_GetFileListThenDecrypt_ShouldWork()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var input = Path.Combine(tempDir, "hello.txt");
            var encrypted = Path.Combine(tempDir, "out.atc");
            var outputDir = Path.Combine(tempDir, "decrypted");
            await File.WriteAllTextAsync(input, "hello world\n日本語OK");

            var es = new EncryptionService();
            var enc = await es.EncryptAsync(new[] { input }, encrypted, Password, new EncryptionOptions());
            Assert.True(enc.Success);

            var ds = new DecryptionService();
            // 1) GetFileListAsync が v150 を理解しているか
            var listResult = await ds.GetFileListAsync(encrypted, Password, new DecryptionOptions(), CancellationToken.None);
            Assert.True(listResult.Success,
                $"GetFileListAsync 失敗: {listResult.ErrorMessage} (code={listResult.ErrorCode})");
            Assert.NotEmpty(listResult.FilePaths);

            // 2) 続けて DecryptAsync
            var dec = await ds.DecryptAsync(encrypted, outputDir, Password, new DecryptionOptions());
            Assert.True(dec.Success, $"DecryptAsync 失敗: {dec.ErrorMessage}");
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    /// <summary>
    /// パスワード空文字列でも暗号化→ファイルリスト取得→復号が通ること。
    /// AttacheCase は空パスワードを許容している。
    /// </summary>
    [Fact]
    public async Task Ac5_EmptyPassword_ShouldRoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var input = Path.Combine(tempDir, "input.txt");
            var encrypted = Path.Combine(tempDir, "out.atc");
            var outputDir = Path.Combine(tempDir, "decrypted");
            await File.WriteAllTextAsync(input, "no password");

            var es = new EncryptionService();
            var enc = await es.EncryptAsync(new[] { input }, encrypted, string.Empty, new EncryptionOptions());
            Assert.True(enc.Success, $"暗号化失敗: {enc.ErrorMessage}");

            var ds = new DecryptionService();
            var listResult = await ds.GetFileListAsync(encrypted, string.Empty, new DecryptionOptions(), CancellationToken.None);
            Assert.True(listResult.Success, $"GetFileListAsync 失敗: {listResult.ErrorMessage}");

            var dec = await ds.DecryptAsync(encrypted, outputDir, string.Empty, new DecryptionOptions());
            Assert.True(dec.Success, $"DecryptAsync 失敗: {dec.ErrorMessage}");
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    /// <summary>
    /// 誤ったパスワードが認証タグ検証で確実に弾かれることを確認する。
    /// </summary>
    [Fact]
    public async Task Ac5_WrongPassword_ShouldFail()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var input = Path.Combine(tempDir, "input.txt");
            var encrypted = Path.Combine(tempDir, "out.atc");
            var outputDir = Path.Combine(tempDir, "decrypted");

            await File.WriteAllTextAsync(input, "secret content");

            var es = new EncryptionService();
            var enc = await es.EncryptAsync(new[] { input }, encrypted, "correct_pw", new EncryptionOptions());
            Assert.True(enc.Success);

            var ds = new DecryptionService();
            var dec = await ds.DecryptAsync(encrypted, outputDir, "WRONG_PASSWORD", new DecryptionOptions());
            Assert.False(dec.Success, "誤ったパスワードでも成功してしまっている");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignored */ }
        }
    }
}
