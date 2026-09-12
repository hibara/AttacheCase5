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
// パスワード入力制限と、制限超過時のファイル破壊のテスト。
//
// 破壊の手順は Windows 版 AttacheCase の BreakTheFile に合わせてある:
//   - 4 バイト目のトークンを「_Atc_Broken_Data」へ書き換える
//   - 32 バイト目から 32 バイトを非ゼロの乱数でつぶす
//----------------------------------------------------------------------

using System.Text;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.Tests;

public class BreakFileTests
{
    private const string TokenNormal = "_AttacheCaseData";
    private const string TokenBroken = "_Atc_Broken_Data";

    private static async Task<string> CreateEncryptedFileAsync(
        string directory, int missTypeLimits, bool breakOnLimit)
    {
        var source = Path.Combine(directory, "source.txt");
        File.WriteAllText(source, "break test");

        var encrypted = Path.Combine(directory, "source.atc");

        var result = await new EncryptionService().EncryptAsync(
            [source], encrypted, "pw",
            new EncryptionOptions
            {
                MissTypeLimits = missTypeLimits,
                BreakFileWhenLimitExceeded = breakOnLimit,
            });

        Assert.True(result.Success, result.ErrorMessage);
        return encrypted;
    }

    [Fact]
    public async Task TheLimitAndTheFlagAreStoredInTheFile()
    {
        var directory = Directory.CreateTempSubdirectory("atc-break").FullName;
        try
        {
            var encrypted = await CreateEncryptedFileAsync(directory, 7, true);

            var (limit, breakOnLimit) = DecryptionService.ReadPasswordLimit(encrypted);

            Assert.Equal(7, limit);
            Assert.True(breakOnLimit);

            // ヘッダの並びが Windows 版と同じであること
            var head = File.ReadAllBytes(encrypted).AsSpan(0, 20);
            Assert.Equal(7, head[2]);
            Assert.Equal(1, head[3]);
            Assert.Equal(TokenNormal, Encoding.ASCII.GetString(head[4..20]));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task WithoutTheFlag_TheByteStaysZero()
    {
        var directory = Directory.CreateTempSubdirectory("atc-break").FullName;
        try
        {
            var encrypted = await CreateEncryptedFileAsync(directory, 3, false);

            var (limit, breakOnLimit) = DecryptionService.ReadPasswordLimit(encrypted);

            Assert.Equal(3, limit);
            Assert.False(breakOnLimit);
            Assert.Equal(0, File.ReadAllBytes(encrypted)[3]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task BreakingRewritesTheTokenAndDestroysTheKeyBlock()
    {
        var directory = Directory.CreateTempSubdirectory("atc-break").FullName;
        try
        {
            var encrypted = await CreateEncryptedFileAsync(directory, 3, true);
            var before = File.ReadAllBytes(encrypted);

            Assert.True(DecryptionService.BreakTheFile(encrypted));

            var after = File.ReadAllBytes(encrypted);

            // トークンが書き換わっている
            Assert.Equal(TokenBroken, Encoding.ASCII.GetString(after, 4, 16));

            // 32 バイト目から 32 バイトが非ゼロの乱数でつぶされている
            Assert.NotEqual(before[32..64], after[32..64]);
            Assert.DoesNotContain((byte)0, after[32..64]);

            // それ以外（本体）は触っていない
            Assert.Equal(before.Length, after.Length);
            Assert.Equal(before[64..], after[64..]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ABrokenFileCannotBeDecryptedAnyMore()
    {
        var directory = Directory.CreateTempSubdirectory("atc-break").FullName;
        try
        {
            var encrypted = await CreateEncryptedFileAsync(directory, 3, true);

            // 破壊前は正しいパスワードで復号できる
            var okDirectory = Path.Combine(directory, "ok");
            var before = await new DecryptionService().DecryptAsync(
                encrypted, okDirectory, "pw", new DecryptionOptions());
            Assert.True(before.Success, before.ErrorMessage);

            DecryptionService.BreakTheFile(encrypted);

            // 破壊後は正しいパスワードでも復号できない
            var ngDirectory = Path.Combine(directory, "ng");
            var after = await new DecryptionService().DecryptAsync(
                encrypted, ngDirectory, "pw", new DecryptionOptions());

            Assert.False(after.Success);
            Assert.Equal(ErrorCodes.ATC_BROKEN_DATA, after.ErrorCode);

            // ファイル一覧の取得も断られる
            var list = await new DecryptionService().GetFileListAsync(encrypted, "pw");
            Assert.False(list.Success);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task BreakingTwiceIsHarmless()
    {
        var directory = Directory.CreateTempSubdirectory("atc-break").FullName;
        try
        {
            var encrypted = await CreateEncryptedFileAsync(directory, 3, true);

            Assert.True(DecryptionService.BreakTheFile(encrypted));
            var afterFirst = File.ReadAllBytes(encrypted);

            Assert.True(DecryptionService.BreakTheFile(encrypted));

            // すでに破壊済みなので、中身は変わらない
            Assert.Equal(afterFirst, File.ReadAllBytes(encrypted));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void APlainFileIsNotTouched()
    {
        var directory = Directory.CreateTempSubdirectory("atc-break").FullName;
        try
        {
            var file = Path.Combine(directory, "plain.txt");
            File.WriteAllText(file, new string('x', 128));
            var before = File.ReadAllBytes(file);

            Assert.False(DecryptionService.BreakTheFile(file));
            Assert.Equal(before, File.ReadAllBytes(file));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TheCommandLineOptionMapsToTheSetting()
    {
        var effective = AttacheCase.Cli.CliParser.Parse(["/breakfile=1", "/typelimit=5"])
            .ApplyTo(new AppSettings());

        Assert.True(effective.BreakFileWhenLimitExceeded);
        Assert.Equal(5, effective.MissTypeLimits);
        Assert.Empty(AttacheCase.Cli.CliParser.Parse(["/breakfile=1"]).IgnoredOptions);
    }
}
