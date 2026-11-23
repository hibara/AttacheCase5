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
//
// TestData\v105 / v130 の legacy_tree.atc は、サブフォルダー入りのフォルダー
// （legacy_tree\root.txt、sub\child.txt、sub\deep\leaf.txt、空の empty\）を
// 同じ旧バージョンの実行ファイルで暗号化したもの（2026-11-23 生成、パスワードは同じ）。
// 元の内容は TestData\<版>\legacy_tree\ にある（空フォルダーは git に入らないので無い）。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
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

    // GUI（DecryptionViewModel）は復号の前に、上書き確認用の一覧を GetFileListAsync で取る。
    // ここが旧形式を読めないと「パスワード違い」になり、GUI からは復号まで届かない
    // （5.5.2 までの不具合。コマンドライン版は一覧を取らないので復号できていた）。
    [Theory]
    [InlineData("v105")]
    [InlineData("v130")]
    [InlineData("v140")]
    public async Task 旧形式の検体のファイル一覧を取得できる(string label)
    {
        var atcPath = Path.Combine(AppContext.BaseDirectory, "TestData", label, "legacy.atc");
        var before = File.ReadAllBytes(atcPath);

        var result = await new DecryptionService().GetFileListAsync(
            atcPath, Password, new DecryptionOptions(), CancellationToken.None);

        Assert.True(result.Success, $"一覧の取得に失敗: code={result.ErrorCode} {result.ErrorMessage}");
        Assert.Contains(result.FilePaths, p => Path.GetFileName(p) == "legacy.txt");
        // 保存先からの相対パスで返る（絶対パスだと、画面の上書き確認が別の場所を見てしまう）
        Assert.All(result.FilePaths, p => Assert.False(Path.IsPathRooted(p), p));

        // 引数 2 つの版も同じ
        var simple = await new DecryptionService().GetFileListAsync(atcPath, Password);
        Assert.True(simple.Success, $"一覧の取得に失敗: code={simple.ErrorCode} {simple.ErrorMessage}");

        // 一覧を取るだけなので、暗号化ファイルには触らない
        Assert.Equal(before, File.ReadAllBytes(atcPath));
    }

    // 旧形式の項目名は区切りが「\」（Windows 版しか書かない）。Windows 以外でそのまま保存先に
    // 結合すると、階層にならず「sub\child.txt」という名前のファイルができる（5.5.2 までの Mac）。
    [Theory]
    [InlineData("v105", false)]
    [InlineData("v105", true)]
    [InlineData("v130", false)]
    [InlineData("v130", true)]
    public async Task サブフォルダー入りの旧形式の検体をフォルダー階層のまま復号できる(string label, bool noParentFolder)
    {
        var atcPath = Path.Combine(AppContext.BaseDirectory, "TestData", label, "legacy_tree.atc");
        var srcRoot = Path.Combine(AppContext.BaseDirectory, "TestData", label, "legacy_tree");
        Assert.True(File.Exists(atcPath), "検体がありません: " + atcPath);

        var outDir = Directory.CreateTempSubdirectory("atc-legacy-tree-" + label).FullName;
        try
        {
            var result = await new DecryptionService().DecryptAsync(
                atcPath, outDir, Password,
                new DecryptionOptions
                {
                    NoParentFolder = noParentFolder,
                    OverwriteOption = OverwriteOption.OverwriteAll,
                });

            Assert.True(result.Success, $"復号に失敗: code={result.ErrorCode} {result.ErrorMessage}");

            var outRoot = noParentFolder ? outDir : Path.Combine(outDir, "legacy_tree");
            foreach (var parts in new[]
                     {
                         new[] { "root.txt" },
                         new[] { "sub", "child.txt" },
                         new[] { "sub", "deep", "leaf.txt" },
                     })
            {
                var relative = Path.Combine(parts);
                var outFile = Path.Combine(outRoot, relative);
                Assert.True(File.Exists(outFile), "復号結果がありません: " + outFile);
                Assert.Equal(
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(srcRoot, relative)))),
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outFile))));
            }

            // 空のフォルダーもフォルダーとして出る
            Assert.True(Directory.Exists(Path.Combine(outRoot, "empty")), "空のフォルダーがありません");

            // 「\」を名前に含む項目（階層にならなかった残骸）が 1 つも無い
            Assert.DoesNotContain(
                Directory.EnumerateFileSystemEntries(outDir, "*", SearchOption.AllDirectories),
                p => Path.GetRelativePath(outDir, p).Replace(Path.DirectorySeparatorChar, '/').Contains('\\'));
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }

    // 一覧（画面の上書き確認が見る場所）は、エンジンが実際に書き出す場所と同じでなければならない
    [Theory]
    [InlineData("v105")]
    [InlineData("v130")]
    public async Task サブフォルダー入りの旧形式の一覧は実際に書き出される場所と一致する(string label)
    {
        var atcPath = Path.Combine(AppContext.BaseDirectory, "TestData", label, "legacy_tree.atc");

        var list = await new DecryptionService().GetFileListAsync(
            atcPath, Password, new DecryptionOptions(), CancellationToken.None);
        Assert.True(list.Success, $"一覧の取得に失敗: code={list.ErrorCode} {list.ErrorMessage}");
        Assert.All(list.FilePaths, p => Assert.False(Path.IsPathRooted(p), p));

        var trimmed = list.FilePaths
            .Select(p => p.TrimEnd(Path.DirectorySeparatorChar))
            .ToList();
        Assert.Contains(Path.Combine("legacy_tree", "root.txt"), trimmed);
        Assert.Contains(Path.Combine("legacy_tree", "sub", "child.txt"), trimmed);
        Assert.Contains(Path.Combine("legacy_tree", "sub", "deep", "leaf.txt"), trimmed);
        Assert.Contains(Path.Combine("legacy_tree", "empty"), trimmed);

        var outDir = Directory.CreateTempSubdirectory("atc-legacy-tree-list-" + label).FullName;
        try
        {
            var result = await new DecryptionService().DecryptAsync(
                atcPath, outDir, Password,
                new DecryptionOptions { OverwriteOption = OverwriteOption.OverwriteAll });
            Assert.True(result.Success, $"復号に失敗: code={result.ErrorCode} {result.ErrorMessage}");

            Assert.All(trimmed, p =>
            {
                var full = Path.Combine(outDir, p);
                Assert.True(File.Exists(full) || Directory.Exists(full), "一覧にあるのに書き出されていません: " + p);
            });
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }

    [Theory]
    [InlineData("v105")]
    [InlineData("v130")]
    public async Task 旧形式の一覧取得は誤ったパスワードをパスワード違いとして返す(string label)
    {
        var atcPath = Path.Combine(AppContext.BaseDirectory, "TestData", label, "legacy.atc");

        var result = await new DecryptionService().GetFileListAsync(
            atcPath, "WrongPassword!", new DecryptionOptions(), CancellationToken.None);

        Assert.False(result.Success);
        // 画面はこのコードを見てパスワードの入力し直し（回数の計上）に進む
        Assert.Equal(ErrorCodes.PASSWORD_TOKEN_NOT_FOUND, result.ErrorCode);
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
