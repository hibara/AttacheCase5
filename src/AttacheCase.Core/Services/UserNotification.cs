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
// Core 層からの利用者通知の出口。
//
// Core は UI（Avalonia）を参照しないため、ダイアログを直接出せない。
// 既定はコンソール（標準エラー）への出力で、これは CLI 実行にそのまま適する。
// GUI アプリは起動時に DialogHelper へ差し替える（App.axaml.cs）。
// 依存注入コンテナを使わない本プロジェクトの流儀（静的シングルトン）に合わせた。
//----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace AttacheCase.Services;

public static class UserNotification
{
    /// <summary>
    /// 利用者への通知。引数は (無人実行か, タイトル, 本文)。
    /// 既定実装は標準エラーへの 1 行出力（無人実行かどうかは影響しない）。
    /// </summary>
    public static Func<bool, string, string, Task> NotifyAsync { get; set; } =
        (_, title, message) =>
        {
            Console.Error.WriteLine(title + ": " + message);
            return Task.CompletedTask;
        };
}
