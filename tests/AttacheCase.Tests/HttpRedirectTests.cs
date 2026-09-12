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
// リダイレクトの扱いのテスト。
//
// 追従して差し支えないのは、受け取る中身を別に検証しているから
// （マニフェストは Ed25519、配布物は SHA-256 と codesign）。
// ただし https から降りることは許さない。そこだけを重点的に確かめる。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Update")]
public class HttpRedirectTests
{
    /// <summary>あらかじめ決めた応答を順に返す。要求先を記録する。</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public ScriptedHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<string> RequestedUrls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            return Task.FromResult(
                _responses.Count > 0
                    ? _responses.Dequeue()
                    : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static HttpResponseMessage Redirect(HttpStatusCode status, string? location)
    {
        var response = new HttpResponseMessage(status);
        if (location is not null)
        {
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        }
        return response;
    }

    private static Task<HttpResponseMessage> Send(ScriptedHandler handler, string url) =>
        HttpRedirect.SendAsync(
            new HttpClient(handler),
            target => new HttpRequestMessage(HttpMethod.Get, target),
            new Uri(url),
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None);

    // =================================================================
    // 追従してよい場合
    // =================================================================

    [Fact]
    public async Task リダイレクトが無ければそのまま返す()
    {
        var handler = new ScriptedHandler(new HttpResponseMessage(HttpStatusCode.OK));

        var response = await Send(handler, "https://example.test/appcast.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(handler.RequestedUrls);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task httpsの移動先へは一度だけ追従する(HttpStatusCode status)
    {
        var handler = new ScriptedHandler(
            Redirect(status, "https://example.test/new/appcast.json"),
            new HttpResponseMessage(HttpStatusCode.OK));

        var response = await Send(handler, "https://example.test/appcast.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new[]
            {
                "https://example.test/appcast.json",
                "https://example.test/new/appcast.json",
            },
            handler.RequestedUrls);
    }

    [Fact]
    public async Task 相対指定の移動先も解決する()
    {
        var handler = new ScriptedHandler(
            Redirect(HttpStatusCode.Found, "/macos/appcast.json"),
            new HttpResponseMessage(HttpStatusCode.OK));

        await Send(handler, "https://example.test/old/appcast.json");

        Assert.Equal("https://example.test/macos/appcast.json", handler.RequestedUrls[1]);
    }

    [Fact]
    public async Task 別のホストへの移動にも追従する()
    {
        // 中身は署名とハッシュで確かめるので、どこから届いても偽物は弾ける。
        // CDN へ移したときに更新が止まるほうが困る
        var handler = new ScriptedHandler(
            Redirect(HttpStatusCode.Found, "https://cdn.example.test/appcast.json"),
            new HttpResponseMessage(HttpStatusCode.OK));

        var response = await Send(handler, "https://example.test/appcast.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =================================================================
    // 追従してはいけない場合
    // =================================================================

    [Fact]
    public async Task httpへ降格させる移動先には追従しない()
    {
        var handler = new ScriptedHandler(
            Redirect(HttpStatusCode.Found, "http://example.test/appcast.json"),
            new HttpResponseMessage(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Send(handler, "https://example.test/appcast.json"));

        Assert.Contains("https ではありません", ex.Message);
        Assert.Single(handler.RequestedUrls);   // 2 回目は投げていない
    }

    [Fact]
    public async Task 移動先が示されなければ失敗させる()
    {
        var handler = new ScriptedHandler(
            Redirect(HttpStatusCode.Found, location: null));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Send(handler, "https://example.test/appcast.json"));

        Assert.Contains("移動先を示さず", ex.Message);
    }

    [Fact]
    public async Task 二段以上のリダイレクトは追わない()
    {
        // 堂々巡りに付き合わない
        var handler = new ScriptedHandler(
            Redirect(HttpStatusCode.Found, "https://example.test/a"),
            Redirect(HttpStatusCode.Found, "https://example.test/b"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Send(handler, "https://example.test/appcast.json"));

        Assert.Contains("リダイレクトが続いています", ex.Message);
        Assert.Equal(2, handler.RequestedUrls.Count);
    }

    // =================================================================
    // 判定
    // =================================================================

    [Theory]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.NotModified, false)]   // 304 は移動ではない
    [InlineData(HttpStatusCode.MovedPermanently, true)]
    [InlineData(HttpStatusCode.PermanentRedirect, true)]
    public void リダイレクトかどうかを見分ける(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, HttpRedirect.IsRedirect(status));
    }
}
