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

//----------------------------------------------------------------------
// 復号の「上書きしない」指定（Skip / SkipAll ＝ 確認ダイアログを出せない実行での
// 「同名ファイルがあるときは上書きの確認をする」）。
// 保存先に同名ファイルが 1 つでもあれば、何も書き出す前に OUTPUT_FILE_EXISTS で止まり
// （CLI では暗号化側と同じ終了コード 5）、既存ファイルも他のファイルも触らない。
// v5 / v4 / 旧形式（v1〜v3）/ パスワード ZIP のすべての経路で同じ。
// GUI の既定（Ask）は復号画面が自前で確認するので、サービスの挙動は変えない。
//----------------------------------------------------------------------

#pragma warning disable CS0618 // ZipPasswordDecryptionService.DecryptAsync は Obsolete 指定のまま現役

using System.Text;
using AttacheCase.Cli;
using AttacheCase.Headless;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.Tests;

public class DecryptionOverwriteTests : IDisposable
{
    private const string Password = "test-password";
    private const string Original = "ORIGINAL CONTENT (must survive)";

    private readonly string _dir = Directory.CreateTempSubdirectory("atc-overwrite").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch { /* 一時フォルダーの片付けに失敗しても、テストの結果には関係ない */ }
    }

    private string Source(string name, string content)
    {
        var dir = Path.Combine(_dir, "src");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private string OutputFolder(string name)
    {
        var dir = Path.Combine(_dir, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task<string> EncryptAtcAsync(params string[] inputs)
    {
        var atc = Path.Combine(_dir, "archive.atc");
        var result = await new EncryptionService().EncryptAsync(inputs, atc, Password, new EncryptionOptions());
        Assert.True(result.Success, result.ErrorMessage);
        return atc;
    }

    private static Task<EncryptionResult> DecryptAtcAsync(string atc, string outDir, OverwriteOption overwrite)
        => new DecryptionService().DecryptAsync(
            atc, outDir, Password, new DecryptionOptions { OverwriteOption = overwrite });

    /// <summary>出力フォルダーにあるファイル（相対パス、並び順固定）</summary>
    private static string[] FilesIn(string dir) => Directory.Exists(dir)
        ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(dir, p)).Order().ToArray()
        : [];

    /// <summary>書庫を一度展開して、復号結果の相対パスの並びを知る（書庫の中の配置に依存しないため）</summary>
    private async Task<string[]> LayoutOfAsync(string atc)
    {
        var probe = OutputFolder("layout");
        var result = await DecryptAtcAsync(atc, probe, OverwriteOption.OverwriteAll);
        Assert.True(result.Success, result.ErrorMessage);
        return FilesIn(probe);
    }

    private static void AssertStoppedBeforeWriting(EncryptionResult result, string outDir, string existingPath)
    {
        Assert.False(result.Success, "同名ファイルがあるのに復号が成功した");
        Assert.Equal(ErrorCodes.OUTPUT_FILE_EXISTS, result.ErrorCode);
        Assert.Equal(Path.GetFullPath(existingPath), Path.GetFullPath(result.ErrorFilePath));
        Assert.Contains(Path.GetFileName(existingPath), result.ErrorMessage);
        Assert.Equal(Original, File.ReadAllText(existingPath, Encoding.UTF8));
        Assert.Equal([Path.GetRelativePath(outDir, existingPath)], FilesIn(outDir));
        Assert.Equal(AtcExitCode.OutputExists, AtcExitCode.FromEngineReturnCode(result.ErrorCode));
    }

    //======================================================================
    // v5（v150）
    //======================================================================

    [Fact]
    public async Task V5_SkipAll_ExistingFile_StopsBeforeWritingAnything()
    {
        var atc = await EncryptAtcAsync(Source("a.txt", "AAAA"), Source("b.txt", "BBBB"));
        var layout = await LayoutOfAsync(atc);
        Assert.Equal(2, layout.Length);

        // 2 つ目のファイルだけ保存先に先にある → 1 つ目も書かずに止まる
        var outDir = OutputFolder("out");
        var existing = Path.Combine(outDir, layout.Single(p => p.EndsWith("b.txt")));
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, Original, Encoding.UTF8);

        var result = await DecryptAtcAsync(atc, outDir, OverwriteOption.SkipAll);

        AssertStoppedBeforeWriting(result, outDir, existing);
        Assert.Empty(Directory.EnumerateFiles(outDir, "*.attmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task V5_SkipAll_NoConflict_Succeeds()
    {
        var atc = await EncryptAtcAsync(Source("a.txt", "AAAA"));
        var outDir = OutputFolder("out");

        var result = await DecryptAtcAsync(atc, outDir, OverwriteOption.SkipAll);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Single(FilesIn(outDir));
    }

    [Fact]
    public async Task V5_OverwriteAll_ReplacesTheExistingFile()
    {
        var atc = await EncryptAtcAsync(Source("a.txt", "AAAA"));
        var layout = await LayoutOfAsync(atc);
        var outDir = OutputFolder("out");
        var existing = Path.Combine(outDir, layout.Single());
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, Original, Encoding.UTF8);

        var result = await DecryptAtcAsync(atc, outDir, OverwriteOption.OverwriteAll);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("AAAA", File.ReadAllText(existing, Encoding.UTF8));
    }

    [Fact]
    public async Task V5_Ask_IsTheGuiPath_AndStillOverwrites()
    {
        // GUI は復号画面で確認してからサービスを呼ぶ（既定の Ask）。サービス側の挙動は変えない
        var atc = await EncryptAtcAsync(Source("a.txt", "AAAA"));
        var layout = await LayoutOfAsync(atc);
        var outDir = OutputFolder("out");
        var existing = Path.Combine(outDir, layout.Single());
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, Original, Encoding.UTF8);

        var result = await DecryptAtcAsync(atc, outDir, OverwriteOption.Ask);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("AAAA", File.ReadAllText(existing, Encoding.UTF8));
    }

    //======================================================================
    // 旧形式（v105 / v130 は移植エンジン、v140 は DecryptionService の v4 経路）
    //======================================================================

    [Theory]
    [InlineData("v105")]
    [InlineData("v130")]
    [InlineData("v140")]
    public async Task Legacy_SkipAll_ExistingFile_StopsBeforeWriting(string label)
    {
        var atcPath = Path.Combine(AppContext.BaseDirectory, "TestData", label, "legacy.atc");
        Assert.True(File.Exists(atcPath), "検体がありません: " + atcPath);

        var outDir = OutputFolder("out-" + label);
        var existing = Path.Combine(outDir, "legacy.txt");
        File.WriteAllText(existing, Original, Encoding.UTF8);

        var result = await new DecryptionService().DecryptAsync(
            atcPath, outDir, "DogfoodPassw0rd!",
            new DecryptionOptions
            {
                NoParentFolder = true,
                RestoreTimeStamp = true,
                OverwriteOption = OverwriteOption.SkipAll,
            });

        AssertStoppedBeforeWriting(result, outDir, existing);
    }

    //======================================================================
    // パスワード ZIP
    //======================================================================

    [Fact]
    public async Task Zip_SkipAll_ExistingFile_StopsBeforeWritingAnything()
    {
        var zip = Path.Combine(_dir, "archive.zip");
        var created = await new ZipPasswordEncryptionService(ZipPasswordEncryptionMethod.Aes256).EncryptAsync(
            [Source("a.txt", "AAAA"), Source("b.txt", "BBBB")], zip, Password, new EncryptionOptions());
        Assert.True(created.Success, created.ErrorMessage);

        var outDir = OutputFolder("out");
        var existing = Path.Combine(outDir, "b.txt");
        File.WriteAllText(existing, Original, Encoding.UTF8);

        var result = await new ZipPasswordDecryptionService().DecryptAsync(
            zip, outDir, Password, new DecryptionOptions { OverwriteOption = OverwriteOption.SkipAll });

        AssertStoppedBeforeWriting(result, outDir, existing);

        var overwritten = await new ZipPasswordDecryptionService().DecryptAsync(
            zip, outDir, Password, new DecryptionOptions { OverwriteOption = OverwriteOption.OverwriteAll });
        Assert.True(overwritten.Success, overwritten.ErrorMessage);
        Assert.Equal("BBBB", File.ReadAllText(existing, Encoding.UTF8));
    }

    //======================================================================
    // CLI（HeadlessRunner）: 「上書きを確認する」＝ Overwrite=false は終了コード 5
    //======================================================================

    [Fact]
    public async Task Headless_WithoutOverwrite_ExistingFile_ExitsWithOutputExists()
    {
        var atc = await EncryptAtcAsync(Source("a.txt", "AAAA"));
        var layout = await LayoutOfAsync(atc);
        var outDir = OutputFolder("out");
        var existing = Path.Combine(outDir, layout.Single());
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, Original, Encoding.UTF8);

        var refused = new HeadlessRunner().Decrypt(new HeadlessDecryptRequest
        {
            InputFilePath = atc,
            OutputDirectory = outDir,
            Password = Password,
            Overwrite = false,
        });

        Assert.False(refused.Succeeded);
        Assert.Equal(AtcExitCode.OutputExists, refused.ExitCode);
        Assert.Equal(Path.GetFullPath(existing), Path.GetFullPath(refused.ErrorFilePath));
        Assert.Equal(Original, File.ReadAllText(existing, Encoding.UTF8));

        var allowed = new HeadlessRunner().Decrypt(new HeadlessDecryptRequest
        {
            InputFilePath = atc,
            OutputDirectory = outDir,
            Password = Password,
            Overwrite = true,
        });

        Assert.True(allowed.Succeeded, allowed.ErrorMessage);
        Assert.Equal(AtcExitCode.Success, allowed.ExitCode);
        Assert.Equal("AAAA", File.ReadAllText(existing, Encoding.UTF8));
    }
}
