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
// DMG から .app を取り出す部分のテスト。
//
// 実物の DMG を使う確認も入れてある（build_and_release.sh の出力がある場合のみ）。
// hdiutil のマウントと ditto の複製は、模擬だけでは噛み合わなくなっても気づけない。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Update")]
public class DiskImageExtractorTests
{
    // =================================================================
    // マウント先から .app を選ぶ
    // =================================================================

    private static string NewMountPoint() =>
        Directory.CreateTempSubdirectory("atc-mount").FullName;

    [Fact]
    public void appがひとつなら選べる()
    {
        var mount = NewMountPoint();
        try
        {
            Directory.CreateDirectory(Path.Combine(mount, "AttacheCase.app"));
            Directory.CreateDirectory(Path.Combine(mount, ".background"));
            File.WriteAllText(Path.Combine(mount, ".VolumeIcon.icns"), "");

            Assert.Equal(
                Path.Combine(mount, "AttacheCase.app"),
                DiskImageExtractor.FindSingleApp(mount));
        }
        finally
        {
            Directory.Delete(mount, true);
        }
    }

    [Fact]
    public void appが無ければ受け付けない()
    {
        var mount = NewMountPoint();
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => DiskImageExtractor.FindSingleApp(mount));
            Assert.Contains("見つかりません", ex.Message);
        }
        finally
        {
            Directory.Delete(mount, true);
        }
    }

    [Fact]
    public void appが複数あれば受け付けない()
    {
        // こちらが作った DMG なら必ず 1 つ。定まらないものは使わない
        var mount = NewMountPoint();
        try
        {
            Directory.CreateDirectory(Path.Combine(mount, "AttacheCase.app"));
            Directory.CreateDirectory(Path.Combine(mount, "Evil.app"));

            var ex = Assert.Throws<InvalidOperationException>(
                () => DiskImageExtractor.FindSingleApp(mount));
            Assert.Contains("2 個", ex.Message);
        }
        finally
        {
            Directory.Delete(mount, true);
        }
    }

    [Fact]
    public void 入れ子のappは数えない()
    {
        // 直下だけを見る。深いところの .app に引きずられない
        var mount = NewMountPoint();
        try
        {
            Directory.CreateDirectory(Path.Combine(mount, "AttacheCase.app"));
            Directory.CreateDirectory(
                Path.Combine(mount, "AttacheCase.app", "Contents", "Helper.app"));

            Assert.Equal(
                Path.Combine(mount, "AttacheCase.app"),
                DiskImageExtractor.FindSingleApp(mount));
        }
        finally
        {
            Directory.Delete(mount, true);
        }
    }

    // =================================================================
    // 手順の確認
    // =================================================================

    private sealed class RecordingRunner : IProcessRunner
    {
        public List<string> Calls { get; } = new();
        public Func<string, IReadOnlyList<string>, ProcessResult>? Behavior { get; set; }

        public Task<ProcessResult> RunAsync(
            string fileName, IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls.Add($"{Path.GetFileName(fileName)} {arguments[0]}");
            return Task.FromResult(
                Behavior?.Invoke(fileName, arguments) ?? new ProcessResult(0, "", ""));
        }
    }

    [Fact]
    public async Task マウントに失敗したらアンマウントを試みて終わる()
    {
        var runner = new RecordingRunner
        {
            Behavior = (_, args) => args[0] == "attach"
                ? new ProcessResult(1, "", "image not recognized")
                : new ProcessResult(0, "", ""),
        };
        var directory = Directory.CreateTempSubdirectory("atc-extract").FullName;
        var dmg = Path.Combine(directory, "x.dmg");
        await File.WriteAllTextAsync(dmg, "dummy");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new DiskImageExtractor(runner).ExtractAppAsync(dmg, directory));

            // finally で必ず後始末する
            Assert.Contains("hdiutil detach", runner.Calls);

            // マウントできていないので取り出しには進まない。
            // 記録は "ditto /Users/..." の形になるため、前方一致で見る
            Assert.DoesNotContain(runner.Calls, c => c.StartsWith("ditto", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task DMGが無ければ例外にする()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => new DiskImageExtractor().ExtractAppAsync(
                "/no/such/file.dmg", Path.GetTempPath()));
    }

    // =================================================================
    // 実物での確認
    // =================================================================

    /// <summary>
    /// 署名済みの DMG（build_and_release.sh の出力）。
    /// publish されていない環境では確認せずに抜ける。
    /// </summary>
    private static string? BuiltDmgPath()
    {
        var directory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../src/AttacheCase/bin/Publish/osx-arm64"));
        if (!Directory.Exists(directory)) return null;

        return Directory.GetFiles(directory, "*.dmg").FirstOrDefault();
    }

    [Fact]
    public async Task 実物のDMGからappを取り出せる()
    {
        var dmg = BuiltDmgPath();
        if (dmg is null) return;

        var destination = Directory.CreateTempSubdirectory("atc-extract-real").FullName;
        try
        {
            var app = await new DiskImageExtractor().ExtractAppAsync(dmg, destination);

            Assert.True(Directory.Exists(app));
            Assert.EndsWith(".app", app);

            // ditto で複製したものが、そのまま署名検証を通ること。
            // cp だと拡張属性が落ちてここで失敗する
            var teamId = await new UpdatePackageVerifier().GetTeamIdentifierAsync(app);
            Assert.Equal("SPPG3MXDYF", teamId);
        }
        finally
        {
            Directory.Delete(destination, true);
        }
    }

    [Fact]
    public async Task 取り出したあとマウントが残らない()
    {
        var dmg = BuiltDmgPath();
        if (dmg is null) return;

        var destination = Directory.CreateTempSubdirectory("atc-extract-mount").FullName;
        try
        {
            await new DiskImageExtractor().ExtractAppAsync(dmg, destination);

            var runner = new ProcessRunner();
            var mounts = await runner.RunAsync(
                "/sbin/mount", Array.Empty<string>(), TimeSpan.FromSeconds(30));

            Assert.DoesNotContain("attachecase-update-", mounts.StandardOutput);
        }
        finally
        {
            Directory.Delete(destination, true);
        }
    }
}
