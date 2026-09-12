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
}
