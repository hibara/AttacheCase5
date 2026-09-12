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
//
// Post-PPAP の登録が途中で失敗したとき、Keychain に鍵を残さないことを見張る。
// accounts.json に載らない鍵が残っても誰も使えず、消す手立てもなくなる。
//----------------------------------------------------------------------

using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class PostPpapRollbackTests
{
    /// <summary>
    /// ハイブリッド鍵では RSA と ML-KEM を続けて保存する。
    /// 2 本目で失敗したとき、1 本目が残っていないこと。
    /// </summary>
    [Fact]
    public async Task Registration_WhenSecondKeyFailsToStore_RemovesTheFirstOne()
    {
        var keychain = new FailingKeychain(failOnAccountPrefix: "mlkem-priv-");
        var manager = new PostPpapManager(keychain);

        // 仕込んだ ML-KEM 保存失敗そのものが伝播すること
        // （ThrowsAny<Exception> だと、鍵保存より手前の別の例外でも通ってしまう）
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.StartRegistrationAsync("rollback-test@example.com"));
        Assert.Equal("SecItemAdd failed (test)", ex.Message);

        // ML-KEM の保存が実際に 1 回試みられた上での失敗であること
        Assert.Equal(1, keychain.FailingSetAttempts);

        Assert.False(keychain.Stored.Keys.Any(k => k.StartsWith("rsa-priv-")),
            "ML-KEM の保存に失敗したのに RSA 秘密鍵が Keychain に残っている");
    }

    /// <summary>
    /// 指定した接頭辞の account で Set が失敗する Keychain。
    /// </summary>
    private sealed class FailingKeychain(string failOnAccountPrefix) : IKeychainService
    {
        public Dictionary<string, byte[]> Stored { get; } = new();

        /// <summary>失敗対象（接頭辞一致）の Set が呼ばれた回数。</summary>
        public int FailingSetAttempts { get; private set; }

        public bool TryGet(string service, string account, out byte[]? data)
            => Stored.TryGetValue(account, out data);

        public void Set(string service, string account, byte[] data)
        {
            if (account.StartsWith(failOnAccountPrefix, StringComparison.Ordinal))
            {
                FailingSetAttempts++;
                throw new InvalidOperationException("SecItemAdd failed (test)");
            }

            Stored[account] = data;
        }

        public bool Delete(string service, string account) => Stored.Remove(account);
    }
}
