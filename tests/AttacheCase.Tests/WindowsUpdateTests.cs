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
// Windows の更新経路のテスト（Windows 版 verify-update.ps1 / e2e-update.ps1 の
// 主要な観点を xunit に移したもの）。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;
using AttacheCase.Platform.Windows;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class WindowsUpdateTests
{
    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Windows のみ";
        }
    }

    // =================================================================
    // 版数の比較（Windows は 4 桁目で版を進める）
    // =================================================================

    [WindowsFact]
    public void Windowsでは4桁のフルバージョンを使う()
    {
        // 3 桁に丸めると 5.0.0.3 と 5.0.0.4 が同じに見えて更新を検出できない
        var current = UpdateService.GetCurrentVersion();
        Assert.True(current.Revision >= 0, "Revision が -1（3 桁に丸められている）");
        Assert.Equal(AppVersion.CurrentFull, current);
    }

    // =================================================================
    // マニフェスト
    // =================================================================

    [Fact]
    public void マニフェストのbetaとinstallerキーを読める()
    {
        var json = """
            {
              "version": "5.0.0.3",
              "beta": true,
              "builds": {
                "installer": { "url": "https://example.test/a.exe", "size": 10, "sha256": "ab" },
                "zip": { "url": "https://example.test/a.zip", "size": 20, "sha256": "cd" }
              }
            }
            """;

        var manifest = System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(json)!;

        Assert.True(manifest.Beta);
        Assert.Equal(10, manifest.Builds["installer"].Size);
        Assert.Equal(20, manifest.Builds["zip"].Size);
    }

    [Fact]
    public void リリースノートのnull値はフォールバックする()
    {
        var json = """
            {
              "version": "1.0",
              "releaseNotes": { "ja": null, "en": [ { "kind": "new", "items": [ "hello" ] } ] }
            }
            """;

        var manifest = System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(json)!;

        var notes = manifest.NotesFor("ja"); // ja は null → en へ
        Assert.Single(notes);
        Assert.Equal("hello", notes[0].Items[0]);
    }

    // =================================================================
    // インストール形態の判定
    // =================================================================

    [WindowsFact]
    public void パスの同一判定は区切りと大文字小文字を吸収する()
    {
        Assert.True(WindowsInstallKind.IsSameDirectory(@"C:\Apps\AttacheCase5\", @"c:\apps\attachecase5"));
        Assert.False(WindowsInstallKind.IsSameDirectory(@"C:\Apps\AttacheCase5", @"C:\Apps\Other"));
        Assert.False(WindowsInstallKind.IsSameDirectory("", @"C:\Apps"));
    }

    [WindowsFact]
    public void ビルドキーはinstallerとzip()
    {
        Assert.Equal(UpdateService.BuildKeyInstaller, WindowsInstallKind.BuildKeyFor(InstallKind.Installer));
        Assert.Equal(UpdateService.BuildKeyZip, WindowsInstallKind.BuildKeyFor(InstallKind.Portable));
    }

    // =================================================================
    // 落としたインストーラの名前（ShellExecute で起動するので .exe が必須）
    // =================================================================

    /// <summary>決められた本文を返す実行系。実際の通信はしない。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        public StubHandler(byte[] body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_body),
            });
    }

    /// <summary>署名の検証を常に通す（名前の検査だけしたい）。</summary>
    private sealed class AlwaysOkVerifier : WindowsPackageVerifier
    {
        public override string GetRunningAppSigner() => "CN=Test Signer";
        public override WindowsVerificationResult Verify(string filePath, string expectedSigner) =>
            WindowsVerificationResult.Success();
    }

    [WindowsFact]
    public async Task 落としたインストーラはexeの名前で保存される()
    {
        // ダウンローダーの既定は Mac の .dmg。そのままだと update.dmg として保存され、
        // LaunchInstaller の ShellExecute が「どのアプリで開きますか」を出す（2026-09-12 実測）
        var body = new byte[2048];
        Random.Shared.NextBytes(body);
        var workRoot = Directory.CreateTempSubdirectory("atc-update-test4").FullName;
        try
        {
            var flow = new WindowsUpdateFlow(
                new UpdateDownloader(new HttpClient(new StubHandler(body))),
                new AlwaysOkVerifier(),
                workRoot);
            var build = new UpdateBuild
            {
                Url = "https://example.test/5500b/atcs5500b.exe",
                Size = body.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
            };

            using var preparation = await flow.PrepareAsync(build);

            Assert.Equal("atcs5500b.exe", Path.GetFileName(preparation.VerifiedInstallerPath));
            Assert.True(File.Exists(preparation.VerifiedInstallerPath));
        }
        finally
        {
            try { Directory.Delete(workRoot, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }

    // =================================================================
    // 掴んだハンドルからのマニフェスト照合（TOCTOU 対策）
    // =================================================================

    [WindowsFact]
    public void 掴んだハンドルの内容がマニフェストと一致すれば通る()
    {
        var dir = Directory.CreateTempSubdirectory("atc-update-test").FullName;
        try
        {
            var path = Path.Combine(dir, "installer.exe");
            var bytes = new byte[1024];
            Random.Shared.NextBytes(bytes);
            File.WriteAllBytes(path, bytes);

            var build = new UpdateBuild
            {
                Url = "https://example.test/installer.exe",
                Size = bytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            };

            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            WindowsUpdateFlow.VerifyAgainstManifest(hold, build); // 例外が出なければ成功

            // 掴んでいるあいだは書き込みで開けない（差し替えを防げている）
            Assert.ThrowsAny<IOException>(() =>
            {
                using var _ = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            });
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }

    [WindowsFact]
    public void ハッシュが違えばUpdateContentMismatchException()
    {
        var dir = Directory.CreateTempSubdirectory("atc-update-test2").FullName;
        try
        {
            var path = Path.Combine(dir, "installer.exe");
            File.WriteAllBytes(path, new byte[100]);

            var build = new UpdateBuild
            {
                Url = "https://example.test/installer.exe",
                Size = 100,
                Sha256 = "deadbeef", // 正しくないハッシュ
            };

            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Throws<UpdateContentMismatchException>(
                () => WindowsUpdateFlow.VerifyAgainstManifest(hold, build));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }

    [WindowsFact]
    public void サイズが違えばUpdateContentMismatchException()
    {
        var dir = Directory.CreateTempSubdirectory("atc-update-test3").FullName;
        try
        {
            var path = Path.Combine(dir, "installer.exe");
            File.WriteAllBytes(path, new byte[100]);

            var build = new UpdateBuild { Url = "https://x.test/a.exe", Size = 999, Sha256 = "ab" };

            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Throws<UpdateContentMismatchException>(
                () => WindowsUpdateFlow.VerifyAgainstManifest(hold, build));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }

    // =================================================================
    // 鍵の選択
    // =================================================================

    [WindowsFact]
    public void Windowsの既定鍵はWindows配信用()
    {
        // Windows と macOS は配信の鍵を分けている（片方の漏えいがもう片方に及ばない）
        Assert.Contains(UpdateService.UpdateWindowsPublicKeyBase64, UpdateServiceTestHook.DefaultKeys());
        Assert.DoesNotContain(UpdateService.UpdatePublicKeyBase64, UpdateServiceTestHook.DefaultKeys());
    }

    private static class UpdateServiceTestHook
    {
        public static string[] DefaultKeys() => UpdateService.DefaultPublicKeys;
    }
}
