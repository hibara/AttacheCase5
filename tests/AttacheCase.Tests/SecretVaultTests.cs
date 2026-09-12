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
// SecretVault（記憶パスワードの保管庫）のテスト。
//
// 「端末」は IKeychainService の実体で表す。同じ FakeKeychain を渡せば同じ端末、
// 空の別インスタンスを渡せば初めての端末。OS の鍵ストアには触れないので、
// どの OS でも同じように走る。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AttacheCase.Services;
using AttacheCase.Settings;
using Xunit;

namespace AttacheCase.Tests;

public class SecretVaultTests : IDisposable
{
    private readonly string _dir;
    private string VaultPath => Path.Combine(_dir, SecretVault.FileName);

    public SecretVaultTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "AtcVaultTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>鍵ストアの偽物。1 インスタンス = 1 端末（の 1 ユーザー）。</summary>
    private sealed class FakeKeychain : IKeychainService
    {
        public readonly Dictionary<(string, string), byte[]> Items = new();

        public bool TryGet(string service, string account, out byte[]? data)
        {
            var found = Items.TryGetValue((service, account), out var value);
            data = found ? value : null;
            return found;
        }

        public void Set(string service, string account, byte[] data)
            => Items[(service, account)] = (byte[])data.Clone();

        public bool Delete(string service, string account)
            => Items.Remove((service, account));
    }

    private static Dictionary<string, string> SampleSecrets => new()
    {
        ["encryptPassword"] = "encrypt-Pass#123",
        ["decryptPassword"] = "復号ぱすわーど",
    };

    // =====================================================================

    [Fact]
    public void 作成した端末では無入力で読み書きできる()
    {
        var machine = new FakeKeychain();

        using (var session = SecretVault.CreateNew(VaultPath, machine, "test-device"))
        {
            session.SetSecrets(SampleSecrets);
        }

        // 読み直して device プロテクターで開く（平常時の経路）
        var vault = SecretVault.TryLoad(VaultPath);
        Assert.NotNull(vault);
        using var reopened = vault!.TryUnlockWithDevice(machine);
        Assert.NotNull(reopened);

        var secrets = reopened!.GetSecrets();
        Assert.NotNull(secrets);
        Assert.Equal("encrypt-Pass#123", secrets!["encryptPassword"]);
        Assert.Equal("復号ぱすわーど", secrets["decryptPassword"]);
    }

    [Fact]
    public void 別の端末では開けない()
    {
        var machineA = new FakeKeychain();
        using (var session = SecretVault.CreateNew(VaultPath, machineA, "A"))
        {
            session.SetSecrets(SampleSecrets);
        }

        // ファイルだけ持ち出された状況 = 鍵ストアが空の端末
        var machineB = new FakeKeychain();
        var vault = SecretVault.TryLoad(VaultPath);
        Assert.Null(vault!.TryUnlockWithDevice(machineB));
    }

    [Fact]
    public void 移行コードで新しい端末を登録できる_コードは使い捨てになる()
    {
        var machineA = new FakeKeychain();
        string code;
        using (var session = SecretVault.CreateNew(VaultPath, machineA, "A"))
        {
            session.SetSecrets(SampleSecrets);
            code = session.IssueMigrationCode();
        }

        Assert.Matches(@"^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$", code);

        // 新しい端末で、コードを入力して登録する
        var machineB = new FakeKeychain();
        var vault = SecretVault.TryLoad(VaultPath)!;
        using (var session = vault.TryUnlockWithCode(code, machineB, registerThisDevice: true, "B"))
        {
            Assert.NotNull(session);
            Assert.Equal("encrypt-Pass#123", session!.GetSecrets()!["encryptPassword"]);
        }

        // 使った時点でコードは消えている（登録の有無にかかわらず）
        var reloaded = SecretVault.TryLoad(VaultPath)!;
        Assert.False(reloaded.HasActiveCodeProtector());
        Assert.Null(reloaded.TryUnlockWithCode(code, new FakeKeychain(), false, "C"));

        // 端末 B は以後、無入力で開ける。端末 A も無傷
        Assert.NotNull(reloaded.TryUnlockWithDevice(machineB));
        Assert.NotNull(reloaded.TryUnlockWithDevice(machineA));
    }

    [Fact]
    public void 移行コードを発行し直すと前のコードは無効になる()
    {
        var machine = new FakeKeychain();
        string code1, code2;
        using (var session = SecretVault.CreateNew(VaultPath, machine, "A"))
        {
            session.SetSecrets(SampleSecrets);
            code1 = session.IssueMigrationCode();
            code2 = session.IssueMigrationCode();
        }

        var vault = SecretVault.TryLoad(VaultPath)!;
        Assert.Null(vault.TryUnlockWithCode(code1, new FakeKeychain(), false, "B"));

        var vault2 = SecretVault.TryLoad(VaultPath)!;
        Assert.NotNull(vault2.TryUnlockWithCode(code2, new FakeKeychain(), false, "B"));
    }

    [Fact]
    public void 期限切れの移行コードは使えず_device開錠時に自動削除される()
    {
        var machine = new FakeKeychain();
        string code;
        using (var session = SecretVault.CreateNew(VaultPath, machine, "A"))
        {
            session.SetSecrets(SampleSecrets);
            code = session.IssueMigrationCode(TimeSpan.FromMinutes(-1)); // 既に期限切れ
        }

        var vault = SecretVault.TryLoad(VaultPath)!;
        Assert.Null(vault.TryUnlockWithCode(code, new FakeKeychain(), false, "B"));

        // 信頼済み端末で開いたときに掃除される（申し送り §6.5）
        var vault2 = SecretVault.TryLoad(VaultPath)!;
        using var session2 = vault2.TryUnlockWithDevice(machine);
        Assert.NotNull(session2);

        var reloaded = SecretVault.TryLoad(VaultPath)!;
        Assert.DoesNotContain(reloaded.ListProtectorIds(), p => p.Type == "code");
    }

    [Fact]
    public void 期限を書き換えると認証タグで弾かれる()
    {
        var machine = new FakeKeychain();
        string code;
        string originalExpiry;
        using (var session = SecretVault.CreateNew(VaultPath, machine, "A"))
        {
            session.SetSecrets(SampleSecrets);
            code = session.IssueMigrationCode(TimeSpan.FromMinutes(-1)); // 期限切れで発行
            originalExpiry = session.ListProtectors().Single(p => p.Type == "code").ExpiresAt!;
        }

        // ファイル上の expiresAt を未来に書き換える（平文なので書き換え自体はできる）
        var future = DateTimeOffset.UtcNow.AddDays(30)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(VaultPath, File.ReadAllText(VaultPath).Replace(originalExpiry, future));

        // 期限判定は通る（未来になった）が、AAD が合わなくなり復号で必ず失敗する
        var vault = SecretVault.TryLoad(VaultPath)!;
        Assert.True(vault.HasActiveCodeProtector());
        Assert.Null(vault.TryUnlockWithCode(code, new FakeKeychain(), false, "B"));
    }

    [Fact]
    public void ラベルと作成日時はファイルに平文で残らない()
    {
        var machine = new FakeKeychain();
        const string label = "SECRET-OFFICE-PC-LABEL";
        using (var session = SecretVault.CreateNew(VaultPath, machine, label))
        {
            session.SetSecrets(SampleSecrets);

            // 開いていれば管理画面用に読める
            var info = session.ListProtectors().Single(p => p.Type == "device");
            Assert.Equal(label, info.Label);
            Assert.NotEqual("", info.CreatedAt);
        }

        // ファイルを拾った人には見えない（申し送り §5.1）
        var raw = File.ReadAllText(VaultPath);
        Assert.DoesNotContain(label, raw);
        Assert.DoesNotContain("encrypt-Pass#123", raw);
    }

    [Fact]
    public void 開けないdeviceプロテクターは削除されない()
    {
        // 端末 A で作り、端末 B を移行コードで登録 → A の鍵ストアからは B の
        // エントリは開けないが、開けないという理由で消してはいけない（申し送り §7.1）
        var machineA = new FakeKeychain();
        string code;
        using (var session = SecretVault.CreateNew(VaultPath, machineA, "A"))
        {
            session.SetSecrets(SampleSecrets);
            code = session.IssueMigrationCode();
        }

        var machineB = new FakeKeychain();
        SecretVault.TryLoad(VaultPath)!
            .TryUnlockWithCode(code, machineB, registerThisDevice: true, "B")!.Dispose();

        // A で開き直しても、B の device プロテクターは残っている
        var vault = SecretVault.TryLoad(VaultPath)!;
        vault.TryUnlockWithDevice(machineA)!.Dispose();

        var reloaded = SecretVault.TryLoad(VaultPath)!;
        Assert.Equal(2, reloaded.ListProtectorIds().Count(p => p.Type == "device"));
    }

    // =====================================================================
    // 移行コードの表記
    // =====================================================================

    [Theory]
    [InlineData("7FK3-9XQ2-MD4V-B8TN", "7FK39XQ2MD4VB8TN")] // ハイフンは無視
    [InlineData("7fk3 9xq2 md4v b8tn", "7FK39XQ2MD4VB8TN")] // 小文字・空白も可
    [InlineData("7FKO-9XQ2-MD4V-B8TN", "7FK09XQ2MD4VB8TN")] // O → 0
    [InlineData("7FKI-9XQ2-MD4V-B8TL", "7FK19XQ2MD4VB8T1")] // I / L → 1
    public void 移行コードの入力は読み替えて正規化される(string input, string expected)
    {
        Assert.Equal(expected, MigrationCode.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7FK3-9XQ2-MD4V")]      // 短い
    [InlineData("7FK3-9XQ2-MD4V-B8TNX")] // 長い
    [InlineData("7FKU-9XQ2-MD4V-B8TN")] // U はアルファベットに無い
    [InlineData("7FK3-9XQ2-MD4V-B8T!")] // 記号
    public void 読めない移行コードはnull(string? input)
    {
        Assert.Null(MigrationCode.Normalize(input));
    }

    [Fact]
    public void 移行コードの符号化は16文字80bit()
    {
        var bytes = new byte[10];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 25 + 1);

        var canonical = MigrationCode.Encode(bytes);
        Assert.Equal(16, canonical.Length);
        Assert.Equal(canonical, MigrationCode.Normalize(MigrationCode.FormatForDisplay(canonical)));
    }

    // =====================================================================
    // RememberedPasswordService（保管庫の上のアプリ向け API）
    // =====================================================================

    [Fact]
    public void 記憶パスワードの保存と取得()
    {
        var machine = new FakeKeychain();
        var service = new RememberedPasswordService(keychain: machine, vaultPath: VaultPath);

        Assert.Equal(RememberedPasswordState.None, service.GetState());
        Assert.Null(service.GetPassword(RememberedPasswordService.KeyEncryptPassword));

        Assert.True(service.SetPasswords("enc-pw", "dec-pw"));
        Assert.Equal(RememberedPasswordState.Unlocked, service.GetState());
        Assert.Equal("enc-pw", service.GetPassword(RememberedPasswordService.KeyEncryptPassword));
        Assert.Equal("dec-pw", service.GetPassword(RememberedPasswordService.KeyDecryptPassword));

        // null は「その項目を消す」
        Assert.True(service.SetPasswords("enc-pw2", null));
        Assert.Equal("enc-pw2", service.GetPassword(RememberedPasswordService.KeyEncryptPassword));
        Assert.Null(service.GetPassword(RememberedPasswordService.KeyDecryptPassword));
    }

    [Fact]
    public void 別の端末ではLockedになり_移行コードで登録できる()
    {
        var machineA = new FakeKeychain();
        var serviceA = new RememberedPasswordService(keychain: machineA, vaultPath: VaultPath);
        Assert.True(serviceA.SetPasswords("enc-pw", null));
        var code = serviceA.IssueMigrationCode();
        Assert.NotNull(code);

        var machineB = new FakeKeychain();
        var serviceB = new RememberedPasswordService(keychain: machineB, vaultPath: VaultPath);
        Assert.Equal(RememberedPasswordState.Locked, serviceB.GetState());
        Assert.Null(serviceB.GetPassword(RememberedPasswordService.KeyEncryptPassword));
        Assert.False(serviceB.SetPasswords("x", null)); // Locked には書けない

        Assert.True(serviceB.RegisterThisDeviceWithCode(code!));
        Assert.Equal(RememberedPasswordState.Unlocked, serviceB.GetState());
        Assert.Equal("enc-pw", serviceB.GetPassword(RememberedPasswordService.KeyEncryptPassword));
    }

    [Fact]
    public void すべて消去でファイルと鍵ストアの項目が消える()
    {
        var machine = new FakeKeychain();
        var service = new RememberedPasswordService(keychain: machine, vaultPath: VaultPath);
        Assert.True(service.SetPasswords("enc-pw", "dec-pw"));
        Assert.NotEmpty(machine.Items);

        service.DeleteAll();

        Assert.False(File.Exists(VaultPath));
        Assert.Empty(machine.Items);
        Assert.Equal(RememberedPasswordState.None, service.GetState());
    }

    [Fact]
    public void 保管庫が無い状態で両方消すだけなら保管庫を作らない()
    {
        var service = new RememberedPasswordService(keychain: new FakeKeychain(), vaultPath: VaultPath);
        Assert.True(service.SetPasswords(null, ""));
        Assert.False(File.Exists(VaultPath));
    }

    [Fact]
    public void 最後のdeviceプロテクターは削除できない()
    {
        var machine = new FakeKeychain();
        using var session = SecretVault.CreateNew(VaultPath, machine, "A");
        session.SetSecrets(SampleSecrets);

        var device = Assert.Single(session.ListProtectors(), p => p.Type == "device");
        Assert.Throws<InvalidOperationException>(() => session.RemoveProtector(device.Id));

        // code プロテクターは普通に消せる
        session.IssueMigrationCode();
        var code = Assert.Single(session.ListProtectors(), p => p.Type == "code");
        Assert.True(session.RemoveProtector(code.Id));
        Assert.DoesNotContain(session.ListProtectors(), p => p.Type == "code");
    }

    [Fact]
    public void 新規作成の保存に失敗したら鍵ストアにDPKを残さない()
    {
        var machine = new FakeKeychain();
        // 保存先の親を「ファイル」にして SaveAtomically を失敗させる
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "");
        var badPath = Path.Combine(blocker, SecretVault.FileName);

        Assert.ThrowsAny<Exception>(() => SecretVault.CreateNew(badPath, machine, "A"));
        Assert.Empty(machine.Items);
    }

    [Fact]
    public void 別プロセスが消した保管庫を書き換えで蘇らせない()
    {
        var machine = new FakeKeychain();
        using var session = SecretVault.CreateNew(VaultPath, machine, "A");

        // 別プロセスの DeleteAll 相当
        File.Delete(VaultPath);

        Assert.Throws<InvalidOperationException>(() => session.SetSecrets(SampleSecrets));
        Assert.False(File.Exists(VaultPath));
    }

    [Fact]
    public void 別のDEKに置き換わった保管庫の上には書かない()
    {
        var machineA = new FakeKeychain();
        using var sessionA = SecretVault.CreateNew(VaultPath, machineA, "A");

        // 別プロセスが消して作り直した（DEK が変わっている）
        File.Delete(VaultPath);
        var machineB = new FakeKeychain();
        using (var sessionB = SecretVault.CreateNew(VaultPath, machineB, "B"))
        {
            sessionB.SetSecrets(SampleSecrets);
        }

        // A の古いセッションから書くと、B のプロテクターと食い違った
        // 開けないファイルになるため拒否される
        Assert.Throws<InvalidOperationException>(
            () => sessionA.SetSecrets(new Dictionary<string, string> { ["encryptPassword"] = "x" }));

        // B の保管庫は無傷で開ける
        var reloaded = SecretVault.TryLoad(VaultPath);
        using var check = reloaded!.TryUnlockWithDevice(machineB);
        Assert.NotNull(check);
        Assert.Equal("encrypt-Pass#123", check!.GetSecrets()!["encryptPassword"]);
    }

    [Fact]
    public void 開きっぱなしの古いモデルの保存が_他所で登録された端末を消さない()
    {
        // 端末 A がセッションを開いたまま（RememberedPasswordService の平常運用）、
        // 同じファイルを別のインスタンス（別プロセス・別 PC 相当）が書き換えるシナリオ
        var machineA = new FakeKeychain();
        using var sessionA = SecretVault.CreateNew(VaultPath, machineA, "A");
        var code = sessionA.IssueMigrationCode();

        // 端末 B が移行コードで自分を登録する（別インスタンスでファイルを開く）
        var machineB = new FakeKeychain();
        var vaultB = SecretVault.TryLoad(VaultPath);
        Assert.NotNull(vaultB);
        using (var sessionB = vaultB!.TryUnlockWithCode(
                   code.Replace("-", ""), machineB, registerThisDevice: true, "B"))
        {
            Assert.NotNull(sessionB);
        }

        // A が古いモデルのまま秘密を書き換えても、B の device プロテクターは残る
        sessionA.SetSecrets(SampleSecrets);

        var reloaded = SecretVault.TryLoad(VaultPath);
        using var checkB = reloaded!.TryUnlockWithDevice(machineB);
        Assert.NotNull(checkB);
        Assert.Equal("encrypt-Pass#123",
            checkB!.GetSecrets()![RememberedPasswordService.KeyEncryptPassword]);
    }

    [Fact]
    public void 既にある保管庫の上にCreateNewしても上書きしない()
    {
        var machineA = new FakeKeychain();
        using (var session = SecretVault.CreateNew(VaultPath, machineA, "A"))
        {
            session.SetSecrets(SampleSecrets);
        }

        // 「無いから作る」判断と作成の間に別プロセスが先に作っていたケース。
        // 上書きすると A の秘密とプロテクターが失われるので拒否される
        var machineB = new FakeKeychain();
        Assert.Throws<InvalidOperationException>(
            () => SecretVault.CreateNew(VaultPath, machineB, "B"));

        // A の保管庫は無傷で開ける
        var vault = SecretVault.TryLoad(VaultPath);
        using var check = vault!.TryUnlockWithDevice(machineA);
        Assert.NotNull(check);
        Assert.Equal("encrypt-Pass#123", check!.GetSecrets()!["encryptPassword"]);
    }

    [Fact]
    public void 保管庫のロックは同じパスに対して排他になる()
    {
        using (SecretVault.AcquireExclusiveLock(VaultPath))
        {
            // 別スレッド（別プロセス相当）は、解放されるまで取得できない。
            // Task.Run + ブロック待ちだと、待っているスレッド自身がタスクを
            // インライン実行することがあり（同一スレッド = Mutex 再入で取得
            // できてしまう）、まれに偽陽性になる。必ず別スレッドで確かめる
            var acquired = false;
            var thread = new System.Threading.Thread(() =>
            {
                using var mutex = new System.Threading.Mutex(false,
                    MutexNameFor(VaultPath));
                if (mutex.WaitOne(TimeSpan.FromMilliseconds(200)))
                {
                    acquired = true;
                    mutex.ReleaseMutex();
                }
            });
            thread.Start();
            thread.Join();
            Assert.False(acquired);
        }

        // 解放後は取得できる
        using var relock = SecretVault.AcquireExclusiveLock(VaultPath);
        Assert.NotNull(relock);
    }

    /// <summary>SecretVault.AcquireExclusiveLock と同じ規則で Mutex 名を組む。</summary>
    private static string MutexNameFor(string path)
    {
        var normalized = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            normalized = normalized.ToUpperInvariant();
        }
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(normalized)));
        return "AttacheCase.Vault." + hash;
    }
}
