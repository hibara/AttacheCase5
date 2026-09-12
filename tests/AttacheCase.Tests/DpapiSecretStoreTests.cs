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
// Windows の秘密情報ストア（DPAPI + ファイル）の契約テスト。
// macOS 版 KeychainServiceTests と同じ観点（往復・上書き・削除・分離）を検証する。
//----------------------------------------------------------------------

using System;
using System.IO;
using AttacheCase.Platform;
using Xunit;

namespace AttacheCase.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class DpapiSecretStoreTests : IDisposable
{
    private readonly string _dir;

    public DpapiSecretStoreTests()
    {
        _dir = Directory.CreateTempSubdirectory("atc-dpapi-test").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* 後始末の失敗は無視 */ }
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Windows のみ（DPAPI に依存）";
        }
    }

    [WindowsFact]
    public void 保存した秘密を取り出せる()
    {
        var store = new DpapiSecretStore(_dir);
        var secret = new byte[] { 1, 2, 3, 4, 5 };

        store.Set("svc", "acct", secret);

        Assert.True(store.TryGet("svc", "acct", out var loaded));
        Assert.Equal(secret, loaded);
    }

    [WindowsFact]
    public void 上書きすると新しい値になる()
    {
        var store = new DpapiSecretStore(_dir);
        store.Set("svc", "acct", new byte[] { 1 });
        store.Set("svc", "acct", new byte[] { 2, 3 });

        Assert.True(store.TryGet("svc", "acct", out var loaded));
        Assert.Equal(new byte[] { 2, 3 }, loaded);
    }

    [WindowsFact]
    public void 無い項目はfalseを返す()
    {
        var store = new DpapiSecretStore(_dir);
        Assert.False(store.TryGet("svc", "nothing", out var data));
        Assert.Null(data);
    }

    [WindowsFact]
    public void 削除すると取り出せなくなる()
    {
        var store = new DpapiSecretStore(_dir);
        store.Set("svc", "acct", new byte[] { 9 });

        Assert.True(store.Delete("svc", "acct"));
        Assert.False(store.TryGet("svc", "acct", out _));
        Assert.False(store.Delete("svc", "acct")); // 二度目はfalse（例外にしない）
    }

    [WindowsFact]
    public void 別の項目の_blob_を差し替えても復号できない()
    {
        // DPAPI の optionalEntropy に (service, account) を結び付けているため、
        // ファイルを入れ替えられても他の鍵として開かないことを確かめる
        var store = new DpapiSecretStore(_dir);
        store.Set("svc", "alice", new byte[] { 1, 1, 1 });
        store.Set("svc", "bob", new byte[] { 2, 2, 2 });

        var files = Directory.GetFiles(_dir, "*.bin");
        Assert.Equal(2, files.Length);

        // alice の blob を bob の位置へコピーする（ファイル名 = 項目の識別）
        var a = files[0];
        var b = files[1];
        File.Copy(a, b, overwrite: true);

        // どちらか一方（差し替えられた側）は復号に失敗して「無い」扱いになる
        var aliceOk = store.TryGet("svc", "alice", out _);
        var bobOk = store.TryGet("svc", "bob", out _);
        Assert.True(aliceOk ^ bobOk, "エントロピー結合が効いていれば、差し替えられた側だけ開けない");
    }

    [WindowsFact]
    public void ファイル名から中身を推測できない()
    {
        var store = new DpapiSecretStore(_dir);
        store.Set("org.hibara.AttacheCase.PostPPAP", "rsa-priv-someone@example.com", new byte[] { 1 });

        foreach (var file in Directory.GetFiles(_dir))
        {
            Assert.DoesNotContain("example.com", Path.GetFileName(file));
            Assert.DoesNotContain("rsa", Path.GetFileName(file));
        }
    }

    /// <summary>旧形式のファイル名（区切り文字連結ハッシュ）。実装から独立に計算する。</summary>
    private static string LegacyFileName(string service, string account)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
               System.Text.Encoding.UTF8.GetBytes(service + "\n" + account))) + ".bin";

    [WindowsFact]
    public void 識別子に改行があっても別項目と衝突しない()
    {
        // 旧形式は SHA-256("a\nb" + "\n" + "c") と SHA-256("a" + "\n" + "b\nc") が
        // 同じ名前になり、後の Set が先の項目を上書きしていた
        var store = new DpapiSecretStore(_dir);
        store.Set("a\nb", "c", new byte[] { 1 });
        store.Set("a", "b\nc", new byte[] { 2 });

        Assert.True(store.TryGet("a\nb", "c", out var first));
        Assert.True(store.TryGet("a", "b\nc", out var second));
        Assert.Equal(new byte[] { 1 }, first);
        Assert.Equal(new byte[] { 2 }, second);
    }

    [WindowsFact]
    public void 旧形式のファイル名でも読めて新形式へ改名される()
    {
        // 既存利用者の保存物（旧名）を孤児にしないための読み替え
        var store = new DpapiSecretStore(_dir);
        store.Set("svc", "acct", new byte[] { 7, 8, 9 });

        // 保存されたファイルを旧形式の名前へ付け替えて「旧版の保存物」を再現する
        var current = Assert.Single(Directory.GetFiles(_dir, "*.bin"));
        var legacy = Path.Combine(_dir, LegacyFileName("svc", "acct"));
        File.Move(current, legacy);

        Assert.True(store.TryGet("svc", "acct", out var loaded));
        Assert.Equal(new byte[] { 7, 8, 9 }, loaded);

        // 読めたついでに新名へ改名されている
        Assert.False(File.Exists(legacy));
        Assert.True(store.TryGet("svc", "acct", out _));
    }

    [WindowsFact]
    public void 旧形式のファイルもDeleteで消える()
    {
        var store = new DpapiSecretStore(_dir);
        store.Set("svc", "acct", new byte[] { 1 });
        var current = Assert.Single(Directory.GetFiles(_dir, "*.bin"));
        File.Move(current, Path.Combine(_dir, LegacyFileName("svc", "acct")));

        Assert.True(store.Delete("svc", "acct"));
        Assert.Empty(Directory.GetFiles(_dir, "*.bin"));
        Assert.False(store.TryGet("svc", "acct", out _));
    }
}
