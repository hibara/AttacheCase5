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
// 差し替え先の判定のテスト。
//
// ここを誤ると、更新のつもりで無関係な場所を壊す。「差し替えられる」と
// 判定してよい形が本当にそれだけか、を重点的に確かめる。
//----------------------------------------------------------------------

using System;
using System.IO;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Update")]
public class AppBundleLocatorTests
{
    private const string Installed =
        "/Applications/AttacheCase.app/Contents/MacOS/AttacheCase";

    private static InstallLocation Locate(
        string? exe, bool exists = true, bool writable = true) =>
        AppBundleLocator.Locate(exe, _ => exists, _ => writable);

    // =================================================================
    // バンドルの割り出し
    // =================================================================

    [MacOsFact]
    public void 通常の配置からバンドルを割り出せる()
    {
        Assert.Equal(
            "/Applications/AttacheCase.app",
            AppBundleLocator.FindBundlePath(Installed, _ => true));
    }

    [MacOsFact]
    public void ホーム配下でも割り出せる()
    {
        Assert.Equal(
            "/Users/foo/Applications/AttacheCase.app",
            AppBundleLocator.FindBundlePath(
                "/Users/foo/Applications/AttacheCase.app/Contents/MacOS/AttacheCase", _ => true));
    }

    [MacOsTheory]
    // 開発中の dotnet run。実行ファイルは bin の下に直接ある
    [InlineData("/Users/foo/proj/bin/Debug/net10.0/AttacheCase")]
    // dotnet ホスト経由
    [InlineData("/usr/local/share/dotnet/dotnet")]
    // .app ではあるが階層が違う
    [InlineData("/Applications/AttacheCase.app/Contents/AttacheCase")]
    [InlineData("/Applications/AttacheCase.app/AttacheCase")]
    // MacOS ではあるが .app が無い
    [InlineData("/Applications/AttacheCase/Contents/MacOS/AttacheCase")]
    [InlineData(null)]
    [InlineData("")]
    public void 期待した形でなければ割り出さない(string? exe)
    {
        Assert.Null(AppBundleLocator.FindBundlePath(exe, _ => true));
    }

    [MacOsFact]
    public void バンドルが実在しなければ割り出さない()
    {
        Assert.Null(AppBundleLocator.FindBundlePath(Installed, _ => false));
    }

    // =================================================================
    // 差し替えの可否
    // =================================================================

    [MacOsFact]
    public void 通常の配置なら差し替えられる()
    {
        var location = Locate(Installed);

        Assert.Equal(InstallLocationStatus.Ok, location.Status);
        Assert.True(location.CanReplace);
        Assert.Equal("/Applications/AttacheCase.app", location.BundlePath);
    }

    [MacOsFact]
    public void 開発実行では差し替えない()
    {
        var location = Locate("/usr/local/share/dotnet/dotnet");

        Assert.Equal(InstallLocationStatus.NotInAppBundle, location.Status);
        Assert.False(location.CanReplace);
    }

    [MacOsFact]
    public void AppTranslocation配下では差し替えない()
    {
        // 隔離属性の付いたアプリを Finder から起動すると、macOS が読み取り専用の
        // 場所へ複製して実行する。ここを差し替えても本体には反映されない
        var location = Locate(
            "/private/var/folders/p5/x/T/AppTranslocation/ABC-123/d/AttacheCase.app/Contents/MacOS/AttacheCase");

        Assert.Equal(InstallLocationStatus.Translocated, location.Status);
        Assert.False(location.CanReplace);
    }

    [MacOsFact]
    public void マウント先から起動していたら差し替えない()
    {
        // DMG を開いてそのまま起動している場合
        var location = Locate("/Volumes/AttacheCase/AttacheCase.app/Contents/MacOS/AttacheCase");

        Assert.Equal(InstallLocationStatus.ReadOnlyLocation, location.Status);
        Assert.False(location.CanReplace);
    }

    [MacOsFact]
    public void 親ディレクトリに書けなければ差し替えない()
    {
        // 管理者でない利用者の /Applications など。権限昇格は求めない
        var location = Locate(Installed, writable: false);

        Assert.Equal(InstallLocationStatus.NotWritable, location.Status);
        Assert.False(location.CanReplace);
        Assert.Equal("/Applications/AttacheCase.app", location.BundlePath);
    }

    [MacOsFact]
    public void 判定の順序は移送と読み取り専用が権限より先()
    {
        // 書けないことより「そこを直しても意味が無い」ほうが先に分かるべき。
        // 案内する文言が変わるため
        var translocated = AppBundleLocator.Locate(
            "/private/var/folders/x/AppTranslocation/A/d/AttacheCase.app/Contents/MacOS/AttacheCase",
            _ => true,
            _ => false);

        Assert.Equal(InstallLocationStatus.Translocated, translocated.Status);
    }

    // =================================================================
    // 書き込み可否の実測
    // =================================================================

    [MacOsFact]
    public void 書ける場所は書けると判定する()
    {
        var directory = Directory.CreateTempSubdirectory("atc-writable").FullName;
        try
        {
            Assert.True(AppBundleLocator.IsWritable(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [MacOsFact]
    public void 存在しない場所は書けないと判定する()
    {
        Assert.False(AppBundleLocator.IsWritable("/no/such/directory/" + Guid.NewGuid()));
    }

    [MacOsFact]
    public void 書けない場所は書けないと判定する()
    {
        // /usr のような実在の保護領域に頼ると、環境によって結果が変わる。
        // 自分で書き込み権限を落としたディレクトリで確かめる
        var directory = Directory.CreateTempSubdirectory("atc-readonly").FullName;
        try
        {
            TestFile.MakeReadOnly(directory);

            Assert.False(AppBundleLocator.IsWritable(directory));
        }
        finally
        {
            TestFile.MakeWritable(directory);
            Directory.Delete(directory, true);
        }
    }

    [MacOsFact]
    public void 書き込み確認の痕跡を残さない()
    {
        var directory = Directory.CreateTempSubdirectory("atc-writable").FullName;
        try
        {
            AppBundleLocator.IsWritable(directory);

            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
