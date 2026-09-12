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
/// 汎用パスワード項目（Generic Password）の保存/取得/削除を提供するインターフェース。
/// macOS では Security.framework の Keychain にバインドされる。
/// テストではメモリ実装に差し替え可能。
/// </summary>
public interface IKeychainService
{
    /// <summary>
    /// 指定の (service, account) に紐づくデータを取得する。
    /// 項目が無い場合は false を返し、data は null。
    /// </summary>
    bool TryGet(string service, string account, out byte[]? data);

    /// <summary>
    /// 指定の (service, account) にデータを保存する。
    /// 既存項目があれば上書き、無ければ新規追加。
    /// </summary>
    void Set(string service, string account, byte[] data);

    /// <summary>
    /// 指定の (service, account) の項目を削除する。
    /// 項目が無い場合は false を返す（例外にしない）。
    /// </summary>
    bool Delete(string service, string account);
}
