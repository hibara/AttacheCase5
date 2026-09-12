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
// 配信されている配布物が、マニフェストの記載と食い違っているときの例外。
//
// 通信できない・署名が違うといった他の失敗とは、利用者に伝えるべきことが違う。
// これは配信側の取り違えであって、利用者のパソコンには何も起きていない。
// 待っても直らないことがあるので「あとでもう一度」だけでは足りず、
// サイトから入れる道も示す必要がある。
//
// 実際に、同じ名前の別ビルドをサーバーに上げてしまい、これを踏んだことがある。
//----------------------------------------------------------------------

using System;

namespace AttacheCase.Services;

/// <summary>
/// 落とした配布物が、検証済みマニフェストのサイズ・SHA-256 と一致しない。
/// </summary>
public sealed class UpdateContentMismatchException : Exception
{
    /// <param name="message">
    /// 食い違いの中身。期待値と実際の値を含む。報告してもらうときに要るので、
    /// 画面では最後に残す。
    /// </param>
    public UpdateContentMismatchException(string message) : base(message)
    {
    }
}
