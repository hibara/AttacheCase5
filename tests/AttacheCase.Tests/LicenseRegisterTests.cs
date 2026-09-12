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
// 商用ライセンス（レジストレーションコード）検証のテスト。
// 埋め込み鍵の公開部で本物と同じ形式のコードを作り、往復で検証する。
// 保存先はメモリ上のフェイクを注入し、実レジストリ・実ファイルには触れない。
//----------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Text;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class LicenseRegisterTests
{
    private sealed class FakeStore : ILicenseCodeStore
    {
        public string Stored = "";
        public string Read() => Stored;
        public void Write(string code) => Stored = code;
        public void Delete() => Stored = "";
    }

    /// <summary>
    /// 復号鍵が組み込まれているか。鍵はリポジトリ外（_secrets\）にあり、公開ソースから
    /// のビルドでは空鍵になるため、コードの往復を要するテストはその場合スキップする
    /// （xunit 2 に動的 Skip が無いので早期 return）。
    /// </summary>
    private static bool HasKey => LicenseRegister.HasPrivateKey;

    /// <summary>本物の発行処理と同じ形式でコードを作る（公開鍵で OAEP-SHA1 暗号化 → 16 進）。</summary>
    private static string MakeCode(string payload)
    {
        using var rsa = RSA.Create();
        rsa.FromXmlString(LicenseRegister.PrivateKey); // 公開部も含まれている
        var encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes(payload), RSAEncryptionPadding.OaepSHA1);
        return Convert.ToHexString(encrypted);
    }

    [Fact]
    public void 鍵なしビルドでは常に未登録扱い()
    {
        if (HasKey) return; // 鍵ありビルドでは対象外

        Assert.False(new LicenseRegister("DEADBEEF", new FakeStore()).Decrypt());
    }

    [Fact]
    public void 正しいコードを検証すると氏名とメールアドレスが取れる()
    {
        if (!HasKey) return;

        var code = MakeCode("HIBARA\t山田太郎\ttaro@example.com");
        var store = new FakeStore();
        var license = new LicenseRegister(code, store);

        Assert.True(license.Decrypt());
        Assert.Equal("山田太郎", license.UserNameString);
        Assert.Equal("taro@example.com", license.EmailAddressString);
        Assert.Equal("", store.Stored); // fRegister=false では保存しない
    }

    [Fact]
    public void fRegisterで保存され次回はストアから読める()
    {
        if (!HasKey) return;

        var code = MakeCode("HIBARA\tYamada\tyamada@example.com");
        var store = new FakeStore();

        Assert.True(new LicenseRegister(code, store).Decrypt(fRegister: true));
        Assert.Equal(code, store.Stored);

        // コード引数なし → ストアから読んで検証できる
        var second = new LicenseRegister(store: store);
        Assert.True(second.GetCommercialLicense());
        Assert.Equal("Yamada", second.UserNameString);
    }

    [Fact]
    public void 改行入りのコードも受理する()
    {
        if (!HasKey) return;

        var raw = MakeCode("HIBARA\tA\ta@example.com");
        var wrapped = string.Join(Environment.NewLine,
            raw.Substring(0, 64), raw.Substring(64, 64), raw.Substring(128));

        Assert.True(new LicenseRegister(wrapped, new FakeStore()).Decrypt());
    }

    [Fact]
    public void トークンが違えば失敗する()
    {
        if (!HasKey) return;

        var code = MakeCode("NOTHIBARA\tX\tx@example.com");
        var license = new LicenseRegister(code, new FakeStore());

        Assert.False(license.Decrypt());
        Assert.Equal("", license.UserNameString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("zzzz not hex")]
    [InlineData("DEADBEEF")] // 16 進だが RSA ブロックとして不正
    public void 不正なコードは失敗する(string code)
    {
        Assert.False(new LicenseRegister(code, new FakeStore()).Decrypt());
    }

    [Fact]
    public void 削除すると未登録に戻る()
    {
        if (!HasKey) return;

        var code = MakeCode("HIBARA\tB\tb@example.com");
        var store = new FakeStore();
        var license = new LicenseRegister(code, store);
        Assert.True(license.Decrypt(fRegister: true));

        Assert.True(license.DeleteLicense());
        Assert.Equal("", store.Stored);
        Assert.Equal("", license.UserNameString);
        Assert.False(new LicenseRegister(store: store).GetCommercialLicense());
    }

    [Fact]
    public void ファイルストアの往復()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("atc-license-test").FullName;
        try
        {
            var path = System.IO.Path.Combine(dir, "registration.code");
            var store = new FileLicenseCodeStore(path);

            Assert.Equal("", store.Read());
            store.Write("CODE123");
            Assert.Equal("CODE123", store.Read());
            store.Delete();
            Assert.Equal("", store.Read());
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }
}
