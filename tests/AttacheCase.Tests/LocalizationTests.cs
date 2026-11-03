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

//----------------------------------------------------------------------
// ローカライズの取りこぼし検出。
// 設定画面やコマンドライン関連の項目を増やすたびに、英語側の追加を
// 忘れていないかをここで機械的に確認する。
//----------------------------------------------------------------------

using System.Collections;
using System.Globalization;
using System.Resources;
using AttacheCase.Services;

namespace AttacheCase.Tests;

public class LocalizationTests
{
    private static ResourceManager CreateManager() =>
        new("AttacheCase.Resources.Strings", typeof(LocalizationService).Assembly);

    /// <summary>
    /// その言語で「実際に定義されている」キーだけを取り出す（親カルチャへのフォールバックをしない）。
    /// </summary>
    private static HashSet<string> KeysOf(ResourceManager manager, CultureInfo culture)
    {
        var set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set)
        {
            if (entry.Key is string key) keys.Add(key);
        }
        return keys;
    }

    [Fact]
    public void EveryStringExistsInBothLanguages()
    {
        var manager = CreateManager();

        var english = KeysOf(manager, CultureInfo.InvariantCulture);
        var japanese = KeysOf(manager, new CultureInfo("ja"));

        Assert.NotEmpty(english);

        var missingInJapanese = english.Except(japanese).Order().ToArray();
        var missingInEnglish = japanese.Except(english).Order().ToArray();

        Assert.True(missingInJapanese.Length == 0,
            "日本語リソースに無いキー: " + string.Join(", ", missingInJapanese));
        Assert.True(missingInEnglish.Length == 0,
            "英語リソースに無いキー: " + string.Join(", ", missingInEnglish));
    }

    [Fact]
    public void NoStringIsLeftEmpty()
    {
        var manager = CreateManager();

        foreach (var culture in new[] { CultureInfo.InvariantCulture, new CultureInfo("ja") })
        {
            var set = manager.GetResourceSet(culture, true, false);
            Assert.NotNull(set);

            foreach (DictionaryEntry entry in set)
            {
                var value = entry.Value as string;
                Assert.False(string.IsNullOrWhiteSpace(value),
                    $"{culture.Name}: '{entry.Key}' が空です");
            }
        }
    }

    [Theory]
    // 今回の一連の作業で追加した画面の代表的なキー
    [InlineData("Settings_Title")]
    [InlineData("Settings_General")]
    [InlineData("Settings_Window")]
    [InlineData("Settings_Encrypt")]
    [InlineData("Settings_Decrypt")]
    [InlineData("Settings_Delete")]
    [InlineData("Settings_DeleteOriginalAfterEncryption")]
    [InlineData("Settings_DeleteEncryptedAfterDecryption")]
    [InlineData("Settings_ShowDeleteCheckBox")]
    [InlineData("Settings_DeleteMethod")]
    [InlineData("Settings_DeleteWipeOptions")]
    [InlineData("Settings_DeleteRandomTimes")]
    [InlineData("Settings_ExitAfterProcess")]
    [InlineData("Settings_MinimizeWhileProcessing")]
    [InlineData("Settings_ShowMenuBarIcon")]
    [InlineData("Settings_ShowTaskbarIcon")]
    [InlineData("Settings_RememberSection")]
    [InlineData("Settings_RememberEncrypt")]
    [InlineData("Settings_ExecuteWithRememberedPassword")]
    [InlineData("Settings_ShowPasswordStrengthMeter")]
    [InlineData("Settings_NoMultipleInstance")]
    [InlineData("Settings_NoMaximizeInTabletMode")]
    [InlineData("Settings_FileTypeExe")]
    [InlineData("Settings_UseLastEncryptionFileType")]
    [InlineData("Settings_PortableHint")]
    [InlineData("Settings_PortableSelectFolder")]
    [InlineData("Settings_PortableOverwriteConfirm")]
    [InlineData("Settings_MultipleFiles")]
    [InlineData("Settings_MultipleFilesNormal")]
    [InlineData("Settings_MultipleFilesAllPack")]
    [InlineData("Settings_EncryptOneByOne")]
    [InlineData("Settings_EncryptOneByOneHint")]
    [InlineData("Settings_KeepTimeStamp")]
    [InlineData("Error_OutputEqualsInput")]
    [InlineData("Error_LeftoverFiles")]
    [InlineData("Error_OutputFileExists")]
    [InlineData("Settings_AutoNameInsert")]
    [InlineData("Settings_AutoNameRandomCharset")]
    [InlineData("Settings_AutoNameSymbols")]
    [InlineData("Menu_Settings")]
    [InlineData("Password_Show")]
    [InlineData("Password_Hide")]
    [InlineData("Dialog_DeleteTitle")]
    [InlineData("Error_NotEncryptedFile")]
    public void TheEnglishTextIsRealText(string key)
    {
        var manager = CreateManager();

        var english = manager.GetString(key, CultureInfo.InvariantCulture);

        Assert.False(string.IsNullOrWhiteSpace(english));

        // キー名がそのまま出ていない（リソース漏れの典型）
        Assert.NotEqual(key, english);

        // 英語のはずの場所に日本語が混ざっていない
        Assert.DoesNotContain(english, c => c >= '぀' && c <= '鿿');
    }

    /// <summary>
    /// レート制限の待ち時間メッセージは、待ち時間を 1 つだけ差し込む書式。
    /// {1} 以降を書いてしまうと、実行時に FormatException で落ちる。
    /// </summary>
    [Theory]
    [InlineData("PostPpap_TryAgainAfter")]
    [InlineData("PostPpap_ServiceBusyAfter")]
    [InlineData("PostPpap_DurationMinute")]
    [InlineData("PostPpap_DurationMinutes")]
    [InlineData("PostPpap_DurationHour")]
    [InlineData("PostPpap_DurationHours")]
    public void TheRetryMessageTakesExactlyOnePlaceholder(string key)
    {
        var manager = CreateManager();

        foreach (var culture in new[] { CultureInfo.InvariantCulture, new CultureInfo("ja") })
        {
            var format = manager.GetString(key, culture);
            Assert.NotNull(format);
            Assert.Contains("{0}", format);
            Assert.DoesNotContain("{1}", format);
        }
    }

    /// <summary>
    /// 待ち時間を差し込まない文言（1 分未満）に、うっかり書式指定子を入れていないこと。
    /// </summary>
    [Fact]
    public void TheLessThanAMinuteTextHasNoPlaceholder()
    {
        var manager = CreateManager();

        foreach (var culture in new[] { CultureInfo.InvariantCulture, new CultureInfo("ja") })
        {
            var text = manager.GetString("PostPpap_DurationLessThanAMinute", culture);
            Assert.NotNull(text);
            Assert.DoesNotContain("{0}", text);
        }
    }
}
