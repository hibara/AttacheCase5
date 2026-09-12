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

using System.Runtime.CompilerServices;
using System.Text;

namespace AttacheCase;

internal static class ModuleInit
{
    /// <summary>
    /// 旧形式 v105 (AttacheCase 2) の復号は Shift-JIS (CP932) を使う。
    /// .NET (Core) 系は既定でコードページ系エンコーディングを持たないため、
    /// このアセンブリが読み込まれた時点で登録しておく。
    /// 登録が無いと Encoding.GetEncoding(932) が NotSupportedException になる。
    /// </summary>
    [ModuleInitializer]
    internal static void RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
