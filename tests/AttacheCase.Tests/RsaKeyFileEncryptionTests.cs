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
// RSA 鍵ファイル（.atcpub / .atcpvt）による暗号化・復号。
//
// 5.0 で一度畳み、5.5 で復活させた経路（Core の Crypto/RsaKeyFile）。
// 形式は v150 の公開鍵方式で、宛先エントリは RSA 単体。ここでは
//   - 4096 / 2048 ビットの鍵で往復できること（GUI の生成器 / AttacheCase 4 の鍵に相当）
//   - 別の秘密鍵・パスワードでは開けないこと
//   - 2048 / 4096 以外の鍵長は暗号化を断り、出力を残さないこと
//   - ファイル一覧（GetFileList）も秘密鍵で取れること
// を見る。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AttacheCase.Crypto;
using AttacheCase.Models;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class RsaKeyFileEncryptionTests : IDisposable
{
    private readonly string _dir;

    public RsaKeyFileEncryptionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ac5-rsa-keyfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 後始末の失敗は無視 */ }
    }

    /// <summary>鍵ペアを XML（RSAKeyValue）で作る。token / id は付けない（照合に使わないため）</summary>
    private static (string publicXml, string privateXml) MakeKeyPair(int bits)
    {
        using var rsa = RSA.Create(bits);
        return (rsa.ToXmlString(false), rsa.ToXmlString(true));
    }

    private async Task<string> WriteInputAsync(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(2048)]
    public async Task 公開鍵で暗号化し秘密鍵で復号できる(int bits)
    {
        var (publicXml, privateXml) = MakeKeyPair(bits);
        var input = await WriteInputAsync("hello.txt", "secret payload " + bits);
        var atc = Path.Combine(_dir, "out" + bits + ".atc");
        var outDir = Path.Combine(_dir, "dec" + bits);

        var enc = await new EncryptionService().EncryptAsync(
            new[] { input }, atc, string.Empty,
            new EncryptionOptions { IsUseRsaEncryption = true, RsaPublicKeyXml = publicXml });
        Assert.True(enc.Success, "暗号化失敗: " + enc.ErrorMessage);
        Assert.True(File.Exists(atc));

        var dec = await new DecryptionService().DecryptAsync(
            atc, outDir, string.Empty,
            new DecryptionOptions { IsUseRsaEncryption = true, RsaPrivateKeyXml = privateXml });
        Assert.True(dec.Success, "復号失敗: " + dec.ErrorMessage);

        var restored = Directory.GetFiles(outDir, "hello.txt", SearchOption.AllDirectories);
        Assert.Single(restored);
        Assert.Equal("secret payload " + bits, await File.ReadAllTextAsync(restored[0]));
    }

    [Fact]
    public async Task 別の秘密鍵やパスワードでは復号できない()
    {
        var (publicXml, _) = MakeKeyPair(2048);
        var (_, otherPrivateXml) = MakeKeyPair(2048);
        var input = await WriteInputAsync("hello.txt", "secret");
        var atc = Path.Combine(_dir, "out.atc");
        var outDir = Path.Combine(_dir, "dec");

        var enc = await new EncryptionService().EncryptAsync(
            new[] { input }, atc, string.Empty,
            new EncryptionOptions { IsUseRsaEncryption = true, RsaPublicKeyXml = publicXml });
        Assert.True(enc.Success, enc.ErrorMessage);

        var wrongKey = await new DecryptionService().DecryptAsync(
            atc, outDir, string.Empty,
            new DecryptionOptions { IsUseRsaEncryption = true, RsaPrivateKeyXml = otherPrivateXml });
        Assert.False(wrongKey.Success, "別の秘密鍵で復号できてしまった");

        var wrongPassword = await new DecryptionService().DecryptAsync(
            atc, outDir, "password", new DecryptionOptions());
        Assert.False(wrongPassword.Success, "パスワードで復号できてしまった");

        Assert.False(Directory.Exists(outDir) && Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Length > 0,
            "失敗したのに出力ファイルが残っている");
    }

    [Fact]
    public async Task 対応外の鍵長は暗号化を断り出力を残さない()
    {
        var (publicXml, _) = MakeKeyPair(1024);
        var input = await WriteInputAsync("hello.txt", "secret");
        var atc = Path.Combine(_dir, "out.atc");

        var enc = await new EncryptionService().EncryptAsync(
            new[] { input }, atc, string.Empty,
            new EncryptionOptions { IsUseRsaEncryption = true, RsaPublicKeyXml = publicXml });

        Assert.False(enc.Success, "1024 ビットの鍵で暗号化が通ってしまった");
        Assert.Contains("1024", enc.ErrorMessage);
        Assert.False(File.Exists(atc), "断ったのに出力ファイルが残っている");
    }

    [Fact]
    public async Task 壊れた公開鍵XMLは暗号化を断り出力を残さない()
    {
        var input = await WriteInputAsync("hello.txt", "secret");
        var atc = Path.Combine(_dir, "out.atc");

        var enc = await new EncryptionService().EncryptAsync(
            new[] { input }, atc, string.Empty,
            new EncryptionOptions { IsUseRsaEncryption = true, RsaPublicKeyXml = "<RSAKeyValue><Modulus>x</Modulus></RSAKeyValue>" });

        Assert.False(enc.Success);
        Assert.False(File.Exists(atc));
    }

    [Fact]
    public async Task ファイル一覧も秘密鍵で取れる()
    {
        var (publicXml, privateXml) = MakeKeyPair(2048);
        var input = await WriteInputAsync("listed.txt", "secret");
        var atc = Path.Combine(_dir, "out.atc");

        var enc = await new EncryptionService().EncryptAsync(
            new[] { input }, atc, string.Empty,
            new EncryptionOptions { IsUseRsaEncryption = true, RsaPublicKeyXml = publicXml });
        Assert.True(enc.Success, enc.ErrorMessage);

        var list = await new DecryptionService().GetFileListAsync(
            atc, string.Empty,
            new DecryptionOptions { IsUseRsaEncryption = true, RsaPrivateKeyXml = privateXml });
        Assert.True(list.Success, "一覧取得失敗: " + list.ErrorMessage);
        Assert.Contains(list.FilePaths, f => f.EndsWith("listed.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void 宛先変換は鍵長で識別子を決め指紋は秘密鍵側と一致する()
    {
        var (publicXml, privateXml) = MakeKeyPair(2048);

        var recipient = RsaKeyFile.CreateRecipient(publicXml);
        Assert.Equal(PkaAlgorithmId.Rsa2048, recipient.AlgorithmId);
        Assert.Null(recipient.MlKemPublicKey);

        var privateKey = RsaKeyFile.ParsePrivateKey(privateXml);
        var fromPrivate = Convert.ToHexString(RsaKeyFile.ComputeFingerprint(privateKey));
        Assert.Equal(recipient.Fingerprint, fromPrivate);
    }
}
