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
// 差し替え候補の .app を検証する部分のテスト。
//
// このアプリで最も重要な防御線。更新は HttpClient で取得するため隔離属性が付かず、
// Gatekeeper は新しい .app を検査しない。ここを抜けられると、全利用者に任意の
// バイナリを配れることになる。
//
// 「通してはいけないものを通さない」を重点的に確かめる。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Update")]
public class UpdatePackageVerifierTests
{
    private const string TeamId = "SPPG3MXDYF";

    /// <summary>コマンドごとの結果を差し替えられる実行系。</summary>
    private sealed class FakeRunner : IProcessRunner
    {
        private readonly Dictionary<string, ProcessResult> _results = new();
        public List<string> Calls { get; } = new();

        /// <summary>キーは "codesign --verify" のように、コマンド名＋最初の引数。</summary>
        public FakeRunner Set(string key, int exitCode, string output = "")
        {
            _results[key] = new ProcessResult(exitCode, output, string.Empty);
            return this;
        }

        public Task<ProcessResult> RunAsync(
            string fileName, IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var key = $"{Path.GetFileName(fileName)} {arguments[0]}";
            Calls.Add(key);
            return Task.FromResult(
                _results.TryGetValue(key, out var r) ? r : new ProcessResult(0, "", ""));
        }
    }

    /// <summary>4 段すべてを通る状態の実行系。</summary>
    private static FakeRunner AllPassing() =>
        new FakeRunner()
            .Set("codesign --verify", 0)
            .Set("codesign -dvv", 0, $"Identifier=jp.hibara.attachecase\nTeamIdentifier={TeamId}\n")
            .Set("xcrun stapler", 0, "The validate action worked!")
            .Set("spctl -a", 0, "accepted\nsource=Notarized Developer ID");

    private static Task<VerificationResult> Verify(FakeRunner runner) =>
        new UpdatePackageVerifier(runner).VerifyAsync("/tmp/AttacheCase.app", TeamId);

    // =================================================================
    // 通ってよい場合
    // =================================================================

    [Fact]
    public async Task 四段すべて通れば受け入れる()
    {
        var result = await Verify(AllPassing());

        Assert.True(result.Ok);
        Assert.Equal(VerificationFailure.None, result.Failure);
    }

    [Fact]
    public async Task 四段すべてを実際に実行している()
    {
        var runner = AllPassing();
        await Verify(runner);

        Assert.Equal(
            new[] { "codesign --verify", "codesign -dvv", "xcrun stapler", "spctl -a" },
            runner.Calls);
    }

    // =================================================================
    // 通してはいけない場合
    // =================================================================

    [Fact]
    public async Task 署名が壊れていたら拒否する()
    {
        var runner = AllPassing().Set("codesign --verify", 1, "code object is not signed at all");

        var result = await Verify(runner);

        Assert.False(result.Ok);
        Assert.Equal(VerificationFailure.SignatureInvalid, result.Failure);
    }

    [Fact]
    public async Task 署名が壊れていたらそこで打ち切る()
    {
        // 通らないと分かった時点で止める。後段を無駄に走らせない
        var runner = AllPassing().Set("codesign --verify", 1);
        await Verify(runner);

        Assert.Equal(new[] { "codesign --verify" }, runner.Calls);
    }

    [Fact]
    public async Task 別人が署名していたら拒否する()
    {
        // 実質いちばん強い防御。配信サーバや Ed25519 鍵が破られても、
        // 攻撃者は Apple の Developer ID で署名したバイナリを作れない
        var runner = AllPassing()
            .Set("codesign -dvv", 0, "TeamIdentifier=ATTACKER99\n");

        var result = await Verify(runner);

        Assert.False(result.Ok);
        Assert.Equal(VerificationFailure.TeamIdMismatch, result.Failure);
        Assert.Contains("ATTACKER99", result.Detail);
    }

    [Fact]
    public async Task 別人が署名していたら公証の確認まで進まない()
    {
        var runner = AllPassing().Set("codesign -dvv", 0, "TeamIdentifier=ATTACKER99\n");
        await Verify(runner);

        Assert.DoesNotContain("xcrun stapler", runner.Calls);
        Assert.DoesNotContain("spctl -a", runner.Calls);
    }

    [Fact]
    public async Task TeamIdを読めなければ拒否する()
    {
        var runner = AllPassing().Set("codesign -dvv", 0, "Identifier=jp.hibara.attachecase\n");

        var result = await Verify(runner);

        Assert.False(result.Ok);
        Assert.Equal(VerificationFailure.TeamIdUnavailable, result.Failure);
    }

    [Fact]
    public async Task 公証されていなければ拒否する()
    {
        // spctl は署名済み・公証なしの .app も accepted にする（source が
        // "Notarized Developer ID" ではなく "Developer ID" になるだけ）。
        // 終了コードで区別できるのは stapler のほう
        var runner = AllPassing()
            .Set("xcrun stapler", 65, "does not have a ticket stapled to it");

        var result = await Verify(runner);

        Assert.False(result.Ok);
        Assert.Equal(VerificationFailure.NotNotarized, result.Failure);
    }

    [Fact]
    public async Task Gatekeeperに拒否されたら拒否する()
    {
        var runner = AllPassing().Set("spctl -a", 3, "rejected");

        var result = await Verify(runner);

        Assert.False(result.Ok);
        Assert.Equal(VerificationFailure.GatekeeperRejected, result.Failure);
    }

    [Fact]
    public async Task 比較相手のTeamIdが無ければ検証せずに拒否する()
    {
        var runner = AllPassing();
        var result = await new UpdatePackageVerifier(runner)
            .VerifyAsync("/tmp/AttacheCase.app", "");

        Assert.False(result.Ok);
        Assert.Equal(VerificationFailure.TeamIdUnavailable, result.Failure);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task 検証そのものが失敗したら拒否する()
    {
        // 「確かめられなかった」ものを通してはいけない
        var runner = new ThrowingRunner();
        var result = await new UpdatePackageVerifier(runner)
            .VerifyAsync("/tmp/AttacheCase.app", TeamId);

        Assert.False(result.Ok);
        Assert.Equal(VerificationFailure.VerificationFailed, result.Failure);
    }

    private sealed class ThrowingRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            string fileName, IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("codesign が見つかりません");
    }

    // =================================================================
    // TeamIdentifier の読み取り
    // =================================================================

    [Fact]
    public void 出力からTeamIdentifierを取り出せる()
    {
        var output = """
            Executable=/Applications/AttacheCase.app/Contents/MacOS/AttacheCase
            Identifier=jp.hibara.attachecase
            Format=app bundle with Mach-O thin (arm64)
            Authority=Developer ID Application: MITSUHIRO HIBARA (SPPG3MXDYF)
            TeamIdentifier=SPPG3MXDYF
            """;

        Assert.Equal("SPPG3MXDYF", UpdatePackageVerifier.ParseTeamIdentifier(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Identifier=jp.hibara.attachecase")]
    [InlineData("TeamIdentifier=not set")]
    [InlineData("TeamIdentifier=")]
    public void 読み取れない出力からはnullを返す(string output)
    {
        Assert.Null(UpdatePackageVerifier.ParseTeamIdentifier(output));
    }

    [Fact]
    public void 別の行のTeamIdentifierに引きずられない()
    {
        // 行頭に限定していないと、細工した文字列を含むバンドル名などで騙されうる
        var output = "Executable=/tmp/x TeamIdentifier=FAKE00000\nTeamIdentifier=SPPG3MXDYF";

        Assert.Equal("SPPG3MXDYF", UpdatePackageVerifier.ParseTeamIdentifier(output));
    }

    [Fact]
    public void 偽のTeamIdentifier行が先にあると矛盾としてnullを返す()
    {
        // Identifier= は署名者が決める文字列。改行を仕込めば偽の TeamIdentifier=
        // 行を本物より先に置ける。最初の一致を採ると署名者比較をすり抜けるので、
        // 値の違う行が複数あるときは細工とみなして null
        var output = "Identifier=com.evil\nTeamIdentifier=SPPG3MXDYF\n" +
                     "Format=app bundle\nTeamIdentifier=EVIL000000";

        Assert.Null(UpdatePackageVerifier.ParseTeamIdentifier(output));
    }

    [Fact]
    public void 同じ値のTeamIdentifier行が複数あっても読める()
    {
        var output = "TeamIdentifier=SPPG3MXDYF\nTeamIdentifier=SPPG3MXDYF";

        Assert.Equal("SPPG3MXDYF", UpdatePackageVerifier.ParseTeamIdentifier(output));
    }

    [Fact]
    public void TeamIDの形でない値は受け付けない()
    {
        // Apple の Team ID は英数字 10 文字。それ以外（改行注入で作られた
        // 任意文字列など）は読み取れなかった扱いにする
        Assert.Null(UpdatePackageVerifier.ParseTeamIdentifier("TeamIdentifier=short"));
        Assert.Null(UpdatePackageVerifier.ParseTeamIdentifier("TeamIdentifier=toolongvalue1"));
        Assert.Null(UpdatePackageVerifier.ParseTeamIdentifier("TeamIdentifier=SPPG3MXDY!"));
    }

    [Fact]
    public void 形の崩れた行と正しい形の行が混在したらnullを返す()
    {
        // 本物が "not set"（アドホック署名）で、注入された偽の行だけが
        // Team ID の形、という組み合わせ。形の合う行だけ拾う実装だと
        // 偽の行が唯一の一致になってすり抜ける
        Assert.Null(UpdatePackageVerifier.ParseTeamIdentifier(
            "TeamIdentifier=SPPG3MXDYF\nTeamIdentifier=not set"));
        Assert.Null(UpdatePackageVerifier.ParseTeamIdentifier(
            "TeamIdentifier=not set\nTeamIdentifier=SPPG3MXDYF"));
    }

    // =================================================================
    // 実物での確認
    // =================================================================

    /// <summary>
    /// 署名済みの .app（build_and_release.sh の出力）。
    ///
    /// これは実物のコマンドを叩く確認なので、publish されていない環境
    /// （素の clone、CI の単体テストだけの実行）では成立しない。
    /// 無ければ確認せずに抜ける。dotnet build だけでは作られない点に注意。
    /// </summary>
    private static string? BuiltAppPath()
    {
        var candidate = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/AttacheCase/bin/Publish/osx-arm64/AttacheCase.app"));
        return Directory.Exists(candidate) ? candidate : null;
    }

    [Fact]
    public async Task 実物のappからTeamIdentifierを読める()
    {
        var app = BuiltAppPath();
        if (app is null) return;   // publish されていない環境では確認しない

        var teamId = await new UpdatePackageVerifier().GetTeamIdentifierAsync(app);

        Assert.Equal(TeamId, teamId);
    }

    [Fact]
    public async Task 実物のappを別人のTeamIdでは通さない()
    {
        var app = BuiltAppPath();
        if (app is null) return;

        var result = await new UpdatePackageVerifier().VerifyAsync(app, "ATTACKER99");

        Assert.False(result.Ok);
        Assert.Equal(VerificationFailure.TeamIdMismatch, result.Failure);
    }
}
