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
// KeychainService の Round-trip テスト。
// macOS 上でしか実行されない（他プラットフォームでは Skip 相当の早期 return）。
// 実 Keychain に書き込むため、Trait("Category","Keychain") で分離可能。
//----------------------------------------------------------------------

using System.Text;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Keychain")]
public class KeychainServiceTests
{
    private const string TestService = "org.hibara.AttacheCase.TestSuite";

    [Fact]
    public void SetGetDelete_RoundTrip()
    {
        if (!OperatingSystem.IsMacOS()) return; // 非 macOS では実行しない

        var keychain = new KeychainService();
        var account = "round-trip-" + Guid.NewGuid().ToString("N");
        var payload = Encoding.UTF8.GetBytes("Hello Keychain 日本語 🔐");

        try
        {
            keychain.Set(TestService, account, payload);

            Assert.True(keychain.TryGet(TestService, account, out var loaded));
            Assert.NotNull(loaded);
            Assert.Equal(payload, loaded);

            Assert.True(keychain.Delete(TestService, account));
            Assert.False(keychain.TryGet(TestService, account, out _));
        }
        finally
        {
            keychain.Delete(TestService, account); // 残留掃除
        }
    }

    [Fact]
    public void Set_Overwrite_ReplacesExisting()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var keychain = new KeychainService();
        var account = "overwrite-" + Guid.NewGuid().ToString("N");

        try
        {
            keychain.Set(TestService, account, Encoding.UTF8.GetBytes("first"));
            keychain.Set(TestService, account, Encoding.UTF8.GetBytes("second"));

            Assert.True(keychain.TryGet(TestService, account, out var loaded));
            Assert.Equal("second", Encoding.UTF8.GetString(loaded!));
        }
        finally
        {
            keychain.Delete(TestService, account);
        }
    }

    [Fact]
    public void Delete_NonExisting_ReturnsFalse()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var keychain = new KeychainService();
        var account = "missing-" + Guid.NewGuid().ToString("N");

        Assert.False(keychain.Delete(TestService, account));
    }

    [Fact]
    public void TryGet_NonExisting_ReturnsFalseAndNullData()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var keychain = new KeychainService();
        var account = "missing-" + Guid.NewGuid().ToString("N");

        Assert.False(keychain.TryGet(TestService, account, out var data));
        Assert.Null(data);
    }

    [Fact]
    public void Set_LargeBinaryPayload_RoundTrip()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var keychain = new KeychainService();
        var account = "large-" + Guid.NewGuid().ToString("N");

        // RSA-4096 秘密鍵 ~2.4KB / ML-KEM-768 秘密鍵 ~2.4KB を想定して 8KB のバイナリでテスト
        var payload = new byte[8 * 1024];
        new Random(42).NextBytes(payload);

        try
        {
            keychain.Set(TestService, account, payload);
            Assert.True(keychain.TryGet(TestService, account, out var loaded));
            Assert.Equal(payload, loaded);
        }
        finally
        {
            keychain.Delete(TestService, account);
        }
    }
}
