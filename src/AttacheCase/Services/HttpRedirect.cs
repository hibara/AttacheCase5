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
// リダイレクトの扱い。
//
// HttpClient の自動追従は使わない（http へ降格する経路をこちらで止められないため）。
// 代わりに、https のままの移動先へ 1 回だけ自分で追従する。
//
// 追従して差し支えないのは、受け取る中身を別に検証しているから。
// マニフェストは Ed25519、配布物は SHA-256 と codesign で確かめる。どこから
// 届いたかにかかわらず、偽物は弾かれる。
//
// 追従しないと、サーバー側に末尾スラッシュや www の正規化が入っただけで
// 更新が黙って届かなくなる。セキュリティ上の修正を配れなくなるほうが危ない。
//----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AttacheCase.Services;

internal static class HttpRedirect
{
    /// <summary>
    /// 要求を送り、https の移動先が返ってきたら 1 回だけ追従する。
    /// </summary>
    /// <param name="http">送信に使うクライアント。自動追従は無効にしておくこと。</param>
    /// <param name="createRequest">
    /// 要求を組み立てる。追従のたびに新しい要求が要るため、使い回せる形で渡す。
    /// </param>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        Func<Uri, HttpRequestMessage> createRequest,
        Uri uri,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        var response = await http
            .SendAsync(createRequest(uri), completionOption, cancellationToken)
            .ConfigureAwait(false);

        if (!IsRedirect(response.StatusCode)) return response;

        // Dispose 後に StatusCode を読まないよう、先に控える（Windows 版で直した問題の移植）
        var firstStatus = response.StatusCode;
        var location = response.Headers.Location;
        response.Dispose();

        if (location is null)
        {
            throw new InvalidOperationException(
                $"配信先が移動先を示さずにリダイレクトしました（{(int)firstStatus}）: {uri}");
        }

        // 相対指定も受け付ける。基準は元の URL
        var target = location.IsAbsoluteUri ? location : new Uri(uri, location);

        if (target.Scheme != Uri.UriSchemeHttps)
        {
            // http へ降格させられたら、そこで止める
            throw new InvalidOperationException(
                $"リダイレクト先が https ではありません: {target.Scheme}://…");
        }

        var redirected = await http
            .SendAsync(createRequest(target), completionOption, cancellationToken)
            .ConfigureAwait(false);

        if (IsRedirect(redirected.StatusCode))
        {
            // 2 回目以降は追わない。堂々巡りに付き合わない
            var status = redirected.StatusCode;
            redirected.Dispose();
            throw new InvalidOperationException(
                $"リダイレクトが続いています（{(int)status}）: {target}");
        }

        return redirected;
    }

    internal static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently      // 301
            or HttpStatusCode.Found                    // 302
            or HttpStatusCode.SeeOther                 // 303
            or HttpStatusCode.TemporaryRedirect        // 307
            or HttpStatusCode.PermanentRedirect;       // 308
}
