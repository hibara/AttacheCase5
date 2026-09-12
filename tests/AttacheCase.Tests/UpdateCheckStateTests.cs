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
// 更新確認の間隔（1 日 1 回）のテスト。
//
// 間隔を空けること自体より、「空けすぎて更新を受け取れなくなる」ほうが危ない。
// 時計が狂っている場合に確認が止まらないことを重点的に確かめる。
//----------------------------------------------------------------------

using System;
using System.IO;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Update")]
public class UpdateCheckStateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 12, 0, 0, TimeSpan.FromHours(9));

    // =================================================================
    // 間隔の判定
    // =================================================================

    [Fact]
    public void 一度も確認していなければ確認する()
    {
        Assert.True(UpdateCheckStateStore.ShouldCheck(null, Now));
    }

    [Fact]
    public void 丸一日経っていれば確認する()
    {
        Assert.True(UpdateCheckStateStore.ShouldCheck(Now.AddDays(-1), Now));
    }

    [Fact]
    public void 丸一日を少しでも超えていれば確認する()
    {
        Assert.True(UpdateCheckStateStore.ShouldCheck(Now.AddDays(-1).AddSeconds(-1), Now));
    }

    [Theory]
    [InlineData(-23)]
    [InlineData(-1)]
    [InlineData(0)]
    public void 丸一日経っていなければ確認しない(int hoursAgo)
    {
        Assert.False(UpdateCheckStateStore.ShouldCheck(Now.AddHours(hoursAgo), Now));
    }

    [Fact]
    public void 記録が未来を指していても確認する()
    {
        // 時計が狂っていたり巻き戻されたりしたときに、更新を永久に受け取れなく
        // なるのを避ける。確認しすぎるほうが、できなくなるより安全
        Assert.True(UpdateCheckStateStore.ShouldCheck(Now.AddYears(10), Now));
    }

    [Fact]
    public void 時差が違っても同じ瞬間なら確認しない()
    {
        var sameInstantElsewhere = Now.ToOffset(TimeSpan.FromHours(-8));

        Assert.False(UpdateCheckStateStore.ShouldCheck(sameInstantElsewhere, Now));
    }

    // =================================================================
    // 記録の読み書き
    // =================================================================

    private static string NewStatePath() =>
        Path.Combine(Directory.CreateTempSubdirectory("atc-update").FullName, "update_check.json");

    [Fact]
    public void 記録が無ければ確認する()
    {
        var store = new UpdateCheckStateStore(NewStatePath());

        Assert.True(store.ShouldCheck(Now));
    }

    [Fact]
    public void 記録したあとは間隔が空くまで確認しない()
    {
        var store = new UpdateCheckStateStore(NewStatePath());
        store.MarkChecked(Now);

        Assert.False(store.ShouldCheck(Now.AddHours(1)));
        Assert.True(store.ShouldCheck(Now.AddHours(25)));
    }

    [Fact]
    public void 記録は別インスタンスからも読める()
    {
        var path = NewStatePath();
        new UpdateCheckStateStore(path).MarkChecked(Now);

        Assert.False(new UpdateCheckStateStore(path).ShouldCheck(Now.AddHours(1)));
    }

    [Fact]
    public void 記録が壊れていたら確認する()
    {
        // 読めないせいで更新が止まるのが、いちばん避けたい壊れ方
        var path = NewStatePath();
        File.WriteAllText(path, "これは JSON ではない");

        Assert.True(new UpdateCheckStateStore(path).ShouldCheck(Now));
    }

    [Fact]
    public void 書き込めなくても例外にしない()
    {
        // 記録できないと毎回確認することになるが、それ自体は害がない
        var directory = Directory.CreateTempSubdirectory("atc-update").FullName;
        var path = Path.Combine(directory, "update_check.json");
        File.WriteAllText(path, "{}");
        var info = new FileInfo(path) { IsReadOnly = true };
        try
        {
            new UpdateCheckStateStore(path).MarkChecked(Now);
        }
        finally
        {
            info.IsReadOnly = false;
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void 保存先は設定ファイルとは別にする()
    {
        // 設定画面を開いたまま背後で保存が走って、互いの変更を潰し合わないように
        var store = new UpdateCheckStateStore();

        Assert.EndsWith("update_check.json", store.FilePath);
        Assert.DoesNotContain("settings.json", store.FilePath);
    }

    [Fact]
    public void 間隔は1日である()
    {
        Assert.Equal(TimeSpan.FromDays(1), UpdateCheckStateStore.CheckInterval);
    }
}
