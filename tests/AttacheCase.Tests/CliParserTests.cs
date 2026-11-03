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
// コマンドライン引数の解析テスト。
// Windows 版と同じ /name=value 表記が正しく解釈されること、
// macOS で使えないオプションが黙って無視されないことを確認する。
//----------------------------------------------------------------------

using System.IO.Compression;
using AttacheCase.Cli;
using AttacheCase.Services;

namespace AttacheCase.Tests;

public class CliParserTests
{
    [Fact]
    public void NoArguments_MeansNoInstruction()
    {
        var options = CliParser.Parse([]);

        Assert.Null(options.ParseError);
        Assert.Equal(CliAction.Auto, options.Action);
        Assert.False(options.HasAnyInstruction);
    }

    [Fact]
    public void SlashStyleOptions_AreParsed()
    {
        var options = CliParser.Parse(["/en", "/p=secret", "/complv=0", "/typelimit=5"]);

        Assert.Null(options.ParseError);
        Assert.Equal(CliAction.Encrypt, options.Action);
        Assert.Equal("secret", options.Password);
        Assert.Equal(CompressionLevel.NoCompression, options.Compression);
        Assert.Equal(5, options.MissTypeLimits);
    }

    [Fact]
    public void DoubleDashStyleOptions_AreEquivalent()
    {
        var options = CliParser.Parse(["--decrypt", "--password=secret"]);

        Assert.Null(options.ParseError);
        Assert.Equal(CliAction.Decrypt, options.Action);
        Assert.Equal("secret", options.Password);
    }

    [Fact]
    public void SeparatedValue_IsTakenFromNextArgument()
    {
        var directory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var options = CliParser.Parse(["--saveto", directory]);

        Assert.Null(options.ParseError);
        Assert.Equal(Path.GetFullPath(directory), options.EncryptOutputDirectory);
        Assert.Empty(options.InputPaths);
    }

    [Fact]
    public void PasswordContainingEqualSign_KeepsEverythingAfterTheFirstOne()
    {
        var options = CliParser.Parse(["/p=a=b=c"]);

        Assert.Equal("a=b=c", options.Password);
    }

    [Fact]
    public void ExistingPath_IsCollectedAsTarget()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(file, "test");
        try
        {
            var options = CliParser.Parse([file]);

            Assert.Null(options.ParseError);
            Assert.Equal([Path.GetFullPath(file)], options.InputPaths);
            Assert.True(options.HasAnyInstruction);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AbsolutePathThatExists_IsNotMistakenForAnOption()
    {
        // macOS の絶対パスは「/」で始まるため、実在するなら対象パスとして扱う
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(file, "test");
        try
        {
            var options = CliParser.Parse(["/en", file]);

            Assert.Null(options.ParseError);
            Assert.Single(options.InputPaths);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void OverwriteOption_FollowsTheGuiMeaning()
    {
        Assert.True(CliParser.Parse(["/ow=0"]).OverwriteWithoutConfirm);
        Assert.False(CliParser.Parse(["/ow=1"]).OverwriteWithoutConfirm);
        Assert.True(CliParser.Parse(["/y"]).OverwriteWithoutConfirm);
        Assert.True(CliParser.Parse(["/force"]).OverwriteWithoutConfirm);

        // 指定が無ければ設定の値をそのまま使う（上書きしない）
        Assert.Null(CliParser.Parse([]).OverwriteWithoutConfirm);
    }

    [Fact]
    public void DeleteMode_WithoutValue_MeansWipe()
    {
        Assert.Equal(3, CliParser.Parse(["/del"]).DeleteMode);
        Assert.Equal(1, CliParser.Parse(["/del=1"]).DeleteMode);
        Assert.Equal(2, CliParser.Parse(["/del=2"]).DeleteMode);
        Assert.Equal(0, CliParser.Parse(["/del=9"]).DeleteMode);
        Assert.Equal(3, CliParser.Parse(["/delenc"]).DeleteEncryptedFileMode);
    }

    [Fact]
    public void CompressRate_MapsTheSameWayAsTheWindowsGui()
    {
        Assert.Equal(CompressionLevel.NoCompression, CliParser.Parse(["/comprate=0"]).Compression);
        Assert.Equal(CompressionLevel.Fastest, CliParser.Parse(["/comprate=5"]).Compression);
        Assert.Equal(CompressionLevel.Optimal, CliParser.Parse(["/comprate=6"]).Compression);
    }

    [Fact]
    public void ZipOptions_AreSupportedOnMac()
    {
        var options = CliParser.Parse(["/entype=3", "/zipalgo=2"]);

        Assert.Null(options.ParseError);
        Assert.Equal(CliEncryptionType.Zip, options.EncryptionType);
        Assert.Equal(ZipPasswordEncryptionMethod.Aes256, options.ZipAlgorithm);
    }

    [Fact]
    public void CamouflageExtension_GetsALeadingDot()
    {
        Assert.Equal(".jpg", CliParser.Parse(["/camoext=jpg"]).CamouflageExtension);
        Assert.Equal(".jpg", CliParser.Parse(["/camoext=.jpg"]).CamouflageExtension);
    }

    [Fact]
    public void PostPpap_WithoutValue_MeansDecryptWithRegisteredAccounts()
    {
        var withEmail = CliParser.Parse(["/p-ppap=someone@example.com"]);
        Assert.Equal("someone@example.com", withEmail.PostPpapRecipientEmail);
        Assert.False(withEmail.UsePostPpapAccounts);

        var withoutEmail = CliParser.Parse(["/p-ppap"]);
        Assert.Null(withoutEmail.PostPpapRecipientEmail);
        Assert.True(withoutEmail.UsePostPpapAccounts);
    }

    [Fact]
    public void GuiOnlyOptions_AreAcceptedButRecordedAsIgnored()
    {
        var options = CliParser.Parse(["/theme=dark", "/askende=1", "/imeon=1", "/motwall=1"]);

        Assert.Null(options.ParseError);
        Assert.Equal(4, options.IgnoredOptions.Count);
    }

    [Fact]
    public void ExitOption_IsHonouredOnMac()
    {
        Assert.True(CliParser.Parse(["/exit"]).ExitAfterProcess);
        Assert.True(CliParser.Parse(["/exit=1"]).ExitAfterProcess);
        Assert.False(CliParser.Parse(["/exit=0"]).ExitAfterProcess);
        Assert.Null(CliParser.Parse([]).ExitAfterProcess);

        // 無視されるオプションとしては記録されない
        Assert.Empty(CliParser.Parse(["/exit=1"]).IgnoredOptions);
    }

    [Fact]
    public void SettingOptions_AreParsedInsteadOfBeingIgnored()
    {
        Assert.True(CliParser.Parse(["/opf=1"]).OpenDecryptedFile);
        Assert.True(CliParser.Parse(["/nohide"]).NotMaskPassword);
        Assert.False(CliParser.Parse(["/comfdel=0"]).ConfirmDelete);
        Assert.Empty(CliParser.Parse(["/opf=1", "/nohide", "/comfdel=0"]).IgnoredOptions);
    }

    [Fact]
    public void PasswordOptions_AreParsedInsteadOfBeingIgnored()
    {
        // 旧版の /mempexe（記憶パスワードで確認なしに実行）と /psm（強度メーター）は
        // 5.5 で設定として復活した。無視一覧に戻すと設定画面の項目に引数が効かなくなる
        Assert.True(CliParser.Parse(["/mempexe=1"]).ExecuteWithRememberedPassword);
        Assert.True(CliParser.Parse(["/mempexe"]).ExecuteWithRememberedPassword);
        Assert.False(CliParser.Parse(["/psm=0"]).ShowPasswordStrengthMeter);
        Assert.Empty(CliParser.Parse(["/mempexe", "/psm=0"]).IgnoredOptions);

        var effective = CliParser.Parse(["/mempexe=1", "/psm=0"])
            .ApplyTo(new AttacheCase.Models.AppSettings());
        Assert.True(effective.ExecuteWithRememberedPassword);
        Assert.False(effective.ShowPasswordStrengthMeter);

        // 既定: 確認なし実行は OFF、強度メーターは ON（旧版と同じ）
        var defaults = new AttacheCase.Models.AppSettings();
        Assert.False(defaults.ExecuteWithRememberedPassword);
        Assert.True(defaults.ShowPasswordStrengthMeter);
    }

    [Fact]
    public void WindowOptions_AreParsed()
    {
        Assert.True(CliParser.Parse(["/tsktr=1"]).ShowMenuBarIcon);
        Assert.True(CliParser.Parse(["/front"]).WindowForeground);
        Assert.False(CliParser.Parse(["/nomulti=0"]).NoMultipleInstance);
        Assert.Empty(CliParser.Parse(["/tsktr=1", "/front", "/nomulti=1"]).IgnoredOptions);

        var effective = CliParser.Parse(["/tsktr=1", "/front=1", "/nomulti=1"])
            .ApplyTo(new AttacheCase.Models.AppSettings());

        Assert.True(effective.ShowMenuBarIcon);
        Assert.True(effective.WindowForeground);
        Assert.True(effective.NoMultipleInstance);
    }

    [Fact]
    public void SelfExecutableOutput_IsWindowsOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows では受理され、SelfExecutable が立つ
            var parsed = CliParser.Parse(["/exeout=1"]);
            Assert.Null(parsed.ParseError);
            Assert.True(parsed.SelfExecutable);

            var byType = CliParser.Parse(["/entype=2"]);
            Assert.Null(byType.ParseError);
            Assert.True(byType.SelfExecutable);
        }
        else
        {
            // Windows 以外では理由つきで断られる
            Assert.NotNull(CliParser.Parse(["/exeout=1"]).ParseError);
            Assert.NotNull(CliParser.Parse(["/exeout"]).ParseError);
            Assert.NotNull(CliParser.Parse(["/entype=2"]).ParseError);
        }

        // 「自己実行形式にしない」指定はどの OS でも受理してよい
        var off = CliParser.Parse(["/exeout=0"]);
        Assert.Null(off.ParseError);
        Assert.False(off.SelfExecutable);
    }

    [Fact]
    public void UnsupportedOptions_ReportTheReason()
    {
        Assert.NotNull(CliParser.Parse(["/slvgfolder=1"]).ParseError);
        Assert.NotNull(CliParser.Parse(["/slvgsame=1"]).ParseError);
    }

    [Fact]
    public void LicenseOption_IsParsed()
    {
        // /lc=ファイル → 登録、/lc=（空）→ 削除、指定なし → null
        Assert.Equal("license.txt", CliParser.Parse(["/lc=license.txt"]).LicenseFile);
        Assert.Equal("", CliParser.Parse(["/lc="]).LicenseFile);
        Assert.Null(CliParser.Parse([]).LicenseFile);
        Assert.Null(CliParser.Parse(["/lc=license.txt"]).ParseError);
    }

    [Fact]
    public void UnknownOption_IsAnError()
    {
        var options = CliParser.Parse(["/nosuchoption=1"]);

        Assert.NotNull(options.ParseError);
        Assert.Contains("/nosuchoption=1", options.ParseError);
    }

    [Fact]
    public void MissingKeyFile_IsAnError()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-key.atcpub");
        Assert.NotNull(CliParser.Parse(["/pubkey=" + missing]).ParseError);
        Assert.NotNull(CliParser.Parse(["/privkey=" + missing]).ParseError);
    }

    [Fact]
    public void HelpAndVersion_ShortCircuitTheParse()
    {
        Assert.Equal(CliAction.ShowHelp, CliParser.Parse(["--help"]).Action);
        Assert.Equal(CliAction.ShowHelp, CliParser.Parse(["/?"]).Action);
        Assert.Equal(CliAction.ShowHelp, CliParser.Parse(["-h"]).Action);
        Assert.Equal(CliAction.ShowVersion, CliParser.Parse(["--version"]).Action);
        Assert.Equal(CliAction.ShowVersion, CliParser.Parse(["/version"]).Action);
    }

    [Fact]
    public void MissingInputPath_IsReportedAsFileNotFound()
    {
        var options = CliParser.Parse([Path.Combine(Path.GetTempPath(), "no-such-target.txt")]);

        Assert.NotNull(options.ParseError);
        Assert.Equal(AtcExitCode.FileNotFound, options.ParseErrorCode);
    }

    [Fact]
    public void AbsolutePathAsSeparatedValue_IsNotMistakenForAnOption()
    {
        // macOS ではフォルダー指定が「/」で始まるため、値として拾えなければならない
        var directory = Directory.CreateTempSubdirectory("atc-cli-out").FullName;
        try
        {
            var options = CliParser.Parse(["--dsaveto", directory]);

            Assert.Null(options.ParseError);
            Assert.Equal(Path.GetFullPath(directory), options.DecryptOutputDirectory);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ListFile_IsExpandedRelativeToItself()
    {
        var directory = Directory.CreateTempSubdirectory("atc-cli-list").FullName;
        try
        {
            var target = Path.Combine(directory, "target.txt");
            File.WriteAllText(target, "body");

            var listFile = Path.Combine(directory, "list.txt");
            File.WriteAllText(listFile, "target.txt\n\n" + target + "\n");

            var options = CliParser.Parse(["/list=" + listFile]);

            Assert.Null(options.ParseError);
            Assert.Equal(2, options.InputPaths.Count);
            Assert.All(options.InputPaths, p => Assert.Equal(target, p));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ListFile_WithMissingPath_IsAnError()
    {
        var directory = Directory.CreateTempSubdirectory("atc-cli-list").FullName;
        try
        {
            var listFile = Path.Combine(directory, "list.txt");
            File.WriteAllText(listFile, "no-such-file.txt\n");

            Assert.NotNull(CliParser.Parse(["/list=" + listFile]).ParseError);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void NotMaximizedOption_IsParsedInsteadOfBeingIgnored()
    {
        // 旧版の /notmaximized（PC がタブレットモードのとき最大化で起動しない）は 5.5.1 で設定として復活。
        // 無視一覧に戻すと設定画面の項目に引数が効かなくなる
        Assert.True(CliParser.Parse(["/notmaximized=1"]).NoMaximizeInTabletMode);
        Assert.True(CliParser.Parse(["/notmaximized"]).NoMaximizeInTabletMode);
        Assert.False(CliParser.Parse(["/notmaximized=0"]).NoMaximizeInTabletMode);
        Assert.Empty(CliParser.Parse(["/notmaximized"]).IgnoredOptions);
        Assert.Contains("/notmaximized", CliParser.Parse(["/notmaximized"]).GuiOnlyOptionsGiven());

        var effective = CliParser.Parse(["/notmaximized=1"]).ApplyTo(new AttacheCase.Models.AppSettings());
        Assert.True(effective.NoMaximizeInTabletMode);

        // 既定は OFF（旧版と同じ）
        Assert.False(new AttacheCase.Models.AppSettings().NoMaximizeInTabletMode);
    }

    [Fact]
    public void ChkdelOption_IsParsedInsteadOfBeingIgnored()
    {
        // 旧版の /chkdel（メインウィンドウに削除のチェックボックスを表示する）は 5.5.1 で設定として復活
        Assert.True(CliParser.Parse(["/chkdel=1"]).ShowDeleteCheckBox);
        Assert.True(CliParser.Parse(["/chkdel"]).ShowDeleteCheckBox);
        Assert.False(CliParser.Parse(["/chkdel=0"]).ShowDeleteCheckBox);
        Assert.Empty(CliParser.Parse(["/chkdel"]).IgnoredOptions);
        Assert.Contains("/chkdel", CliParser.Parse(["/chkdel"]).GuiOnlyOptionsGiven());
        Assert.True(CliParser.Parse(["/chkdel=1"]).HasSettingOverride);
    }

    [Fact]
    public void BeforeTypeOption_IsParsedInsteadOfBeingIgnored()
    {
        // 旧版の /beforetype（前に使ったファイルタイプで暗号化する）は 5.5.1 で設定として復活。
        // 旧版は値に「前回の種類」1〜3 を取ったので、0 以外は ON
        Assert.True(CliParser.Parse(["/beforetype=1"]).UseLastEncryptionFileType);
        Assert.True(CliParser.Parse(["/beforetype=2"]).UseLastEncryptionFileType);
        Assert.True(CliParser.Parse(["/beforetype"]).UseLastEncryptionFileType);
        Assert.False(CliParser.Parse(["/beforetype=0"]).UseLastEncryptionFileType);
        Assert.Empty(CliParser.Parse(["/beforetype"]).IgnoredOptions);
        Assert.Contains("/beforetype", CliParser.Parse(["/beforetype"]).GuiOnlyOptionsGiven());

        var effective = CliParser.Parse(["/beforetype=1"]).ApplyTo(new AttacheCase.Models.AppSettings());
        Assert.True(effective.UseLastEncryptionFileType);

        // 既定は OFF（旧版と同じ。設定画面で選んだ種類を常に使う）
        Assert.False(new AttacheCase.Models.AppSettings().UseLastEncryptionFileType);
    }

    [Fact]
    public void AllPackAndOneBy_AreExclusiveAndRestoreNormal()
    {
        // 「複数ファイルの場合」は旧版と同じ 3 択（通常 / 一つにまとめる / 一つずつ）。既定は通常
        var defaults = new AttacheCase.Models.AppSettings();
        Assert.False(defaults.AllFilePack);
        Assert.False(defaults.EncryptOneByOne);

        var packed = CliParser.Parse(["/allpack=1"]).ApplyTo(new AttacheCase.Models.AppSettings { EncryptOneByOne = true });
        Assert.True(packed.AllFilePack);
        Assert.False(packed.EncryptOneByOne);

        var oneBy = CliParser.Parse(["/oneby=1"]).ApplyTo(new AttacheCase.Models.AppSettings { AllFilePack = true });
        Assert.False(oneBy.AllFilePack);
        Assert.True(oneBy.EncryptOneByOne);

        // /allpack=0 と /oneby=0 は「通常」へ戻す
        var normal = CliParser.Parse(["/allpack=0"]).ApplyTo(new AttacheCase.Models.AppSettings { AllFilePack = true });
        Assert.False(normal.AllFilePack);
        Assert.False(normal.EncryptOneByOne);
        normal = CliParser.Parse(["/oneby=0"]).ApplyTo(new AttacheCase.Models.AppSettings { EncryptOneByOne = true });
        Assert.False(normal.EncryptOneByOne);

        // 両方指定されたら後のものが勝つ
        var last = CliParser.Parse(["/allpack=1", "/oneby=1"]).ApplyTo(new AttacheCase.Models.AppSettings());
        Assert.True(last.EncryptOneByOne);
        Assert.False(last.AllFilePack);
    }
}
