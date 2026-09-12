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
// 旧 Windows 版（HKCU\Software\Hibara\AttacheCase5）のレジストリ設定を、
// 統一 JSON 設定（AppSettings / settings.json）へ一度だけ移行する。
//
// 方針:
// - settings.json が無い初回起動時にだけ実行する（SettingsService.Load が呼ぶ）
// - レジストリは消さない。旧版と併用しても壊れないようにするため
// - 旧版は整数も真偽値も REG_SZ（"1"/"0" や "3"）で書くが、一部は REG_DWORD の
//   ことがある（AppVersion など、AC3/4 由来の値）。読み取りは種別に依存しない
// - 新モデルに対応する項目だけを移す。対応の無い項目（MOTW 設定・ウィンドウ
//   位置等）は移さない
// - MyKey の保存パスワード（DPAPI blob）は新モデル側に受け皿が無いため移さない
// - Registration（商用ライセンス）はライセンス登録機能の実装時（Phase 7）に扱う
//
// 対応表の根拠: 旧版 AppSettings.cs の ReadOptionsFromRegistry() /
// SaveOptionsToRegistry()（2026-09-07 時点のソース）。
//----------------------------------------------------------------------

using System;
using System.Runtime.Versioning;
using AttacheCase.Models;
using Microsoft.Win32;

namespace AttacheCase.Platform;

[SupportedOSPlatform("windows")]
public static class RegistrySettingsMigrator
{
    public const string RegistryRootPath = @"Software\Hibara\AttacheCase5";

    /// <summary>
    /// 既定の場所（HKCU\Software\Hibara\AttacheCase5）から移行を試みる。
    /// キーが存在しなければ false。
    /// </summary>
    public static bool TryMigrateFromDefaultLocation(out AppSettings? settings)
    {
        settings = null;

        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(RegistryRootPath);
            if (root == null) return false;

            settings = Migrate(root);
            return true;
        }
        catch (Exception)
        {
            // レジストリが読めない環境でも起動は続ける（既定値で開始）
            return false;
        }
    }

    /// <summary>
    /// 指定されたルートキー（AttacheCase5 直下相当）から設定を組み立てる。
    /// テストからは一時キーを注入して実レジストリを汚さない。
    /// </summary>
    public static AppSettings Migrate(RegistryKey root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var s = new AppSettings { MigratedFromRegistry = true };

        using var option = root.OpenSubKey("Option");
        using var myKey = root.OpenSubKey("MyKey");

        if (option != null)
        {
            // ---- 一般 ----
            // 許可値（ja / en）以外は「システムに従う」( 空文字 ) に倒す。
            // 契約外の値をそのまま settings.json に永続化しない
            var language = ReadString(option, "Language", s.Language);
            s.Language = language is "ja" or "en" ? language : string.Empty;

            // テーマ（auto/light/dark）。知らない値は auto に倒す
            var theme = ReadString(option, "ThemeColorName", s.Theme);
            s.Theme = theme is "light" or "dark" ? theme : "auto";
            s.ExitAfterProcess = ReadBool(option, "fEndToExit", s.ExitAfterProcess);
            s.OpenDecryptedFile = ReadBool(option, "fOpenFile", s.OpenDecryptedFile);
            s.CheckForUpdatesOnStartup = ReadBool(option, "fCheckUpdate", s.CheckForUpdatesOnStartup);
            // fMainWindowMinimize は ver.5 で廃止済み（読み書きされない）。移さない
            s.ShowMenuBarIcon = ReadBool(option, "fTaskTrayIcon", s.ShowMenuBarIcon);
            s.WindowForeground = ReadBool(option, "fWindowForeground", s.WindowForeground);
            s.NoMultipleInstance = ReadBool(option, "fNoMultipleInstance", s.NoMultipleInstance);

            // ---- 暗号化 ----
            // 旧: 0=未設定, 1=ATC, 2=自己実行形式, 3=ZIP。
            // 自己実行形式（2）は保存形式の既定としては廃止 → ATC へ倒す
            var fileType = ReadInt(option, "EncryptionFileType", 0);
            s.EncryptionFileType = fileType == 3 ? EncryptionFileTypes.Zip : EncryptionFileTypes.Atc;

            s.SaveToSameFolder = ReadBool(option, "fSaveToSameFldr", s.SaveToSameFolder);
            s.SaveToFolderPath = ReadString(option, "SaveToSameFldrPath", s.SaveToFolderPath);
            s.ConfirmOverwrite = ReadBool(option, "fEncryptConfirmOverwrite", s.ConfirmOverwrite);
            s.EncryptOneByOne = ReadBool(option, "fFilesOneByOne", s.EncryptOneByOne);
            s.KeepTimeStamp = ReadBool(option, "fKeepTimeStamp", s.KeepTimeStamp);
            s.IncludeOriginalExtension = ReadBool(option, "fExtInAtcFileName", s.IncludeOriginalExtension);

            s.UseAutoName = ReadBool(option, "fAutoName", s.UseAutoName);
            s.AutoNameFormat = ReadString(option, "AutoNameFormatText", s.AutoNameFormat);
            s.AutoNameRandomUpperCase = ReadBool(option, "fAutoNameUpperCase", s.AutoNameRandomUpperCase);
            s.AutoNameRandomLowerCase = ReadBool(option, "fAutoNameLowerCase", s.AutoNameRandomLowerCase);
            s.AutoNameRandomNumbers = ReadBool(option, "fAutoNameNumbers", s.AutoNameRandomNumbers);
            s.AutoNameRandomSymbols = ReadBool(option, "fAutoNameSymbols", s.AutoNameRandomSymbols);

            s.UseCamouflageExtension = ReadBool(option, "fAddCamoExt", s.UseCamouflageExtension);
            s.CamouflageExtension = ReadString(option, "CamoExt", s.CamouflageExtension);

            // 0=無圧縮, 1=速度優先, 2=サイズ優先（新旧で同じ意味）
            // 値が無いときは新モデルの既定値（1 = 速度優先）を維持する。
            // 固定の 2 を既定にすると、部分的な旧設定の移行でサイズ優先に変わってしまう
            s.CompressionLevel = Math.Clamp(
                ReadInt(option, "CompressionLevel", s.CompressionLevel), 0, 2);
            s.MissTypeLimits = Math.Clamp(ReadInt(option, "MissTypeLimitsNum", s.MissTypeLimits), 1, 10);
            s.BreakFileWhenLimitExceeded = ReadBool(option, "fBroken", s.BreakFileWhenLimitExceeded);

            // ---- ZIP ----
            // 0〜2 = 暗号方式、3 = 暗号化なし（旧 ver.5 と同じ語彙）。範囲外は既定の 0 へ
            var zipAlgo = ReadInt(option, "ZipEncryptionAlgorithm", 0);
            s.ZipAlgorithm = zipAlgo is >= 0 and <= 3 ? zipAlgo : 0;
            s.ZipSaveToSameFolder = ReadBool(option, "fZipToSameFldr", s.ZipSaveToSameFolder);
            s.ZipSaveToFolderPath = ReadString(option, "ZipToSameFldrPath", s.ZipSaveToFolderPath);

            // ---- 復号 ----
            // AC4 世代の旧名（fDecodeToSameFldr / DecodeToSameFldrPath）にも対応
            s.DecryptSaveToSameFolder = ReadBool(option, "fDecryptToSameFldr",
                ReadBool(option, "fDecodeToSameFldr", s.DecryptSaveToSameFolder));
            s.DecryptSaveToFolderPath = ReadString(option, "DecryptToSameFldrPath",
                ReadString(option, "DecodeToSameFldrPath", s.DecryptSaveToFolderPath));
            s.NoParentFolder = ReadBool(option, "fNoParentFldr", s.NoParentFolder);
            s.SetTimeStampToNow = ReadBool(option, "fSameTimeStamp", s.SetTimeStampToNow);
            s.ContinueOnHashMismatch = ReadBool(option, "fSalvageIgnoreHashCheck", s.ContinueOnHashMismatch);

            // ---- パスワードファイル ----
            s.AllowPasswordFileDragDrop = ReadBool(option, "fAllowPassFile", s.AllowPasswordFileDragDrop);
            s.CheckPasswordFileOnEncrypt = ReadBool(option, "fCheckPassFile", s.CheckPasswordFileOnEncrypt);
            s.EncryptPasswordFilePath = ReadString(option, "PassFilePath", s.EncryptPasswordFilePath);
            s.CheckPasswordFileOnDecrypt = ReadBool(option, "fCheckPassFileDecrypt", s.CheckPasswordFileOnDecrypt);
            s.DecryptPasswordFilePath = ReadString(option, "PassFilePathDecrypt", s.DecryptPasswordFilePath);
            s.NoErrorWhenPasswordFileMissing = ReadBool(option, "fNoErrMsgOnPassFile", s.NoErrorWhenPasswordFileMissing);
            s.ExecuteWithPasswordFileWithoutConfirm = ReadBool(option, "fPasswordFileExe", s.ExecuteWithPasswordFileWithoutConfirm);

            // ---- 削除 ----
            // 旧版は「削除するか」のフラグ 2 つ（元ファイル / 暗号化ファイル）と、
            // 共有の「削除方法」（fCompleteDelFile: 0=しない,1=通常,2=ごみ箱,3=完全削除）
            // の組み合わせ。新モデルは対象ごとに方法を持つ
            var deleteMethod = Math.Clamp(ReadInt(option, "fCompleteDelFile", 0), 0, 3);
            s.DeleteMode = ReadBool(option, "fDelOrgFile", false) ? deleteMethod : DeleteModes.None;
            s.DeleteEncryptedFileMode = ReadBool(option, "fDelEncFile", false) ? deleteMethod : DeleteModes.None;
            s.DeleteRandomTimes = Math.Clamp(ReadInt(option, "DelRandNum", s.DeleteRandomTimes), 1, 99);
            s.DeleteZeroTimes = Math.Clamp(ReadInt(option, "DelZeroNum", s.DeleteZeroTimes), 1, 99);
            s.ConfirmDelete = ReadBool(option, "fConfirmToDeleteAfterEncryption", s.ConfirmDelete);

            // ---- 関連付け ----
            // 旧版の 0 は「本体アイコン」だが、統合版は同梱 .ico（1〜4）だけなので
            // 範囲外は既定の 1 に倒す（旧版も >4 は 1 に戻していた）
            var iconIndex = ReadInt(option, "AtcsFileIconIndex", s.AssociationIconIndex);
            s.AssociationIconIndex = iconIndex is >= 1 and <= 4 ? iconIndex : 1;
        }

        if (myKey != null)
        {
            // fNotMaskPassword だけは Option ではなく MyKey に居る（旧版の実装どおり）
            s.NotMaskPassword = ReadBool(myKey, "fNotMaskPassword", s.NotMaskPassword);

            // 記憶パスワードの「記憶するか」フラグ。
            // パスワードの実体（DPAPI blob）はここでは移さない。settings.json に
            // 秘密を入れない方針のため、SecretVault（_AtcVault.json）への移行として
            // RememberedPasswordService.TryMigrateFromLegacyRegistry が別途行う
            s.RememberEncryptPassword = ReadBool(myKey, "fMyEncryptPasswordKeep", s.RememberEncryptPassword);
            s.RememberDecryptPassword = ReadBool(myKey, "fMyDecryptPasswordKeep", s.RememberDecryptPassword);
        }

        return s;
    }

    // =====================================================================
    // 種別に依存しない読み取り
    //   旧版はほぼすべて REG_SZ（"1"/"0"、"3" など）で書くが、AC3/4 由来の値や
    //   手で直された値が REG_DWORD のことがある。どちらでも読めるようにする
    // =====================================================================

    private static bool ReadBool(RegistryKey key, string name, bool defaultValue)
    {
        var value = key.GetValue(name);
        return value switch
        {
            int i => i != 0,
            string t => t == "1",
            _ => defaultValue,
        };
    }

    private static int ReadInt(RegistryKey key, string name, int defaultValue)
    {
        var value = key.GetValue(name);
        return value switch
        {
            int i => i,
            string t when int.TryParse(t, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    private static string ReadString(RegistryKey key, string name, string defaultValue)
    {
        return key.GetValue(name) as string ?? defaultValue;
    }
}
