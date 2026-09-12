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
// 変更点（リリースノート）の受け取りと選び方のテスト。
//
// 更新の確認画面は、更新経路のうち唯一「外から来た内容」を描く場所。
// 受け取る形が想定どおりであること、無い場合に壊れないことを確かめる。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AttacheCase.Models;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Update")]
public class ReleaseNotesTests
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private static UpdateManifest Parse(string json) =>
        JsonSerializer.Deserialize<UpdateManifest>(json, JsonOpts)!;

    // =================================================================
    // 受け取り
    // =================================================================

    [Fact]
    public void 変更点を言語ごとに受け取れる()
    {
        var manifest = Parse("""
            {
              "version": "5.0.2",
              "releaseNotes": {
                "ja": [
                  { "kind": "new", "items": ["あたらしいもの"] },
                  { "kind": "fixes", "items": ["なおしたもの", "もうひとつ"] }
                ],
                "en": [
                  { "kind": "new", "items": ["something new"] }
                ]
              }
            }
            """);

        var ja = manifest.NotesFor("ja");
        Assert.Equal(2, ja.Count);
        Assert.Equal("new", ja[0].Kind);
        Assert.Equal(new[] { "なおしたもの", "もうひとつ" }, ja[1].Items);

        Assert.Single(manifest.NotesFor("en"));
    }

    [Fact]
    public void 指定の言語が無ければ英語に落とす()
    {
        var manifest = Parse("""
            {"releaseNotes": {"en": [{"kind":"new","items":["only english"]}]}}
            """);

        Assert.Equal("only english", manifest.NotesFor("ja").Single().Items.Single());
    }

    [Fact]
    public void 英語も無ければ入っているものを使う()
    {
        // 何も出さないより、読めるものを出したほうがよい
        var manifest = Parse("""
            {"releaseNotes": {"fr": [{"kind":"new","items":["nouveau"]}]}}
            """);

        Assert.Equal("nouveau", manifest.NotesFor("ja").Single().Items.Single());
    }

    [Theory]
    [InlineData("""{"version":"5.0.2"}""")]
    [InlineData("""{"releaseNotes":{}}""")]
    [InlineData("""{"releaseNotes":null}""")]
    public void 変更点が無くても壊れない(string json)
    {
        // 変更点を用意し忘れた版でも、更新そのものは案内できるべき
        Assert.Empty(Parse(json).NotesFor("ja"));
    }

    // =================================================================
    // 見出しの語
    // =================================================================

    [Theory]
    [InlineData("new")]
    [InlineData("improvements")]
    [InlineData("fixes")]
    [InlineData("NEW")]
    public void 既知の見出しは訳される(string kind)
    {
        var label = Views.UpdateAvailableWindow.LabelFor(kind, LocalizationService.Instance);

        Assert.False(string.IsNullOrWhiteSpace(label));
        // キーがそのまま出ていないこと
        Assert.NotEqual(kind, label);
    }

    [Fact]
    public void 知らない見出しはそのまま出す()
    {
        // 配信側が新しい種類を足しても、表示が空にならないように
        Assert.Equal(
            "security",
            Views.UpdateAvailableWindow.LabelFor("security", LocalizationService.Instance));
    }

    // =================================================================
    // スキップの記録
    // =================================================================

    private static UpdateCheckStateStore NewStore() =>
        new(System.IO.Path.Combine(
            System.IO.Directory.CreateTempSubdirectory("atc-skip").FullName, "update_check.json"));

    [Fact]
    public void スキップした版を覚えている()
    {
        var store = NewStore();
        store.MarkSkipped("5.0.2");

        Assert.True(store.IsSkipped("5.0.2"));
    }

    [Fact]
    public void より新しい版はスキップ対象にならない()
    {
        // 飛ばした版より新しいものが出れば、また知らせる
        var store = NewStore();
        store.MarkSkipped("5.0.2");

        Assert.False(store.IsSkipped("5.0.3"));
    }

    [Fact]
    public void 何もスキップしていなければ対象外()
    {
        Assert.False(NewStore().IsSkipped("5.0.2"));
    }

    [Fact]
    public void スキップの記録は確認日時を消さない()
    {
        // 別々の目的の記録が、互いを潰さないこと
        var store = NewStore();
        var now = DateTimeOffset.Now;

        store.MarkChecked(now);
        store.MarkSkipped("5.0.2");

        Assert.True(store.IsSkipped("5.0.2"));
        Assert.False(store.ShouldCheck(now.AddHours(1)));   // 確認日時が残っている
    }

    [Fact]
    public void 確認日時の記録はスキップを消さない()
    {
        var store = NewStore();

        store.MarkSkipped("5.0.2");
        store.MarkChecked(DateTimeOffset.Now);

        Assert.True(store.IsSkipped("5.0.2"));
    }
}
