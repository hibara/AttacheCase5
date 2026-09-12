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
// パスワードファイルの読み込み（設定「高度な設定」）。
//
// ファイルの中身から鍵素材（SHA-256 の 32 バイト）を作る。1 バイトでも
// 内容が変われば復号できなくなる点は Windows 版と同じ。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using AttacheCase.Cli;
using AttacheCase.Models;

namespace AttacheCase.Services;

/// <summary>読み込んだパスワードファイル。使えなかった場合は <see cref="Material"/> が null</summary>
public readonly record struct PasswordFileMaterial(byte[]? Material, string? Path)
{
    public static PasswordFileMaterial None => new(null, null);

    public bool IsAvailable => Material != null;
}

public static class PasswordFileLoader
{
    private static LocalizationService Localize => LocalizationService.Instance;

    /// <summary>
    /// 設定「暗号化／復号時にパスワードファイルを自動チェックする」に従って読み込む。
    /// </summary>
    /// <param name="context">この実行の設定と条件</param>
    /// <param name="forDecryption">復号側の設定を見るなら true</param>
    public static async Task<PasswordFileMaterial> TryLoadConfiguredAsync(RunContext context, bool forDecryption)
    {
        var settings = context.Settings;

        var enabled = forDecryption
            ? settings.CheckPasswordFileOnDecrypt
            : settings.CheckPasswordFileOnEncrypt;

        if (!enabled) return PasswordFileMaterial.None;

        var path = forDecryption
            ? settings.DecryptPasswordFilePath
            : settings.EncryptPasswordFilePath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            // 「無くてもエラーを出さない」が外れていれば知らせる
            if (!settings.NoErrorWhenPasswordFileMissing)
            {
                await UserNotification.NotifyAsync(
                    context.IsUnattended,
                    Localize["PasswordFile_NotFoundTitle"],
                    Localize.GetString("PasswordFile_NotFound", path));
            }

            return PasswordFileMaterial.None;
        }

        return await LoadAsync(path, context);
    }

    /// <summary>
    /// 指定されたファイルから鍵素材を作る。読めなければ知らせて None を返す。
    /// </summary>
    public static async Task<PasswordFileMaterial> LoadAsync(string path, RunContext context)
    {
        try
        {
            return new PasswordFileMaterial(PasswordFileHash.ComputeSha256(path), path);
        }
        catch (Exception)
        {
            await UserNotification.NotifyAsync(
                context.IsUnattended,
                Localize["PasswordFile_NotFoundTitle"],
                Localize.GetString("PasswordFile_ReadFailed", path));

            return PasswordFileMaterial.None;
        }
    }
}
