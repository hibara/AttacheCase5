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
// 旧形式（v105 / v130 / v140）の復号互換テスト。
//
// TestData\v105 / v130 / v140 の検体は、それぞれ本物の旧バージョン
// （AttacheCase 2 / 3 / 4 の Windows 実行ファイル）で生成した。
// パスワードは "DogfoodPassw0rd!"、平文は legacy.txt（乱数 16 KB）。
// 再生成するときは dogfooding\RegressionTest\run-tests.ps1 -RefreshFixtures を
// 実行し、%TEMP%\atc_regress_fixtures からコピーする。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AttacheCase.Models;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class LegacyFormatTests
{
    private const string Password = "DogfoodPassw0rd!";

    [Theory]
    [InlineData("v105")]
    [InlineData("v130")]
    [InlineData("v140")]
    public async Task 旧形式の検体を復号して内容が一致する(string label)
    {
        var atcPath = Path.Combine(AppContext.BaseDirectory, "TestData", label, "legacy.atc");
        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", label, "legacy.txt");
        Assert.True(File.Exists(atcPath), "検体がありません: " + atcPath);

        var outDir = Directory.CreateTempSubdirectory("atc-legacy-" + label).FullName;
        try
        {
            var service = new DecryptionService();
            var result = await service.DecryptAsync(
                atcPath, outDir, Password,
                new DecryptionOptions
                {
                    NoParentFolder = true,
                    RestoreTimeStamp = true,
                    OverwriteOption = OverwriteOption.OverwriteAll,
                });

            Assert.True(result.Success, $"復号に失敗: code={result.ErrorCode} {result.ErrorMessage}");

            var outFile = Path.Combine(outDir, "legacy.txt");
            Assert.True(File.Exists(outFile), "復号結果がありません: " + outFile);

            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(srcPath))),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outFile))));
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }

    [Theory]
    [InlineData("v105", 105)]
    [InlineData("v130", 130)]
    [InlineData("v140", 140)]
    public void 旧形式の検体のバージョンを判定できる(string label, int expected)
    {
        var atcPath = Path.Combine(AppContext.BaseDirectory, "TestData", label, "legacy.atc");
        Assert.Equal(expected, Headless.HeadlessProbe.GetDataFileVersion(atcPath));
    }

    [Theory]
    [InlineData("v105")]
    [InlineData("v130")]
    public async Task 旧形式でも誤ったパスワードは復号に失敗する(string label)
    {
        var atcPath = Path.Combine(AppContext.BaseDirectory, "TestData", label, "legacy.atc");
        var outDir = Directory.CreateTempSubdirectory("atc-legacy-wrong-" + label).FullName;
        try
        {
            var service = new DecryptionService();
            var result = await service.DecryptAsync(
                atcPath, outDir, "WrongPassword!",
                new DecryptionOptions { NoParentFolder = true });

            Assert.False(result.Success);
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }
}
