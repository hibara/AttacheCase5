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
// コマンドライン関連の動作テスト。
//   - 自動ファイル名（/autoname）の書式展開
//   - 出力先の決定規則（/out, /saveto, /camoext, /withext）
//   - 元ファイルの削除（/del）
//   - パスワードファイル（/pfile, /dpfile）による暗号化・復号の往復
//----------------------------------------------------------------------

using System.Text;
using AttacheCase.Cli;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.Tests;

public class AutoFileNameTests
{
    [Fact]
    public void FileNameAndExtension_AreExpanded()
    {
        var path = Path.Combine(Path.GetTempPath(), "report.atc");

        var result = AutoFileName.Format("<filename>_backup<ext>", path, AutoNameCharset.Default, out var invalid);

        Assert.False(invalid);
        Assert.Equal("report_backup.atc", result);
    }

    [Fact]
    public void DateSpecifier_UsesTheDotNetFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), "report.atc");
        var expected = $"report_{DateTime.Now:yyyyMMdd}.atc";

        var result = AutoFileName.Format(
            "<filename>_<date:yyyyMMdd><ext>", path, AutoNameCharset.Default, out _);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void RandomSpecifier_ProducesTheRequestedLength()
    {
        var path = Path.Combine(Path.GetTempPath(), "report.atc");

        var result = AutoFileName.Format("<random:12><ext>", path, AutoNameCharset.Default, out _);

        Assert.Equal(12 + ".atc".Length, result.Length);
        Assert.All(result[..12], c => Assert.True(char.IsLetterOrDigit(c)));
    }

    [Fact]
    public void SerialNumber_SkipsAnExistingFile()
    {
        var directory = Directory.CreateTempSubdirectory("atc-autoname").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "report_001.atc"), "existing");
            var path = Path.Combine(directory, "report.atc");

            var result = AutoFileName.Format(
                "<filename>_<num:3><ext>", path, AutoNameCharset.Default, out _);

            Assert.Equal("report_002.atc", result);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void InvalidCharacter_IsReported()
    {
        var path = Path.Combine(Path.GetTempPath(), "report.atc");

        AutoFileName.Format("sub/dir<ext>", path, AutoNameCharset.Default, out var invalid);

        Assert.True(invalid);
    }
}

public class CliOutputPathTests
{
    /// <summary>引数を「既定の設定 + 上書き」の形にして実行条件を作る</summary>
    private static RunContext Ctx(params string[] args)
    {
        var cli = CliParser.Parse(args);
        Assert.Null(cli.ParseError);

        return new RunContext
        {
            Settings = cli.ApplyTo(new AppSettings()),
            IsCommandLineRun = true,
            OutputPath = cli.OutputPath,
        };
    }

    [Fact]
    public void WithoutOptions_TheOutputSitsNextToTheInput()
    {
        var input = Path.Combine(Path.GetTempPath(), "report.xlsx");

        var result = CliOutputPath.ForEncryption([input], Ctx());

        Assert.Equal(Path.Combine(Path.GetTempPath(), "report.atc"), result);
    }

    [Fact]
    public void WithExt_KeepsTheOriginalExtension()
    {
        var input = Path.Combine(Path.GetTempPath(), "report.xlsx");
        var ctx = Ctx("/withext=1");
var result = CliOutputPath.ForEncryption([input], ctx);

        Assert.Equal(Path.Combine(Path.GetTempPath(), "report.xlsx.atc"), result);
    }

    [Fact]
    public void CamouflageExtension_ReplacesAtc()
    {
        var input = Path.Combine(Path.GetTempPath(), "report.xlsx");
        var ctx = Ctx("/camoext=jpg");
var result = CliOutputPath.ForEncryption([input], ctx);

        Assert.Equal(Path.Combine(Path.GetTempPath(), "report.jpg"), result);
    }

    [Fact]
    public void SaveTo_ChangesTheFolderOnly()
    {
        var directory = Directory.CreateTempSubdirectory("atc-saveto").FullName;
        try
        {
            var input = Path.Combine(Path.GetTempPath(), "report.xlsx");
            var ctx = Ctx("/saveto=" + directory);
var result = CliOutputPath.ForEncryption([input], ctx);

            Assert.Equal(Path.Combine(directory, "report.atc"), result);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void OutWithFileName_IsUsedAsIs_IgnoringAutoName()
    {
        var input = Path.Combine(Path.GetTempPath(), "report.xlsx");
        var explicitOutput = Path.Combine(Path.GetTempPath(), "fixed-name.atc");
        var ctx = Ctx("/out=" + explicitOutput, "/autoname=<filename>_x<ext>");
var result = CliOutputPath.ForEncryption([input], ctx);

        Assert.Equal(explicitOutput, result);
    }

    [Fact]
    public void OneByOne_WithAFileNameInOut_IsRejected()
    {
        var input = Path.Combine(Path.GetTempPath(), "report.xlsx");
        var ctx = Ctx("/oneby=1", "/out=" + Path.Combine(Path.GetTempPath(), "fixed.atc"));
Assert.Throws<InvalidOperationException>(() => CliOutputPath.ForEncryption([input], ctx));
    }

    [Fact]
    public void MultipleInputs_WithAFileNameInOut_ArePackedIntoThatFile()
    {
        // 「複数ファイルの場合」の既定は通常（入力 1 つにつき 1 つ）だが、/out=<ファイル名> で
        // 出力が 1 つに明示されていれば「一つにまとめる」の意図とみなす（旧 5.0 CLI の /list + /out、
        // 回帰 F-03 と同じ）。/oneby だけは矛盾として拒否する
        var a = Path.Combine(Path.GetTempPath(), "a.txt");
        var b = Path.Combine(Path.GetTempPath(), "b.txt");
        var fixedName = Path.Combine(Path.GetTempPath(), "fixed.atc");

        Assert.Equal(fixedName, CliOutputPath.ForEncryption([a, b], Ctx("/out=" + fixedName)));
        Assert.Equal(fixedName, CliOutputPath.ForEncryption([a, b], Ctx("/allpack=1", "/out=" + fixedName)));
        Assert.Throws<InvalidOperationException>(
            () => CliOutputPath.ForEncryption([a, b], Ctx("/oneby=1", "/out=" + fixedName)));
    }
}

public class FileEraserTests
{
    [Fact]
    public void Delete_RemovesTheFile()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(file, "content");

        Assert.True(FileEraser.Erase(file, FileEraser.Mode.Delete));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Wipe_OverwritesAndRemovesTheFile()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(file, new string('A', 100_000));

        Assert.True(FileEraser.Erase(file, FileEraser.Mode.Wipe, randomTimes: 2, zeroTimes: 1));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Wipe_HandlesAWholeFolder()
    {
        var directory = Directory.CreateTempSubdirectory("atc-wipe").FullName;
        Directory.CreateDirectory(Path.Combine(directory, "sub"));
        File.WriteAllText(Path.Combine(directory, "a.txt"), "a");
        File.WriteAllText(Path.Combine(directory, "sub", "b.txt"), "b");

        Assert.True(FileEraser.Erase(directory, FileEraser.Mode.Wipe));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void MissingTarget_IsNotAnError()
    {
        var file = Path.Combine(Path.GetTempPath(), "no-such-file-" + Path.GetRandomFileName());

        Assert.True(FileEraser.Erase(file, FileEraser.Mode.Wipe));
    }
}

public class PasswordFileTests
{
    [Fact]
    public void Sha256_MatchesTheHashOfTheFileContent()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var content = Encoding.UTF8.GetBytes("password file content");
        File.WriteAllBytes(file, content);
        try
        {
            var expected = System.Security.Cryptography.SHA256.HashData(content);

            Assert.Equal(expected, PasswordFileHash.ComputeSha256(file));
            Assert.Equal(expected, PasswordFileHash.ComputeForDataFileVersion(file, 150));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task EncryptAndDecrypt_WithAPasswordFile_RoundTrips()
    {
        var workDir = Directory.CreateTempSubdirectory("atc-pfile").FullName;
        try
        {
            // パスワードファイルと平文を用意する
            var passwordFile = Path.Combine(workDir, "key.bin");
            File.WriteAllBytes(passwordFile, Enumerable.Range(0, 512).Select(i => (byte)i).ToArray());

            var source = Path.Combine(workDir, "source.txt");
            var body = "パスワードファイルによる往復テスト";
            File.WriteAllText(source, body, new UTF8Encoding(false));

            var keyMaterial = PasswordFileHash.ComputeSha256(passwordFile);
            var encrypted = Path.Combine(workDir, "source.atc");

            var encryptResult = await new EncryptionService().EncryptAsync(
                [source],
                encrypted,
                string.Empty,
                new EncryptionOptions { PasswordBinary = keyMaterial });

            Assert.True(encryptResult.Success, encryptResult.ErrorMessage);

            // 文字列パスワード（空文字）では復号できないこと
            var outputWrong = Path.Combine(workDir, "wrong");
            var wrongResult = await new DecryptionService().DecryptAsync(
                encrypted, outputWrong, string.Empty, new DecryptionOptions());
            Assert.False(wrongResult.Success);

            // 同じパスワードファイルからなら復号できること
            var output = Path.Combine(workDir, "out");
            var decryptResult = await new DecryptionService().DecryptAsync(
                encrypted,
                output,
                string.Empty,
                new DecryptionOptions { PasswordBinary = keyMaterial });

            Assert.True(decryptResult.Success, decryptResult.ErrorMessage);
            Assert.Equal(body, File.ReadAllText(Path.Combine(output, "source.txt")));
        }
        finally
        {
            Directory.Delete(workDir, true);
        }
    }
}
