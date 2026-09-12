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
// 更新確認（UpdateService）のテスト。
//
// ここで守りたいのは「署名を通っていないマニフェストを信用しないこと」と
// 「古い版へ引きずり降ろされないこと」の 2 点。
// どちらも破られると、全利用者に任意のバイナリを配れることになる。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using AttacheCase.Models;
using AttacheCase.Services;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Update")]
public class UpdateServiceTests
{
    // =================================================================
    // 署名の検証
    // =================================================================

    /// <summary>テスト用の Ed25519 鍵ペアを作る。</summary>
    private static (string PublicKeyBase64, Ed25519PrivateKeyParameters Private) NewKeyPair()
    {
        var random = new SecureRandom();
        var priv = new Ed25519PrivateKeyParameters(random);
        var pub = priv.GeneratePublicKey();
        return (Convert.ToBase64String(pub.GetEncoded()), priv);
    }

    private static byte[] Sign(byte[] data, Ed25519PrivateKeyParameters priv)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, priv);
        signer.BlockUpdate(data, 0, data.Length);
        return signer.GenerateSignature();
    }

    [Fact]
    public void VerifySignature_正しい署名を受け入れる()
    {
        var (pub, priv) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"version\":\"5.1.0\"}");

        Assert.True(UpdateService.VerifySignature(data, Sign(data, priv), pub));
    }

    [Fact]
    public void VerifySignature_本文が1バイトでも変わったら拒否する()
    {
        var (pub, priv) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"version\":\"5.1.0\"}");
        var signature = Sign(data, priv);

        var tampered = (byte[])data.Clone();
        tampered[^2] ^= 0x01;

        Assert.False(UpdateService.VerifySignature(tampered, signature, pub));
    }

    [Fact]
    public void VerifySignature_署名が変わったら拒否する()
    {
        var (pub, priv) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"version\":\"5.1.0\"}");
        var signature = Sign(data, priv);
        signature[0] ^= 0x01;

        Assert.False(UpdateService.VerifySignature(data, signature, pub));
    }

    [Fact]
    public void VerifySignature_別の鍵で署名されていたら拒否する()
    {
        var (pub, _) = NewKeyPair();
        var (_, otherPriv) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"version\":\"5.1.0\"}");

        Assert.False(UpdateService.VerifySignature(data, Sign(data, otherPriv), pub));
    }

    [Theory]
    [InlineData("")]
    [InlineData("これは Base64 ではない")]
    [InlineData("QUJD")]          // 3 バイト。Ed25519 の公開鍵は 32 バイト
    public void VerifySignature_鍵が壊れていたら拒否する(string badKey)
    {
        var data = Encoding.UTF8.GetBytes("test");
        var signature = new byte[64];

        Assert.False(UpdateService.VerifySignature(data, signature, badKey));
    }

    [Fact]
    public void VerifySignature_署名の長さが違えば拒否する()
    {
        var (pub, priv) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("test");
        var signature = Sign(data, priv);

        Assert.False(UpdateService.VerifySignature(data, signature[..32], pub));
    }

    // =================================================================
    // 信頼する鍵が 2 本あること
    //
    // 公開鍵はアプリに焼き込まれ、配ったあとは足せない。現用の秘密鍵が
    // 漏れても失われても、予備の鍵で署名し直せば既存の利用者がそのまま
    // 更新を受け取れる ―― という逃げ道を守るためのテスト。
    // =================================================================

    [Fact]
    public void 現用の鍵で署名されていれば受理する()
    {
        var (current, currentPriv) = NewKeyPair();
        var (backup, _) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"version\":\"5.1.0\"}");

        Assert.True(UpdateService.VerifySignature(
            data, Sign(data, currentPriv), new[] { current, backup }));
    }

    [Fact]
    public void 予備の鍵で署名されていても受理する()
    {
        var (current, _) = NewKeyPair();
        var (backup, backupPriv) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"version\":\"5.1.0\"}");

        Assert.True(UpdateService.VerifySignature(
            data, Sign(data, backupPriv), new[] { current, backup }));
    }

    [Fact]
    public void どちらの鍵でもなければ拒否する()
    {
        var (current, _) = NewKeyPair();
        var (backup, _) = NewKeyPair();
        var (_, otherPriv) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"version\":\"5.1.0\"}");

        Assert.False(UpdateService.VerifySignature(
            data, Sign(data, otherPriv), new[] { current, backup }));
    }

    [Fact]
    public void 予備の鍵が未設定でも現用の鍵で受理する()
    {
        // 予備を用意する前の状態。空の要素は飛ばす
        var (current, currentPriv) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"version\":\"5.1.0\"}");

        Assert.True(UpdateService.VerifySignature(
            data, Sign(data, currentPriv), new[] { current, "" }));
    }

    [Fact]
    public void 鍵が1本も無ければ拒否する()
    {
        // 呼び違えたときに素通りしないこと。
        // 「鍵が無いから検証しない」ではなく「検証できないので拒否」
        var data = Encoding.UTF8.GetBytes("test");
        var signature = new byte[64];

        Assert.False(UpdateService.VerifySignature(data, signature, Array.Empty<string>()));
        Assert.False(UpdateService.VerifySignature(data, signature, (string[]?)null));
        Assert.False(UpdateService.VerifySignature(data, signature, new[] { "", "  " }));
    }

    [Fact]
    public void HasAnyKey_空と未設定を鍵として数えない()
    {
        Assert.False(UpdateService.HasAnyKey(null));
        Assert.False(UpdateService.HasAnyKey(Array.Empty<string>()));
        Assert.False(UpdateService.HasAnyKey(new[] { "", "  " }));
        Assert.True(UpdateService.HasAnyKey(new[] { "", "QUJD" }));
    }

    [Fact]
    public void 既定の鍵には現用の鍵が入っている()
    {
        // 配信の鍵は OS ごとに別（片方の漏えいがもう片方に及ばない）
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains(UpdateService.UpdateWindowsPublicKeyBase64, UpdateService.DefaultPublicKeys);
            Assert.Contains(UpdateService.UpdateWindowsBackupPublicKeyBase64, UpdateService.DefaultPublicKeys);
        }
        else
        {
            Assert.Contains(UpdateService.UpdatePublicKeyBase64, UpdateService.DefaultPublicKeys);
        }
    }

    // =================================================================
    // バージョンの判定
    // =================================================================

    /// <summary>Evaluate が選ぶ builds のキー（macOS: アーキテクチャ / Windows: 導入形態）</summary>
    private static string CurrentBuildKey =>
        OperatingSystem.IsWindows()
            ? AttacheCase.Platform.Windows.WindowsInstallKind.BuildKeyFor(
                AttacheCase.Platform.Windows.WindowsInstallKind.Detect())
            : UpdateService.GetCurrentArchitecture();

    private static UpdateManifest Manifest(string version, string? minSystem = null)
    {
        var arch = CurrentBuildKey;
        return new UpdateManifest
        {
            Version = version,
            MinimumSystemVersion = minSystem,
            Builds = new Dictionary<string, UpdateBuild>
            {
                [arch] = new UpdateBuild
                {
                    Url = $"https://example.test/AtcMac-{arch}.dmg",
                    Size = 1234,
                    Sha256 = new string('a', 64),
                },
            },
        };
    }

    private static UpdateService ServiceAt(string currentVersion) =>
        new(currentVersion: Version.Parse(currentVersion), publicKeyBase64: "dummy");

    [Fact]
    public void Evaluate_新しい版があれば更新ありとする()
    {
        var result = ServiceAt("5.0.0").Evaluate(Manifest("5.1.0"));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Build);
    }

    [Fact]
    public void Evaluate_同じ版なら更新なしとする()
    {
        Assert.Equal(
            UpdateCheckStatus.UpToDate,
            ServiceAt("5.0.0").Evaluate(Manifest("5.0.0")).Status);
    }

    [Fact]
    public void Evaluate_古い版は受け付けない()
    {
        // ダウングレード攻撃対策。既知の脆弱性を持つ古い版を掴まされないため、
        // 「違っていたら更新」ではなく「新しいときだけ更新」でなければならない
        Assert.Equal(
            UpdateCheckStatus.UpToDate,
            ServiceAt("5.2.0").Evaluate(Manifest("5.1.0")).Status);
    }

    [Fact]
    public void Evaluate_バージョン表記が不正なら失敗させる()
    {
        var result = ServiceAt("5.0.0").Evaluate(Manifest("さいしんばん"));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public void Evaluate_動作環境を満たさない版は更新として出さない()
    {
        var manifest = Manifest("9.9.9", minSystem: "99.0");

        Assert.Equal(
            UpdateCheckStatus.UpToDate,
            ServiceAt("5.0.0").Evaluate(manifest).Status);
    }

    // =================================================================
    // 配布物の妥当性
    // =================================================================

    [Fact]
    public void Evaluate_自分のアーキテクチャ向けが無ければ失敗させる()
    {
        var manifest = Manifest("5.1.0");
        manifest.Builds.Clear();
        manifest.Builds["ppc64"] = new UpdateBuild
        {
            Url = "https://example.test/x.dmg", Size = 1, Sha256 = new string('a', 64),
        };

        Assert.Equal(UpdateCheckStatus.Failed, ServiceAt("5.0.0").Evaluate(manifest).Status);
    }

    [Theory]
    [InlineData("http://example.test/x.dmg")]
    [InlineData("file:///tmp/x.dmg")]
    [InlineData("これは URL ではない")]
    public void Evaluate_httpsでない配布物URLは拒否する(string url)
    {
        var manifest = Manifest("5.1.0");
        manifest.Builds[CurrentBuildKey].Url = url;

        Assert.Equal(UpdateCheckStatus.Failed, ServiceAt("5.0.0").Evaluate(manifest).Status);
    }

    [Fact]
    public void Evaluate_ハッシュが無ければ拒否する()
    {
        // 落としたものを検証できないまま差し替えに進ませない
        var manifest = Manifest("5.1.0");
        manifest.Builds[CurrentBuildKey].Sha256 = "";

        Assert.Equal(UpdateCheckStatus.Failed, ServiceAt("5.0.0").Evaluate(manifest).Status);
    }

    [Fact]
    public void Evaluate_サイズが無ければ拒否する()
    {
        var manifest = Manifest("5.1.0");
        manifest.Builds[CurrentBuildKey].Size = 0;

        Assert.Equal(UpdateCheckStatus.Failed, ServiceAt("5.0.0").Evaluate(manifest).Status);
    }

    // =================================================================
    // 入り口の守り
    // =================================================================

    [Fact]
    public async Task CheckAsync_公開鍵が未設定なら通信せずに失敗させる()
    {
        // 鍵を用意しないまま無署名のマニフェストを信用してしまうと、
        // 更新確認そのものが攻撃経路になる
        var service = new UpdateService(publicKeyBase64: "");
        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    // =================================================================
    // マニフェスト URL の決め方（環境変数による試験用の差し替え）
    // =================================================================

    [Fact]
    public void ResolveManifestUrl_環境変数が無ければOS既定を使う()
    {
        using var _ = new EnvScope(UpdateService.ManifestUrlEnvironmentVariable, null);

        Assert.Equal(UpdateService.DefaultManifestUrlForCurrentOs,
            new UpdateService(publicKeyBase64: "dummy").ManifestUrl);
        Assert.False(UpdateService.IsManifestUrlOverridden);
    }

    [Fact]
    public void ResolveManifestUrl_環境変数があればそちらを使う()
    {
        using var _ = new EnvScope(UpdateService.ManifestUrlEnvironmentVariable,
            "  https://example.test/test/appcast.json  ");

        Assert.Equal("https://example.test/test/appcast.json",
            new UpdateService(publicKeyBase64: "dummy").ManifestUrl);
        Assert.True(UpdateService.IsManifestUrlOverridden);
    }

    [Fact]
    public void ResolveManifestUrl_空白だけの環境変数は未設定として扱う()
    {
        using var _ = new EnvScope(UpdateService.ManifestUrlEnvironmentVariable, "   ");

        Assert.Equal(UpdateService.DefaultManifestUrlForCurrentOs,
            new UpdateService(publicKeyBase64: "dummy").ManifestUrl);
        Assert.False(UpdateService.IsManifestUrlOverridden);
    }

    [Fact]
    public void ResolveManifestUrl_明示したURLは環境変数より優先する()
    {
        // テストがローカルの試験用サーバーを指すとき、環境変数に邪魔されない
        using var _ = new EnvScope(UpdateService.ManifestUrlEnvironmentVariable,
            "https://example.test/env/appcast.json");

        var service = new UpdateService(
            manifestUrl: "https://example.test/explicit/appcast.json",
            publicKeyBase64: "dummy");

        Assert.Equal("https://example.test/explicit/appcast.json", service.ManifestUrl);
    }

    [Fact]
    public async Task CheckAsync_環境変数でhttpに差し替えても拒否する()
    {
        // 差し替えの口があっても、https 限定の守りは緩まない
        using var _ = new EnvScope(UpdateService.ManifestUrlEnvironmentVariable,
            "http://example.test/appcast.json");

        var result = await new UpdateService(publicKeyBase64: "dummy").CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    /// <summary>環境変数を一時的に差し替え、抜けるときに元へ戻す。</summary>
    private sealed class EnvScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }

    [Fact]
    public async Task CheckAsync_マニフェストURLがhttpsでなければ失敗させる()
    {
        var service = new UpdateService(
            manifestUrl: "http://example.test/appcast.json",
            publicKeyBase64: "dummy");
        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    // =================================================================
    // 動作環境の比較
    // =================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("解釈できない値")]
    public void IsSystemVersionSatisfied_指定が無いか十分に低ければ満たす(string? min)
    {
        Assert.True(UpdateService.IsSystemVersionSatisfied(min));
    }

    // "13" は macOS Ventura の版数表記。Windows の OS バージョン（10.x）より
    // 大きいため、この値が「満たす」側に来るのは macOS 上だけ。
    [MacOsFact]
    public void IsSystemVersionSatisfied_macOSの版数表記も満たす()
    {
        Assert.True(UpdateService.IsSystemVersionSatisfied("13"));
    }

    [Fact]
    public void IsSystemVersionSatisfied_高すぎる要求は満たさない()
    {
        Assert.False(UpdateService.IsSystemVersionSatisfied("99.0"));
    }

    // =================================================================
    // 現在の環境
    // =================================================================

    [Fact]
    public void GetCurrentArchitecture_マニフェストのキーと同じ表記を返す()
    {
        Assert.Contains(UpdateService.GetCurrentArchitecture(), new[] { "arm64", "x64" });
    }

    [Fact]
    public void GetCurrentVersion_OSごとの桁数で返す()
    {
        var v = UpdateService.GetCurrentVersion();

        Assert.True(v.Major > 0);
        if (OperatingSystem.IsWindows())
        {
            // Windows は 4 桁目（Revision）で版を進める運用。丸めると更新を検出できない
            Assert.True(v.Revision >= 0);
        }
        else
        {
            Assert.Equal(-1, v.Revision);
        }
    }

    // =================================================================
    // ツールとの相互運用
    //
    // appcast_generate.sh は openssl で署名し、アプリは BouncyCastle で検証する。
    // 実装が違うので、実際に openssl が作った署名を固定データとして持っておき、
    // 片方の更新で黙って噛み合わなくなることを防ぐ。
    // 下のデータは appcast_keygen.sh / appcast_generate.sh の実出力（使い捨ての鍵）。
    // =================================================================

    private const string FixturePublicKey =
        "C0WXpYiKnPUO93ANfTn90tLW0O7Jx2B2o6Ofkf6xQ+I=";

    private const string FixtureSignature =
        "925JHOllk3LguBjI6OeLHIW4SCR6tEvJGtA/Gsm//0gSbDemQXucSzM7VIbO3MgSA8m2fWj9w5mfSGAX0LF1CA==";

    private const string FixtureManifestBase64 =
        "ewogICJ2ZXJzaW9uIjogIjUuMC4wIiwKICAiYmV0YSI6IHRydWUsCiAgIm1pbmltdW1TeXN0ZW1WZXJzaW9uIjogIjEzLjAiLAogICJwdWJEYXRlIjogIjIwMjYtMDktMDNUMDY6MTE6MThaIiwKICAicmVsZWFzZU5vdGVzVXJsIjogImh0dHBzOi8vaGliYXJhLmpwL3NvZnR3YXJlL2F0dGFjaGVjYXNlL21hYy8iLAogICJidWlsZHMiOiB7CiAgICAiYXJtNjQiOiB7CiAgICAgICJ1cmwiOiAiaHR0cHM6Ly9oaWJhcmEuanAvc29mdHdhcmUvYXR0YWNoZWNhc2UvbWFjL0F0Y01hYzUwMGItYXJtNjQuZG1nIiwKICAgICAgInNpemUiOiA2MzgwNjczMiwKICAgICAgInNoYTI1NiI6ICI2MDA1YTVjODMwZTM0NDE4YjAzYTY1MzdmNmQwMzdhYTYwNWM2NDNjZjg2NzIzNGU3MGQ1ZTYyZTNmNzQyMTBlIgogICAgfSwKICAgICJ4NjQiOiB7CiAgICAgICJ1cmwiOiAiaHR0cHM6Ly9oaWJhcmEuanAvc29mdHdhcmUvYXR0YWNoZWNhc2UvbWFjL0F0Y01hYzUwMGIteDY0LmRtZyIsCiAgICAgICJzaXplIjogNjc3NDgyODAsCiAgICAgICJzaGEyNTYiOiAiNmI1MjQzZWNhZTg5Y2QzNWQ0YmY1N2VjMzg2ODljNTMwMDVhOTc1YjZmMGYwZWJhODY0MDljZTNhZjU4ZWY2OCIKICAgIH0KICB9Cn0K";

    [Fact]
    public void opensslが作った署名をBouncyCastleで検証できる()
    {
        var manifest = Convert.FromBase64String(FixtureManifestBase64);
        var signature = Convert.FromBase64String(FixtureSignature);

        Assert.True(UpdateService.VerifySignature(manifest, signature, FixturePublicKey));
    }

    [Fact]
    public void 固定データのマニフェストを解釈できる()
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(FixtureManifestBase64));
        var manifest = System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);
        Assert.Equal("5.0.0", manifest!.Version);
        Assert.Equal("13.0", manifest.MinimumSystemVersion);
        Assert.Equal(2, manifest.Builds.Count);
        Assert.Equal(63806732, manifest.Builds["arm64"].Size);
        Assert.StartsWith("https://", manifest.Builds["x64"].Url);
        Assert.Equal(64, manifest.Builds["arm64"].Sha256.Length);
    }

    [Fact]
    public void 固定データを1バイト変えると検証が落ちる()
    {
        var manifest = Convert.FromBase64String(FixtureManifestBase64);
        var signature = Convert.FromBase64String(FixtureSignature);

        // size の数字を 1 つ変える。URL やハッシュの差し替えが通らないことの確認
        var tampered = Encoding.UTF8.GetString(manifest).Replace("63806732", "63806733");

        Assert.False(UpdateService.VerifySignature(
            Encoding.UTF8.GetBytes(tampered), signature, FixturePublicKey));
    }

    // =================================================================
    // 名乗り（User-Agent）
    //
    // 配信側のアクセスログで版と環境の内訳を見るために付けている。
    // 個体を識別する値が紛れ込んでいないことを、ここで押さえておく。
    // =================================================================

    [Fact]
    public void UserAgent_版と環境が分かる形になっている()
    {
        var ua = UpdateService.BuildUserAgent(
            new Version(5, 1, 0), "arm64", new Version(26, 6, 2));

        var osName = OperatingSystem.IsWindows() ? "Windows" : "macOS";
        Assert.Equal($"AttacheCase/5.1.0 ({osName} 26.6.2; arm64)", ua);
    }

    [Fact]
    public void UserAgent_実環境でも組み立てられる()
    {
        var ua = UpdateService.UserAgent;

        Assert.StartsWith("AttacheCase/", ua);
        Assert.Contains(OperatingSystem.IsWindows() ? "Windows " : "macOS ", ua);
        Assert.Contains(UpdateService.GetCurrentArchitecture(), ua);
    }

    [Fact]
    public void UserAgent_個体を識別する値を含まない()
    {
        // 数えたいのは「どの版が使われているか」であって「誰が使っているか」ではない
        var ua = UpdateService.UserAgent;

        Assert.DoesNotContain(Environment.UserName, ua);
        Assert.DoesNotContain(Environment.MachineName, ua);
        Assert.DoesNotContain("-", ua.Replace("AttacheCase/", ""));  // UUID 風の値が無いこと
    }

    // =================================================================
    // タイムアウトと中止の区別
    //
    // HttpClient.Timeout も TaskCanceledException（＝OperationCanceledException）
    // で来る。中止と同じに扱うと、起動時の確認が確認日時を記録しないまま抜けて
    // 通信できない間じゅうサーバーを叩き続け、手動の確認では
    // "A task was canceled." がそのまま画面に出る。
    // =================================================================

    /// <summary>要求のたびに指定の例外を投げる差し替え。</summary>
    private sealed class ThrowingHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly Func<Exception> _factory;

        public ThrowingHandler(Func<Exception> factory) => _factory = factory;

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken) =>
            throw _factory();
    }

    private static UpdateService ServiceWith(System.Net.Http.HttpMessageHandler handler) =>
        new(
            http: new System.Net.Http.HttpClient(handler),
            manifestUrl: "https://example.test/appcast.json",
            publicKeyBase64: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

    [Fact]
    public async Task CheckAsync_タイムアウトは失敗として返す()
    {
        // 呼び出し側は何も中止していない
        var service = ServiceWith(new ThrowingHandler(
            () => new TaskCanceledException("A task was canceled.")));

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.DoesNotContain("A task was canceled", result.Error ?? "");
    }

    [Fact]
    public async Task CheckAsync_呼び出し側の中止はそのまま投げ返す()
    {
        // 中止は失敗ではない。確認日時を記録させないため、握らずに投げる
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var service = ServiceWith(new ThrowingHandler(
            () => new OperationCanceledException(cts.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckAsync(cts.Token));
    }

    /// <summary>送信された要求を捕まえるための差し替え。実際の通信はしない。</summary>
    private sealed class CapturingHandler : System.Net.Http.HttpMessageHandler
    {
        public System.Net.Http.HttpRequestMessage? Captured { get; private set; }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken)
        {
            Captured ??= request;
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(
                System.Net.HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task 実際の要求にUserAgentが載る()
    {
        var handler = new CapturingHandler();
        var service = new UpdateService(
            http: new System.Net.Http.HttpClient(handler),
            manifestUrl: "https://example.test/appcast.json",
            publicKeyBase64: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

        await service.CheckAsync();

        Assert.NotNull(handler.Captured);
        Assert.Equal(
            UpdateService.UserAgent,
            handler.Captured!.Headers.UserAgent.ToString());
    }
}
