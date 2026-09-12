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
// 更新 DMG のダウンロードのテスト。
//
// マニフェストは署名検証済みなので、そこに書かれたサイズと SHA-256 は信用してよい。
// 「合わないものを受け取ったときに、確実に捨てるか」を重点的に確かめる。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Update")]
public class UpdateDownloaderTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("これは DMG のつもりの中身");

    private static string Sha256Of(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>決められた本文を返す実行系。実際の通信はしない。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        private readonly long? _declaredLength;
        private readonly HttpStatusCode _status;

        public StubHandler(byte[] body, long? declaredLength = null,
            HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _declaredLength = declaredLength;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(_body);
            if (_declaredLength is not null)
            {
                content.Headers.ContentLength = _declaredLength;
            }
            return Task.FromResult(new HttpResponseMessage(_status) { Content = content });
        }
    }

    private static UpdateBuild BuildFor(byte[] body, string url = "https://example.test/AtcMac.dmg") =>
        new() { Url = url, Size = body.Length, Sha256 = Sha256Of(body) };

    private static (UpdateDownloader Downloader, string Directory) NewDownloader(
        byte[] body, long? declaredLength = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var http = new HttpClient(new StubHandler(body, declaredLength, status));
        var directory = Directory.CreateTempSubdirectory("atc-download").FullName;
        return (new UpdateDownloader(http), directory);
    }

    // =================================================================
    // 正常な場合
    // =================================================================

    [Fact]
    public async Task 正しい配布物を保存できる()
    {
        var (downloader, directory) = NewDownloader(Payload);
        try
        {
            var path = await downloader.DownloadAsync(BuildFor(Payload), directory);

            Assert.True(File.Exists(path));
            Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
            Assert.EndsWith("AtcMac.dmg", path);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// その場で呼ばれる通知先。Progress&lt;T&gt; は別スレッドへ投げるため、
    /// 待ち合わせが要るうえ、検証中に届いた分で結果が変わる。
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        public System.Collections.Generic.List<T> Reports { get; } = new();

        public void Report(T value) => Reports.Add(value);
    }

    [Fact]
    public async Task 進み具合を通知する()
    {
        var (downloader, directory) = NewDownloader(Payload);
        try
        {
            var progress = new SynchronousProgress<DownloadProgress>();
            await downloader.DownloadAsync(BuildFor(Payload), directory, progress);

            Assert.NotEmpty(progress.Reports);
            Assert.Equal(Payload.Length, progress.Reports[^1].BytesReceived);
            Assert.Equal(1.0, progress.Reports[^1].Ratio, 3);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // =================================================================
    // 受け取ってはいけない場合
    // =================================================================

    [Fact]
    public async Task ハッシュが違えば拒否して消す()
    {
        var (downloader, directory) = NewDownloader(Payload);
        try
        {
            var build = BuildFor(Payload);
            build.Sha256 = new string('0', 64);

            // 配信側の取り違えとして扱う。利用者に伝えるべきことが
            // 通信の失敗とは違うので、型で分けている
            await Assert.ThrowsAsync<UpdateContentMismatchException>(
                () => downloader.DownloadAsync(build, directory));

            // 検証を通らなかったものを残さない。あとで取り違えて使わないため
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 申告より大きい本文は途中で打ち切る()
    {
        // Content-Length を信用せず、実際に読んだ量でも止める
        var (downloader, directory) = NewDownloader(Payload, declaredLength: Payload.Length);
        try
        {
            var build = BuildFor(Payload);
            build.Size = Payload.Length - 3;   // 申告を小さく偽る

            await Assert.ThrowsAsync<UpdateContentMismatchException>(
                () => downloader.DownloadAsync(build, directory));

            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 申告された大きさが違えば受け取らない()
    {
        var (downloader, directory) = NewDownloader(Payload, declaredLength: 999999);
        try
        {
            await Assert.ThrowsAsync<UpdateContentMismatchException>(
                () => downloader.DownloadAsync(BuildFor(Payload), directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("http://example.test/x.dmg")]
    [InlineData("file:///tmp/x.dmg")]
    [InlineData("これは URL ではない")]
    public async Task httpsでなければ通信せずに拒否する(string url)
    {
        var (downloader, directory) = NewDownloader(Payload);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => downloader.DownloadAsync(BuildFor(Payload, url), directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(600L * 1024 * 1024)]   // 上限（500MB）超え
    public async Task サイズが妥当でなければ通信せずに拒否する(long size)
    {
        var (downloader, directory) = NewDownloader(Payload);
        try
        {
            var build = BuildFor(Payload);
            build.Size = size;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => downloader.DownloadAsync(build, directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 応答が失敗なら例外にする()
    {
        var (downloader, directory) = NewDownloader(Payload, status: HttpStatusCode.NotFound);
        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => downloader.DownloadAsync(BuildFor(Payload), directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // =================================================================
    // 保存先の扱い
    // =================================================================

    [Fact]
    public async Task 配信側の細工した名前で保存先の外へ書かせない()
    {
        // URL に ../ を含む名前を仕込まれても、保存先の中に収める
        var (downloader, directory) = NewDownloader(Payload);
        try
        {
            var build = BuildFor(Payload, "https://example.test/a/b/../../../evil.dmg");
            var path = await downloader.DownloadAsync(build, directory);

            Assert.Equal(
                Path.GetFullPath(directory),
                Path.GetFullPath(Path.GetDirectoryName(path)!));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 拡張子を指定すればWindowsのインストーラ名を保つ()
    {
        // Windows は .exe を ShellExecute で起動するので、.dmg にされては困る
        var (downloader, directory) = NewDownloader(Payload);
        try
        {
            var build = BuildFor(Payload, "https://example.test/5500b/atcs5500b.exe");
            var path = await downloader.DownloadAsync(build, directory, expectedExtension: ".exe");

            Assert.Equal("atcs5500b.exe", Path.GetFileName(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 指定した拡張子と合わない名前は既定の名前にする()
    {
        var (downloader, directory) = NewDownloader(Payload);
        try
        {
            var build = BuildFor(Payload, "https://example.test/download?id=1");
            var path = await downloader.DownloadAsync(build, directory, expectedExtension: ".exe");

            Assert.Equal("update.exe", Path.GetFileName(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("exe")]
    [InlineData(".")]
    [InlineData(".e/x")]
    public async Task 拡張子の指定が不正なら断る(string extension)
    {
        var (downloader, directory) = NewDownloader(Payload);
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => downloader.DownloadAsync(BuildFor(Payload), directory, expectedExtension: extension));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task dmgでない名前は既定の名前にする()
    {
        var (downloader, directory) = NewDownloader(Payload);
        try
        {
            var build = BuildFor(Payload, "https://example.test/download?id=1");
            var path = await downloader.DownloadAsync(build, directory);

            Assert.Equal("update.dmg", Path.GetFileName(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
