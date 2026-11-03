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
// Post-PPAP（公開鍵方式）の暗号化/復号エンドツーエンドテスト。
// 鍵サーバは叩かず、ローカルで鍵ペアを生成し
// accounts.json と Keychain を直接セットアップして完結させる。
//
// macOS 上でのみ意味があり、実 Keychain に書き込む。
//----------------------------------------------------------------------

using System.Text;
using AttacheCase.Crypto;
using AttacheCase.Models;
using AttacheCase.Services;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Post-PPAP")]
public class PostPpapEncryptDecryptTests
{
    [Fact]
    public async Task EncryptDecrypt_HybridSelfRecipient_RoundTrip()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var mgr = new PostPpapManager();
        var keychain = new KeychainService();
        var fingerprintHex = string.Empty;

        try
        {
            // ── ① 自分宛アカウントを Keychain + accounts.json に直接セットアップ ──
            var (publicAccount, rsaPrivDer, mlkemPrivDer) = GenerateLocalAccount();
            fingerprintHex = publicAccount.Fingerprint;

            keychain.Set(PostPpapManager.KeychainService,
                "rsa-priv-" + fingerprintHex, rsaPrivDer);
            keychain.Set(PostPpapManager.KeychainService,
                "mlkem-priv-" + fingerprintHex, mlkemPrivDer);

            var accounts = mgr.LoadAccounts();
            accounts.Add(publicAccount);
            mgr.SaveAccounts(accounts);

            // ── ② 自分宛に暗号化 ──
            var tempDir = Path.Combine(Path.GetTempPath(),
                "ac5-ppap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var input = Path.Combine(tempDir, "hello.txt");
                var atc = Path.Combine(tempDir, "out.atc");
                var outDir = Path.Combine(tempDir, "decrypted");
                Directory.CreateDirectory(outDir);

                var payload = Encoding.UTF8.GetBytes("Hello Post-PPAP 🔐 日本語テスト");
                await File.WriteAllBytesAsync(input, payload);

                var encResult = await new EncryptionService().EncryptAsync(
                    new[] { input },
                    atc,
                    password: null!,    // 公開鍵方式ではパスワード不要
                    new EncryptionOptions
                    {
                        Recipients = new List<PostPpapKeyInfo> { publicAccount },
                    });

                Assert.True(encResult.Success,
                    $"暗号化失敗: {encResult.ErrorMessage}");

                // 公開鍵方式として書かれていること。
                // GUI はこの判定でパスワード入力を省いて自動復号に入るため、
                // false になると「パスワードを要求される」不具合に戻る
                Assert.True(DecryptionService.IsPublicKeyEncrypted(atc),
                    "公開鍵方式のファイルとして判定されない");
                // RSA 鍵ファイル方式と取り違えると、メイン画面が秘密鍵（.atcpvt）待ちの画面へ送ってしまう
                Assert.Equal(PublicKeyEncryptionKind.PostPpap,
                    DecryptionService.GetPublicKeyEncryptionKind(atc));

                // ── ③ 自分の秘密鍵で復号 ──
                var decResult = await new DecryptionService().DecryptAsync(
                    atc, outDir, password: string.Empty,
                    new DecryptionOptions());

                Assert.True(decResult.Success,
                    $"復号失敗: {decResult.ErrorMessage}");

                // ── ④ 内容比較 ──
                var decryptedFile = Directory
                    .GetFiles(outDir, "*", SearchOption.AllDirectories)
                    .Single();
                var actual = await File.ReadAllBytesAsync(decryptedFile);
                Assert.Equal(payload, actual);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }
        finally
        {
            // テストアカウントだけを掃除（既存アカウントは残す）
            if (!string.IsNullOrEmpty(fingerprintHex))
                mgr.DeleteAccount(fingerprintHex);
        }
    }

    [Fact]
    public async Task Decrypt_NoMatchingPrivateKey_ReturnsError()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var mgr = new PostPpapManager();
        var keychain = new KeychainService();
        var senderFp = string.Empty;

        try
        {
            // 受信者 A の鍵で暗号化するが、A は accounts.json/Keychain に登録しない
            // → 復号時に「秘密鍵が見つかりません」エラーになることを確認
            var (recipientAccount, _, _) = GenerateLocalAccount();
            senderFp = recipientAccount.Fingerprint;

            var tempDir = Path.Combine(Path.GetTempPath(),
                "ac5-ppap-nomatch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var input = Path.Combine(tempDir, "secret.txt");
                var atc = Path.Combine(tempDir, "out.atc");
                var outDir = Path.Combine(tempDir, "decrypted");
                Directory.CreateDirectory(outDir);
                await File.WriteAllBytesAsync(input, "secret"u8.ToArray());

                var encResult = await new EncryptionService().EncryptAsync(
                    new[] { input }, atc, null!,
                    new EncryptionOptions
                    {
                        Recipients = new List<PostPpapKeyInfo> { recipientAccount },
                    });
                Assert.True(encResult.Success);

                var decResult = await new DecryptionService().DecryptAsync(
                    atc, outDir, password: string.Empty,
                    new DecryptionOptions());

                Assert.False(decResult.Success);
                // 環境によって accounts.json が空(= Error_PostPpapNoAccount)か、
                // 他アカウント有(= Error_NoMatchingPrivateKey)かのどちらかが返る。
                // メッセージは表示言語に依存するため、日本語の部分文字列ではなく
                // 実際のリソース値と比較する（en カルチャでも通す）
                var localize = LocalizationService.Instance;
                var expected = new[]
                {
                    localize["Error_PostPpapNoAccount"],
                    localize["Error_NoMatchingPrivateKey"],
                };
                Assert.True(
                    Array.Exists(expected, m => decResult.ErrorMessage.Contains(m)),
                    $"Unexpected error message: {decResult.ErrorMessage}");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }
        finally
        {
            // 念のため Keychain には何も登録していないが、保険として削除を試みる
            if (!string.IsNullOrEmpty(senderFp))
            {
                keychain.Delete(PostPpapManager.KeychainService, "rsa-priv-" + senderFp);
                keychain.Delete(PostPpapManager.KeychainService, "mlkem-priv-" + senderFp);
            }
        }
    }

    // -----------------------------------------------------------------
    // ヘルパー: ローカルで鍵ペアを生成し、accounts.json に書ける形の
    // PostPpapKeyInfo と、Keychain に保存すべき DER エンコード秘密鍵を返す。
    // -----------------------------------------------------------------
    private static (PostPpapKeyInfo Account, byte[] RsaPrivDer, byte[] MlKemPrivDer)
        GenerateLocalAccount()
    {
        var rsaPair = CryptoHelper5.GenerateRsaKeyPair(4096);
        var rsaPub = (RsaKeyParameters)rsaPair.Public;
        var rsaPrivDer = PrivateKeyInfoFactory
            .CreatePrivateKeyInfo(rsaPair.Private).GetDerEncoded();

        var mlkemGen = new MLKemKeyPairGenerator();
        mlkemGen.Init(new MLKemKeyGenerationParameters(
            new SecureRandom(), MLKemParameters.ml_kem_768));
        var mlkemPair = mlkemGen.GenerateKeyPair();
        var mlkemPub = (MLKemPublicKeyParameters)mlkemPair.Public;
        var mlkemPrivParams = (MLKemPrivateKeyParameters)mlkemPair.Private;
        var mlkemPubBytes = mlkemPub.GetEncoded();
        // raw 形式（PostPpapManager.LoadMlKemPrivateKey と整合）
        var mlkemPrivDer = mlkemPrivParams.GetEncoded();

        var modulusBytes = rsaPub.Modulus.ToByteArrayUnsigned();
        var exponentBytes = rsaPub.Exponent.ToByteArrayUnsigned();
        var fpBytes = CryptoHelper5.ComputeFingerprintFromBytes(modulusBytes, mlkemPubBytes);
        var fpHex = ToHexLower(fpBytes);

        var account = new PostPpapKeyInfo
        {
            Email = "test+" + Guid.NewGuid().ToString("N") + "@example.com",
            EmailHash = ToHexLower(System.Security.Cryptography.SHA256
                .HashData(Encoding.UTF8.GetBytes("test@example.com"))),
            Modulus = Convert.ToBase64String(modulusBytes),
            Exponent = Convert.ToBase64String(exponentBytes),
            Fingerprint = fpHex,
            AlgorithmId = PkaAlgorithmId.Rsa4096MlKem768,
            MlKemPublicKey = Convert.ToBase64String(mlkemPubBytes),
            RegisteredAt = DateTime.UtcNow,
        };

        return (account, rsaPrivDer, mlkemPrivDer);
    }

    private static string ToHexLower(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
