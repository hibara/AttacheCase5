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
// PostPpapManager 内部の最小ラウンドトリップ確認テスト。
// 鍵生成 → DER エンコード → Keychain 保存 → DER 取得 → BC parameter 復元 が
// それぞれどこで失敗するか切り分ける。
//----------------------------------------------------------------------

using AttacheCase.Crypto;
using AttacheCase.Models;
using AttacheCase.Services;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Pqc.Crypto.Utilities;
using Org.BouncyCastle.Security;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Post-PPAP")]
public class PostPpapManagerInternalTests
{
    [Fact]
    public void RsaPrivateKey_DerRoundTrip_Pure()
    {
        var pair = CryptoHelper5.GenerateRsaKeyPair(4096);
        var der = PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded();
        var loaded = (RsaPrivateCrtKeyParameters)PrivateKeyFactory.CreateKey(der);
        Assert.NotNull(loaded);
        Assert.Equal(((RsaKeyParameters)pair.Private).Modulus, loaded.Modulus);
    }

    [Fact]
    public void MlKemPrivateKey_RawRoundTrip_Pure()
    {
        var gen = new MLKemKeyPairGenerator();
        gen.Init(new MLKemKeyGenerationParameters(new SecureRandom(), MLKemParameters.ml_kem_768));
        var pair = gen.GenerateKeyPair();
        var raw = ((MLKemPrivateKeyParameters)pair.Private).GetEncoded();
        var loaded = MLKemPrivateKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, raw);
        Assert.NotNull(loaded);
    }

    [Fact]
    public void RsaPrivateKey_ThroughKeychain_RoundTrip()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var pair = CryptoHelper5.GenerateRsaKeyPair(4096);
        var der = PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded();
        var keychain = new KeychainService();
        var account = "test-rsa-" + Guid.NewGuid().ToString("N");
        try
        {
            keychain.Set(PostPpapManager.KeychainService, account, der);
            Assert.True(keychain.TryGet(PostPpapManager.KeychainService, account, out var roundTripped));
            Assert.Equal(der, roundTripped);
            var loaded = (RsaPrivateCrtKeyParameters)PrivateKeyFactory.CreateKey(roundTripped!);
            Assert.NotNull(loaded);
        }
        finally
        {
            keychain.Delete(PostPpapManager.KeychainService, account);
        }
    }
}
