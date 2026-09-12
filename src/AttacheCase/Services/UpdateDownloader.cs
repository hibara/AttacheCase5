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
// 更新 DMG のダウンロード。
//
// マニフェストは Ed25519 で検証済みなので、そこに書かれたサイズと SHA-256 は
// 信用してよい。落としたものがそれと一致するかを必ず確かめる。
//
// ただしハッシュが合っただけでは「配信者が意図したもの」でしかない。
// 中身の .app が本物かどうかは UpdatePackageVerifier で別途確かめること。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;

namespace AttacheCase.Services;

/// <summary>ダウンロードの進み具合。</summary>
/// <param name="BytesReceived">受け取った量。</param>
/// <param name="TotalBytes">全体の量。</param>
public readonly record struct DownloadProgress(long BytesReceived, long TotalBytes)
{
    /// <summary>0.0〜1.0。全体量が不明なら 0。</summary>
    public double Ratio => TotalBytes > 0 ? (double)BytesReceived / TotalBytes : 0;
}

public class UpdateDownloader
{
    /// <summary>
    /// 受け入れる最大のサイズ。DMG は 70MB 程度なので、桁違いのものは
    /// マニフェストが正しくても受け取らない。
    /// </summary>
    private const long MaxDownloadBytes = 500L * 1024 * 1024;

    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = false,
    })
    {
        // 本体のダウンロードは時間がかかる。ヘッダーだけ先に受けて、
        // 本文は読みながら進めるので、全体の制限時間は長めでよい
        Timeout = TimeSpan.FromMinutes(30),
    };

    private readonly HttpClient _http;

    public UpdateDownloader(HttpClient? http = null)
    {
        _http = http ?? SharedHttp;
    }

    /// <summary>
    /// 配布物を一時ディレクトリへ落とし、サイズと SHA-256 を確かめる。
    /// 一致しなければ落としたものを消して例外を投げる。
    /// </summary>
    /// <param name="build">検証済みマニフェストに書かれていた配布物。</param>
    /// <param name="destinationDirectory">保存先。呼び出し側が後始末する。</param>
    /// <param name="progress">進み具合の通知。</param>
    /// <param name="expectedExtension">
    /// 保存する配布物の拡張子（"." 付き）。既定は Mac の DMG。Windows のインストーラは
    /// ".exe" を渡す。URL の名前がこの拡張子で終わらなければ "update" + 拡張子にする。
    /// 保存名は、そのまま ShellExecute で起動する側（Windows）が見るので、
    /// ここで拡張子を誤ると「どのアプリで開きますか」になる（2026-09-12 実測）。
    /// </param>
    /// <returns>保存した配布物のパス。</returns>
    public async Task<string> DownloadAsync(
        UpdateBuild build,
        string destinationDirectory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string expectedExtension = ".dmg")
    {
        ArgumentNullException.ThrowIfNull(build);

        if (string.IsNullOrEmpty(expectedExtension) || expectedExtension[0] != '.' ||
            expectedExtension.Length < 2 ||
            expectedExtension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("拡張子は \".\" で始まる名前にしてください。", nameof(expectedExtension));
        }

        if (!Uri.TryCreate(build.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"配布物の URL が https ではありません: {build.Url}");
        }

        if (build.Size <= 0 || build.Size > MaxDownloadBytes)
        {
            throw new InvalidOperationException($"配布物のサイズが妥当ではありません: {build.Size}");
        }

        Directory.CreateDirectory(destinationDirectory);

        // マニフェストの URL から名前を借りるが、パス要素は捨てる。
        // 配信側が細工した名前で保存先の外へ書き出させないため
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            fileName = "update" + expectedExtension;
        }

        var destination = Path.Combine(destinationDirectory, fileName);

        try
        {
            var actualHash = await DownloadToFileAsync(
                uri, destination, build.Size, progress, cancellationToken).ConfigureAwait(false);

            // 以下 3 つは「配信側のファイルがマニフェストと合っていない」であって、
            // 通信の失敗でも改竄の証拠でもない。利用者に伝えるべきことが違うので
            // 型で分ける（UpdateContentMismatchException のコメントを参照）
            var actualSize = new FileInfo(destination).Length;
            if (actualSize != build.Size)
            {
                throw new UpdateContentMismatchException(
                    $"サイズが一致しません（期待: {build.Size} / 実際: {actualSize}）。");
            }

            if (!string.Equals(actualHash, build.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateContentMismatchException(
                    $"SHA-256 が一致しません（期待: {build.Sha256} / 実際: {actualHash}）。");
            }

            return destination;
        }
        catch (Exception)
        {
            // 検証を通らなかったものを残さない。あとで取り違えて使わないため
            TryDelete(destination);
            throw;
        }
    }

    /// <summary>
    /// 本文を書き出しながら SHA-256 を計算する。二度読みしないで済ませる。
    /// </summary>
    private async Task<string> DownloadToFileAsync(
        Uri uri,
        string destination,
        long expectedSize,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 中身は SHA-256 と codesign で確かめるので、どこから届いても偽物は弾ける。
        // https のままの移動先へ 1 回だけ追従する
        using var response = await HttpRedirect.SendAsync(
            _http,
            target =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, target);
                request.Headers.TryAddWithoutValidation("User-Agent", UpdateService.UserAgent);
                return request;
            },
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var declared = response.Content.Headers.ContentLength;
        if (declared is > 0 && declared != expectedSize)
        {
            throw new UpdateContentMismatchException(
                $"配信された大きさが違います（期待: {expectedSize} / 申告: {declared}）。");
        }

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var file = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        using var sha256 = SHA256.Create();

        var buffer = new byte[81920];
        long received = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            // Content-Length を信用せず、実際に受け取った量でも打ち切る
            received += read;
            if (received > expectedSize)
            {
                throw new UpdateContentMismatchException(
                    $"配信された本文が申告より大きくなりました（申告: {expectedSize}）。");
            }

            sha256.TransformBlock(buffer, 0, read, null, 0);
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            progress?.Report(new DownloadProgress(received, expectedSize));
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // 消せなくても、返す結果は変わらない
        }
    }
}
