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
// 更新確認サービスの入り口。テストから差し替えられるようにインターフェースを切る。
//----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;

namespace AttacheCase.Services;

public interface IUpdateService
{
    /// <summary>
    /// 配信マニフェストを取得し、署名を検証したうえで、更新があるかを返す。
    /// 例外は投げない。失敗は <see cref="UpdateCheckStatus.Failed"/> として返す。
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
