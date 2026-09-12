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
// ポータブル設定（_AtcCase.json）と信頼確認（ハッシュ承認）のテスト。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Text.Json;
using AttacheCase.Models;
using AttacheCase.Settings;
using Xunit;

namespace AttacheCase.Tests;

public class PortableSettingsTests : IDisposable
{
    private readonly string _dir;

    public PortableSettingsTests()
    {
        _dir = Directory.CreateTempSubdirectory("atc-portable-test").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* 後始末の失敗は無視 */ }
    }

    private string WritePortableFile(AppSettings settings)
    {
        var path = Path.Combine(_dir, PortableSettings.FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(settings));
        return path;
    }

    [Fact]
    public void 見つけて読み込める()
    {
        var path = WritePortableFile(new AppSettings { Language = "en", MissTypeLimits = 7 });

        Assert.Equal(path, PortableSettings.Find(_dir));
        var loaded = PortableSettings.Load(path);
        Assert.NotNull(loaded);
        Assert.Equal("en", loaded!.Language);
        Assert.Equal(7, loaded.MissTypeLimits);
    }

    [Fact]
    public void 無ければnull()
    {
        Assert.Null(PortableSettings.Find(_dir));
    }

    [Fact]
    public void 壊れたファイルはnull()
    {
        var path = Path.Combine(_dir, PortableSettings.FileName);
        File.WriteAllText(path, "{ broken json");
        Assert.Null(PortableSettings.Load(path));
    }

    [Fact]
    public void 承認していないファイルは信頼されない()
    {
        var path = WritePortableFile(new AppSettings());
        var guard = new PortableSettingsGuard(Path.Combine(_dir, "trust.json"));

        Assert.False(guard.IsApproved(path));
    }

    [Fact]
    public void 承認すると信頼される()
    {
        var path = WritePortableFile(new AppSettings());
        var guard = new PortableSettingsGuard(Path.Combine(_dir, "trust.json"));

        guard.Approve(path);
        Assert.True(guard.IsApproved(path));
    }

    [Fact]
    public void 内容が変わると未承認に戻る()
    {
        var path = WritePortableFile(new AppSettings());
        var guard = new PortableSettingsGuard(Path.Combine(_dir, "trust.json"));
        guard.Approve(path);

        // 承認後にファイルが差し替えられた（攻撃者が保存先をすり替えた等）
        WritePortableFile(new AppSettings { SaveToFolderPath = @"\\evil\share" });

        Assert.False(guard.IsApproved(path));
    }

    [Fact]
    public void LoadWithHashは1回の読み込みで内容とハッシュを返す()
    {
        var path = WritePortableFile(new AppSettings { Language = "en" });

        var loaded = PortableSettings.LoadWithHash(path);
        Assert.NotNull(loaded);
        Assert.Equal("en", loaded!.Value.Settings.Language);
        Assert.Equal(PortableSettings.ComputeHash(path), loaded.Value.Hash);

        // 壊れたファイルは null
        File.WriteAllText(path, "{ broken json");
        Assert.Null(PortableSettings.LoadWithHash(path));
    }

    [Fact]
    public void ハッシュでの承認照合はファイルを読み直さない()
    {
        var path = WritePortableFile(new AppSettings());
        var guard = new PortableSettingsGuard(Path.Combine(_dir, "trust.json"));
        guard.Approve(path);

        var loaded = PortableSettings.LoadWithHash(path)!;

        // 読み込み後にファイルが差し替えられても（TOCTOU）、読み込んだバイト列の
        // ハッシュで照合するため、差し替え前の内容として正しく承認される。
        // 差し替え後の内容を改めて読むと未承認になる
        WritePortableFile(new AppSettings { SaveToFolderPath = @"\\evil\share" });

        Assert.True(guard.IsApprovedHash(loaded.Value.Hash));
        Assert.False(guard.IsApprovedHash(PortableSettings.ComputeHash(path)));
    }

    [Fact]
    public void 承認の取り消し()
    {
        var path = WritePortableFile(new AppSettings());
        var guard = new PortableSettingsGuard(Path.Combine(_dir, "trust.json"));
        guard.Approve(path);
        guard.Revoke();

        Assert.False(guard.IsApproved(path));
    }
}
