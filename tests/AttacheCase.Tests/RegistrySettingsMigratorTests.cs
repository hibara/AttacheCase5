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
// レジストリ設定移行のテスト。
// 実際の HKCU\Software\Hibara\AttacheCase5 には触れず、テスト専用の
// 一時キーへ旧版と同じ形式（ほぼすべて REG_SZ）で値を書いてから移行を検証する。
//----------------------------------------------------------------------

using System;
using AttacheCase.Models;
using AttacheCase.Platform;
using Microsoft.Win32;
using Xunit;

namespace AttacheCase.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class RegistrySettingsMigratorTests : IDisposable
{
    private const string TestKeyPath = @"Software\Hibara\AttacheCase5UnitTest";

    private readonly RegistryKey? _root;

    public RegistrySettingsMigratorTests()
    {
        if (!OperatingSystem.IsWindows()) return;
        Registry.CurrentUser.DeleteSubKeyTree(TestKeyPath, throwOnMissingSubKey: false);
        _root = Registry.CurrentUser.CreateSubKey(TestKeyPath);
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        _root?.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(TestKeyPath, throwOnMissingSubKey: false);
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Windows のみ（レジストリに依存）";
        }
    }

    [WindowsFact]
    public void 旧版の代表的な設定を移行できる()
    {
        using (var option = _root!.CreateSubKey("Option"))
        {
            // 旧版はほぼすべて REG_SZ で書く
            option.SetValue("Language", "en");
            option.SetValue("fEndToExit", "1");
            option.SetValue("fSaveToSameFldr", "1");
            option.SetValue("SaveToSameFldrPath", @"C:\Backup");
            option.SetValue("fAutoName", "1");
            option.SetValue("AutoNameFormatText", "<filename>_<date:yyyy_MM_dd><ext>");
            option.SetValue("MissTypeLimitsNum", "5");
            option.SetValue("fBroken", "1");
            option.SetValue("EncryptionFileType", "3"); // ZIP
            option.SetValue("ZipEncryptionAlgorithm", "2");
            option.SetValue("fNoParentFldr", "1");
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);

        Assert.True(s.MigratedFromRegistry);
        Assert.Equal("en", s.Language);
        Assert.True(s.ExitAfterProcess);
        Assert.True(s.SaveToSameFolder);
        Assert.Equal(@"C:\Backup", s.SaveToFolderPath);
        Assert.True(s.UseAutoName);
        Assert.Equal("<filename>_<date:yyyy_MM_dd><ext>", s.AutoNameFormat);
        Assert.Equal(5, s.MissTypeLimits);
        Assert.True(s.BreakFileWhenLimitExceeded);
        Assert.Equal(EncryptionFileTypes.Zip, s.EncryptionFileType);
        Assert.Equal(2, s.ZipAlgorithm);
        Assert.True(s.NoParentFolder);
    }

    [WindowsFact]
    public void REG_DWORDで書かれた値も読める()
    {
        using (var option = _root!.CreateSubKey("Option"))
        {
            option.SetValue("fEndToExit", 1, RegistryValueKind.DWord);
            option.SetValue("MissTypeLimitsNum", 7, RegistryValueKind.DWord);
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);

        Assert.True(s.ExitAfterProcess);
        Assert.Equal(7, s.MissTypeLimits);
    }

    [WindowsFact]
    public void 削除方法は対象フラグと共有の方法値から組み立てる()
    {
        using (var option = _root!.CreateSubKey("Option"))
        {
            option.SetValue("fDelOrgFile", "1");
            option.SetValue("fDelEncFile", "0");
            option.SetValue("fCompleteDelFile", "3"); // 完全削除
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);

        Assert.Equal(DeleteModes.Wipe, s.DeleteMode);
        Assert.Equal(DeleteModes.None, s.DeleteEncryptedFileMode);
    }

    [WindowsFact]
    public void 自己実行形式の既定はATCへ倒す()
    {
        using (var option = _root!.CreateSubKey("Option"))
        {
            option.SetValue("EncryptionFileType", "2"); // 旧: 自己実行形式
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);
        Assert.Equal(EncryptionFileTypes.Atc, s.EncryptionFileType);
    }

    [WindowsFact]
    public void AC4世代の旧名にも対応する()
    {
        using (var option = _root!.CreateSubKey("Option"))
        {
            option.SetValue("fDecodeToSameFldr", "1");
            option.SetValue("DecodeToSameFldrPath", @"C:\Restore");
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);

        Assert.True(s.DecryptSaveToSameFolder);
        Assert.Equal(@"C:\Restore", s.DecryptSaveToFolderPath);
    }

    [WindowsFact]
    public void fNotMaskPasswordはMyKeyから読む()
    {
        using (var myKey = _root!.CreateSubKey("MyKey"))
        {
            myKey.SetValue("fNotMaskPassword", "1");
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);
        Assert.True(s.NotMaskPassword);
    }

    [WindowsFact]
    public void 記憶パスワードのフラグはMyKeyから読む()
    {
        using (var myKey = _root!.CreateSubKey("MyKey"))
        {
            myKey.SetValue("fMyEncryptPasswordKeep", "1");
            myKey.SetValue("fMyDecryptPasswordKeep", "0");
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);
        Assert.True(s.RememberEncryptPassword);
        Assert.False(s.RememberDecryptPassword);
    }

    [WindowsFact]
    public void 関連付けアイコン番号を移行する()
    {
        using (var option = _root!.CreateSubKey("Option"))
        {
            option.SetValue("AtcsFileIconIndex", "3");
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);
        Assert.Equal(3, s.AssociationIconIndex);
    }

    [WindowsFact]
    public void 関連付けアイコン番号の範囲外は既定の1へ倒す()
    {
        // 旧版の 0 は「本体アイコン」だが、統合版の exe はアイコンを 1 つしか
        // 持たないため 1〜4 以外は 1 に倒す（>4 は旧版も 1 に戻していた）
        using (var option = _root!.CreateSubKey("Option"))
        {
            option.SetValue("AtcsFileIconIndex", "0");
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);
        Assert.Equal(1, s.AssociationIconIndex);
    }

    [WindowsFact]
    public void 対応外の言語コードは空文字へ倒す()
    {
        // ResolveLanguage は ja / en 以外をシステム言語へフォールバックする。
        // 契約外の値を settings.json に永続化しない
        using (var option = _root!.CreateSubKey("Option"))
        {
            option.SetValue("Language", "fr");
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);
        Assert.Equal(string.Empty, s.Language);
    }

    [WindowsFact]
    public void CompressionLevelが無ければ新モデルの既定値のまま()
    {
        // Option キーはあるが CompressionLevel 値が無い（部分的な旧設定）
        using (var option = _root!.CreateSubKey("Option"))
        {
            option.SetValue("fEndToExit", "1");
        }

        var s = RegistrySettingsMigrator.Migrate(_root!);
        Assert.Equal(new AppSettings().CompressionLevel, s.CompressionLevel);
    }

    [WindowsFact]
    public void 値が無ければ新モデルの既定値のまま()
    {
        var s = RegistrySettingsMigrator.Migrate(_root!);

        var defaults = new AppSettings();
        Assert.Equal(defaults.Language, s.Language);
        Assert.Equal(defaults.ConfirmOverwrite, s.ConfirmOverwrite);
        Assert.Equal(defaults.MissTypeLimits, s.MissTypeLimits);
        Assert.Equal(defaults.DeleteRandomTimes, s.DeleteRandomTimes);
        Assert.True(s.MigratedFromRegistry);
    }
}
