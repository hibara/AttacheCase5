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

namespace AttacheCase.Services;

/// <summary>
/// パスワードZIPの暗号方式
/// </summary>
public enum ZipPasswordEncryptionMethod
{
  /// <summary>
  /// 暗号化しない（ただの ZIP を作る。旧 Windows 版の「なし」）
  /// </summary>
  None = -1,

  /// <summary>
  /// 互換性重視（Windows Explorerで展開できる可能性が高いが暗号は弱い）
  /// </summary>
  ZipCrypto = 0,

  /// <summary>
  /// 強固（ただしWindows Explorerは未対応）
  /// </summary>
  Aes128 = 128,

  /// <summary>
  /// 強固（ただしWindows Explorerは未対応）
  /// </summary>
  Aes256 = 256
}