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
// 商用ライセンス（レジストレーションコード）復号鍵の「空」の雛形。
//
// 本物の秘密鍵はリポジトリに含めない。公式ビルドでは、リポジトリ外
// （_secrets\LicenseRegister.Secret.cs）にある同じ形のファイルが代わりに
// コンパイルされる（切り替えは AttacheCase.Core.csproj）。
// この雛形のまま（鍵が空）だと、レジストレーションコードの検証は常に失敗し、
// アプリはフリー版として動く。それ以外の機能には影響しない。
//----------------------------------------------------------------------

namespace AttacheCase.Services;

public partial class LicenseRegister
{
    /// <summary>
    /// レジストレーションコード復号用 RSA 秘密鍵（RSAKeyValue XML）。
    /// 空文字 = 鍵なし（商用ライセンスは検証できない）。
    /// </summary>
    internal const string PrivateKey = "";
}
