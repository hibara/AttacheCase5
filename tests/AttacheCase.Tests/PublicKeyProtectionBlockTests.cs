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
// 公開鍵方式の鍵保護ブロックの build / parse / 復号往復テスト。
// 鍵サーバには接続しない（ローカルで鍵ペアを生成して完結）。
//----------------------------------------------------------------------

using AttacheCase.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Xunit;

namespace AttacheCase.Tests;

public class PublicKeyProtectionBlockTests
{
    [Fact]
    public void HybridSingleRecipient_RoundTrip()
    {
        var (rsaPriv, rsaPub) = GenerateRsa(4096);
        var (mlkemPriv, mlkemPub) = GenerateMlKem();

        var commonKey = CryptoHelper5.GenerateCommonKey();
        var fingerprint = CryptoHelper5.ComputeFingerprint(rsaPub, mlkemPub.GetEncoded());

        var block = CryptoHelper5.BuildPublicKeyProtectionBlock(commonKey, new[]
        {
            new CryptoHelper5.RecipientKeyEntry
            {
                Fingerprint = fingerprint,
                PublicKey = rsaPub,
                MlKemPublicKey = mlkemPub,
                AlgorithmId = PkaAlgorithmId.Rsa4096MlKem768,
            }
        });

        var parsed = CryptoHelper5.ParsePublicKeyProtectionBlock(block, 0, out var bytesRead);

        Assert.Equal(block.Length, bytesRead);
        Assert.Single(parsed);
        Assert.Equal(fingerprint, parsed[0].Fingerprint);

        var rsaCipherSize = CryptoHelper5.GetRsaCipherSize(PkaAlgorithmId.Rsa4096MlKem768);
        var recovered = CryptoHelper5.UnprotectCommonKeyWithHybrid(
            parsed[0].EncryptedCommonKey, rsaPriv, mlkemPriv, rsaCipherSize);

        Assert.Equal(commonKey, recovered);
    }

    [Fact]
    public void MultiRecipient_EachCanDecrypt()
    {
        var (rsaPrivA, rsaPubA) = GenerateRsa(4096);
        var (mlkemPrivA, mlkemPubA) = GenerateMlKem();
        var (rsaPrivB, rsaPubB) = GenerateRsa(4096);
        var (mlkemPrivB, mlkemPubB) = GenerateMlKem();

        var commonKey = CryptoHelper5.GenerateCommonKey();
        var fpA = CryptoHelper5.ComputeFingerprint(rsaPubA, mlkemPubA.GetEncoded());
        var fpB = CryptoHelper5.ComputeFingerprint(rsaPubB, mlkemPubB.GetEncoded());

        var block = CryptoHelper5.BuildPublicKeyProtectionBlock(commonKey, new[]
        {
            new CryptoHelper5.RecipientKeyEntry
            { Fingerprint = fpA, PublicKey = rsaPubA, MlKemPublicKey = mlkemPubA, AlgorithmId = PkaAlgorithmId.Rsa4096MlKem768 },
            new CryptoHelper5.RecipientKeyEntry
            { Fingerprint = fpB, PublicKey = rsaPubB, MlKemPublicKey = mlkemPubB, AlgorithmId = PkaAlgorithmId.Rsa4096MlKem768 },
        });

        var parsed = CryptoHelper5.ParsePublicKeyProtectionBlock(block, 0, out _);
        Assert.Equal(2, parsed.Count);

        var rsaCipherSize = CryptoHelper5.GetRsaCipherSize(PkaAlgorithmId.Rsa4096MlKem768);

        // Aの秘密鍵で復号
        var entryA = FindByFingerprint(parsed, fpA);
        var keyByA = CryptoHelper5.UnprotectCommonKeyWithHybrid(
            entryA.EncryptedCommonKey, rsaPrivA, mlkemPrivA, rsaCipherSize);
        Assert.Equal(commonKey, keyByA);

        // Bの秘密鍵で復号
        var entryB = FindByFingerprint(parsed, fpB);
        var keyByB = CryptoHelper5.UnprotectCommonKeyWithHybrid(
            entryB.EncryptedCommonKey, rsaPrivB, mlkemPrivB, rsaCipherSize);
        Assert.Equal(commonKey, keyByB);
    }

    [Fact]
    public void RsaOnlyRecipient_RoundTrip()
    {
        var (rsaPriv, rsaPub) = GenerateRsa(2048);
        var commonKey = CryptoHelper5.GenerateCommonKey();
        var fp = CryptoHelper5.ComputeFingerprint(rsaPub);

        var block = CryptoHelper5.BuildPublicKeyProtectionBlock(commonKey, new[]
        {
            new CryptoHelper5.RecipientKeyEntry
            {
                Fingerprint = fp, PublicKey = rsaPub, AlgorithmId = PkaAlgorithmId.Rsa2048,
            },
        });

        var parsed = CryptoHelper5.ParsePublicKeyProtectionBlock(block, 0, out _);
        var recovered = CryptoHelper5.UnprotectCommonKeyWithRsa(
            parsed[0].EncryptedCommonKey, rsaPriv);
        Assert.Equal(commonKey, recovered);
    }

    [Fact]
    public void GetRsaCipherSize_KnownAlgorithms()
    {
        Assert.Equal(256, CryptoHelper5.GetRsaCipherSize(PkaAlgorithmId.Rsa2048));
        Assert.Equal(256, CryptoHelper5.GetRsaCipherSize(PkaAlgorithmId.Rsa2048MlKem768));
        Assert.Equal(512, CryptoHelper5.GetRsaCipherSize(PkaAlgorithmId.Rsa4096));
        Assert.Equal(512, CryptoHelper5.GetRsaCipherSize(PkaAlgorithmId.Rsa4096MlKem768));
    }

    // -----------------------------------------------------------------
    // ヘルパー
    // -----------------------------------------------------------------
    private static (RsaKeyParameters priv, RsaKeyParameters pub) GenerateRsa(int bits)
    {
        var pair = CryptoHelper5.GenerateRsaKeyPair(bits);
        return ((RsaKeyParameters)pair.Private, (RsaKeyParameters)pair.Public);
    }

    private static (MLKemPrivateKeyParameters priv, MLKemPublicKeyParameters pub) GenerateMlKem()
    {
        var gen = new MLKemKeyPairGenerator();
        gen.Init(new MLKemKeyGenerationParameters(new SecureRandom(), MLKemParameters.ml_kem_768));
        var pair = gen.GenerateKeyPair();
        return ((MLKemPrivateKeyParameters)pair.Private, (MLKemPublicKeyParameters)pair.Public);
    }

    private static CryptoHelper5.ParsedRecipientEntry FindByFingerprint(
        IReadOnlyList<CryptoHelper5.ParsedRecipientEntry> entries, byte[] fp)
    {
        foreach (var e in entries)
            if (CryptoHelper5.ConstantTimeEquals(e.Fingerprint, fp))
                return e;
        throw new Xunit.Sdk.XunitException("Fingerprint not found in parsed entries.");
    }
}
