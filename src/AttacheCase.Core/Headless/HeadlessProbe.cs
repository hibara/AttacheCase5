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
// 暗号化ファイルのヘッダを読んで種別を判定する。
// Windows 版 AttacheCase\Headless\HeadlessProbe.cs と同じ公開契約
// （AttacheCase のファイルでなければ 0、読めなくても 0）。
// 実装は DecryptionService の版数検出（自己実行形式のトークンスキャン込み）を使う。
//----------------------------------------------------------------------
using System.IO;
using System.Threading;
using AttacheCase.Services;

namespace AttacheCase.Headless;

public static class HeadlessProbe
{
    /// <summary>
    /// 暗号化ファイルのデータバージョンを返す。
    /// AttacheCase の暗号化ファイルでない場合や読み取れない場合は 0。
    /// </summary>
    /// <remarks>
    /// 「AttacheCase のファイルではない」と「アクセス拒否・I/O エラーで読めない」を
    /// 区別せず、どちらも 0 を返す軽量なヘルパー。失敗の理由を終了コードへ写像する
    /// 必要がある経路（<see cref="HeadlessRunner"/>）は自前でファイルを開いて
    /// 例外を判定している。
    /// </remarks>
    public static int GetDataFileVersion(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return 0;

        try
        {
            return DecryptionService
                .DetectDataFileVersionAsync(filePath, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// AttacheCase の暗号化ファイルかどうか。
    /// </summary>
    public static bool IsEncryptedFile(string? filePath)
    {
        return GetDataFileVersion(filePath) > 0;
    }
}
