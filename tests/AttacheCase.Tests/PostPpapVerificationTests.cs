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
// Post-PPAP の認証待ち（AwaitVerificationAsync）と公開鍵検索の
// サーバ応答検証を見張る。
//   - 認証に至らなかった鍵ペアを Keychain に残さない（孤児鍵の堆積防止）
//   - 鍵素材と食い違った fingerprint を受け入れない（開けないファイルの防止）
//----------------------------------------------------------------------

using System.Net;
using System.Text;
using AttacheCase.Crypto;
using AttacheCase.Models;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class PostPpapVerificationTests : IDisposable
{
    private readonly string _dir;

    public PostPpapVerificationTests()
    {
        _dir = Directory.CreateTempSubdirectory("atc-ppap-verify").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* 後始末の失敗は無視 */ }
    }

    /// <summary>URL の部分一致で応答を返す偽サーバ。</summary>
    private sealed class FakeHandler(Func<string, string?> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = respond(request.RequestUri!.ToString());
            var res = body == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            return Task.FromResult(res);
        }
    }

    private sealed class MemoryKeychain : IKeychainService
    {
        public Dictionary<string, byte[]> Stored { get; } = new();

        public bool TryGet(string service, string account, out byte[]? data)
            => Stored.TryGetValue(account, out data);

        public void Set(string service, string account, byte[] data)
            => Stored[account] = data;

        public bool Delete(string service, string account) => Stored.Remove(account);
    }

    private static PostPpapKeyInfo PendingAccount(string fingerprint) => new()
    {
        Email = "pending@example.com",
        EmailHash = "abc123hash",
        Fingerprint = fingerprint,
        MlKemPublicKey = Convert.ToBase64String(new byte[] { 9, 9, 9 }),
        AlgorithmId = 0x12, // RSA-4096 + ML-KEM-768
    };

    private PostPpapManager Manager(MemoryKeychain keychain, Func<string, string?> respond)
        => new(keychain, new HttpClient(new FakeHandler(respond)), _dir, "test.vault");

    private static MemoryKeychain KeychainWithPendingKeys(string fingerprint)
    {
        var keychain = new MemoryKeychain();
        keychain.Stored["rsa-priv-" + fingerprint] = new byte[] { 1 };
        keychain.Stored["mlkem-priv-" + fingerprint] = new byte[] { 2 };
        return keychain;
    }

    [Fact]
    public async Task 認証が期限切れなら保存済みの秘密鍵を消す()
    {
        const string fp = "f0f0f0f0";
        var keychain = KeychainWithPendingKeys(fp);
        var manager = Manager(keychain, url =>
            url.Contains("/api/register/status/")
                ? """{"ok":true,"data":{"status":"expired"}}"""
                : null);

        var ok = await manager.AwaitVerificationAsync("token", PendingAccount(fp));

        Assert.False(ok);
        Assert.Empty(keychain.Stored);
        Assert.Empty(manager.LoadAccounts());
    }

    [Fact]
    public async Task 認証待ちをキャンセルしたら保存済みの秘密鍵を消す()
    {
        const string fp = "f1f1f1f1";
        var keychain = KeychainWithPendingKeys(fp);
        var manager = Manager(keychain, url =>
            url.Contains("/api/register/status/")
                ? """{"ok":true,"data":{"status":"pending"}}"""
                : null);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.AwaitVerificationAsync("token", PendingAccount(fp), cancellationToken: cts.Token));

        Assert.Empty(keychain.Stored);
        Assert.Empty(manager.LoadAccounts());
    }

    [Fact]
    public async Task 認証に成功したら鍵を残しアカウントを保存する()
    {
        const string fp = "f2f2f2f2";
        var keychain = KeychainWithPendingKeys(fp);
        var manager = Manager(keychain, url =>
            url.Contains("/api/register/status/")
                ? """{"ok":true,"data":{"status":"verified","deviceToken":"dev-token-1"}}"""
                : null);

        var ok = await manager.AwaitVerificationAsync("token", PendingAccount(fp));

        Assert.True(ok);
        Assert.Equal(2, keychain.Stored.Count);
        var saved = Assert.Single(manager.LoadAccounts());
        Assert.Equal("dev-token-1", saved.DeviceToken);
    }

    // =================================================================
    // 公開鍵検索のサーバ応答検証
    // =================================================================

    private static string LookupJson(byte[] modulus, byte[] mlkem, string fingerprint)
    {
        var m = Convert.ToBase64String(modulus);
        var k = Convert.ToBase64String(mlkem);
        var e = Convert.ToBase64String(new byte[] { 1, 0, 1 });
        return "{\"ok\":true,\"data\":{\"registered\":true,\"keys\":[" +
               "{\"modulus\":\"" + m + "\",\"exponent\":\"" + e + "\"," +
               "\"fingerprint\":\"" + fingerprint + "\"," +
               "\"algorithmId\":18,\"mlkemPublicKey\":\"" + k + "\"}]}}";
    }

    [Fact]
    public async Task 鍵素材と食い違うfingerprintの応答は受け入れない()
    {
        var modulus = new byte[] { 1, 2, 3, 4 };
        var mlkem = new byte[] { 5, 6, 7, 8 };
        var manager = Manager(new MemoryKeychain(), url =>
            url.Contains("/api/keys/")
                ? LookupJson(modulus, mlkem, "deadbeefdeadbeef") // 偽の指紋
                : null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.LookupPublicKeyAsync("someone@example.com"));
    }

    [Fact]
    public async Task 鍵素材と一致するfingerprintなら受け入れる()
    {
        var modulus = new byte[] { 1, 2, 3, 4 };
        var mlkem = new byte[] { 5, 6, 7, 8 };
        var fp = Convert.ToHexString(
            CryptoHelper5.ComputeFingerprintFromBytes(modulus, mlkem)).ToLowerInvariant();
        var manager = Manager(new MemoryKeychain(), url =>
            url.Contains("/api/keys/")
                ? LookupJson(modulus, mlkem, fp)
                : null);

        var info = await manager.LookupPublicKeyAsync("someone@example.com");

        Assert.NotNull(info);
        Assert.Equal(fp, info!.Fingerprint);
    }

    [Fact]
    public async Task 壊れた符号化の鍵応答は受け入れない()
    {
        var manager = Manager(new MemoryKeychain(), url =>
            url.Contains("/api/keys/")
                ? """
                  {"ok":true,"data":{"registered":true,"keys":[
                    {"modulus":"%%not-base64%%","exponent":"AQAB","fingerprint":"aa",
                     "algorithmId":18,"mlkemPublicKey":"AAAA"}]}}
                  """
                : null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.LookupPublicKeyAsync("someone@example.com"));
    }
}
