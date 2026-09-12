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
// 更新の下ごしらえ（UpdateFlow）と、表示まわりの小物のテスト。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Update")]
public class UpdateFlowTests
{
    /// <summary>codesign が TeamIdentifier を返さない状態を作る。</summary>
    private sealed class UnsignedRunner : IProcessRunner
    {
        public List<string> Calls { get; } = new();

        public Task<ProcessResult> RunAsync(
            string fileName, IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls.Add(Path.GetFileName(fileName));
            return Task.FromResult(new ProcessResult(0, "Identifier=jp.hibara.attachecase", ""));
        }
    }

    [Fact]
    public async Task 動作中のアプリの署名を読めなければ何もしない()
    {
        // 「あるべき署名者」が分からないまま進むと、比較の意味が無くなる。
        // ダウンロードも始めない
        var verifier = new UpdatePackageVerifier(new UnsignedRunner());
        var flow = new UpdateFlow(verifier: verifier);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => flow.PrepareAsync(DummyBuild(), "/Applications/AttacheCase.app"));

        Assert.Contains("署名を確認できない", ex.Message);
    }

    /// <summary>TeamIdentifier を返す実行系。ここを通さないと作業場所が作られない。</summary>
    private sealed class SignedRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            string fileName, IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(0, "TeamIdentifier=SPPG3MXDYF", ""));
    }

    /// <summary>必ず失敗する通知先。ダウンロードの段で落とすために使う。</summary>
    private sealed class FailingHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(
                System.Net.HttpStatusCode.NotFound));
    }

    private static UpdateBuild DummyBuild() => new()
    {
        Url = "https://example.test/x.dmg", Size = 100, Sha256 = new string('a', 64),
    };

    [Fact]
    public async Task ダウンロードで失敗しても一時ディレクトリを残さない()
    {
        // 署名は読める状態にして作業場所を作らせ、そのあとで失敗させる。
        // ここを通さないと catch の清掃処理が動かず、テストの意味が無くなる。
        //
        // 作業場所の親はこのテスト専用にする。共有の一時ディレクトリを数えると、
        // 並行して走る他のテストが作ったものを拾ってしまう
        var workRoot = Directory.CreateTempSubdirectory("atc-flow").FullName;
        try
        {
            var flow = new UpdateFlow(
                downloader: new UpdateDownloader(new System.Net.Http.HttpClient(new FailingHandler())),
                verifier: new UpdatePackageVerifier(new SignedRunner()),
                workRoot: workRoot);

            await Assert.ThrowsAnyAsync<Exception>(
                () => flow.PrepareAsync(DummyBuild(), "/Applications/AttacheCase.app"));

            Assert.Empty(Directory.GetFileSystemEntries(workRoot));
        }
        finally
        {
            Directory.Delete(workRoot, true);
        }
    }

    [Fact]
    public async Task 署名を読めない段では作業場所すら作らない()
    {
        var workRoot = Directory.CreateTempSubdirectory("atc-flow").FullName;
        try
        {
            var flow = new UpdateFlow(
                verifier: new UpdatePackageVerifier(new UnsignedRunner()),
                workRoot: workRoot);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => flow.PrepareAsync(DummyBuild(), "/Applications/AttacheCase.app"));

            Assert.Empty(Directory.GetFileSystemEntries(workRoot));
        }
        finally
        {
            Directory.Delete(workRoot, true);
        }
    }

    // =================================================================
    // 検証の失敗を利用者に伝える文
    // =================================================================

    [Theory]
    [InlineData(VerificationFailure.SignatureInvalid)]
    [InlineData(VerificationFailure.TeamIdMismatch)]
    [InlineData(VerificationFailure.TeamIdUnavailable)]
    [InlineData(VerificationFailure.NotNotarized)]
    [InlineData(VerificationFailure.GatekeeperRejected)]
    [InlineData(VerificationFailure.VerificationFailed)]
    public void どの失敗にも説明がある(VerificationFailure failure)
    {
        var text = UpdateFlow.DescribeFailure(VerificationResult.Fail(failure));

        Assert.False(string.IsNullOrWhiteSpace(text));
        // 未訳のキー名がそのまま出ていないこと
        Assert.DoesNotContain("Update_Verify_", text);
    }

    [Fact]
    public void 詳細があれば説明に添える()
    {
        var text = UpdateFlow.DescribeFailure(
            VerificationResult.Fail(VerificationFailure.TeamIdMismatch, "期待: A / 実際: B"));

        Assert.Contains("期待: A / 実際: B", text);
    }

    // =================================================================
    // 表示用の整形
    // =================================================================

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(63806732, "60.9 MB")]
    [InlineData(2147483648, "2.0 GB")]
    public void バイト数を見やすい単位にする(long bytes, string expected)
    {
        Assert.Equal(expected, AttacheCase.App.FormatBytes(bytes));
    }
}
