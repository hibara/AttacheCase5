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
// 設定・引数がエンジンまで届くことの検査（2026-09-14 の監査で見つかった欠落の再発防止）。
//   - /breakfile が HeadlessRunner を通ってヘッダに記録される
//   - 「タイムスタンプを元ファイルに合わせる」が .atc の日時に反映される
//   - /exeout・/entype=2 が設定の「種類」（EXE）に写像される
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using AttacheCase.Cli;
using AttacheCase.Headless;
using AttacheCase.Models;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class CliSettingsWiringTests
{
    [Fact]
    public void HeadlessRunner_PassesBreakFileToTheHeader()
    {
        var directory = Directory.CreateTempSubdirectory("atc-wiring").FullName;
        try
        {
            var source = Path.Combine(directory, "source.txt");
            File.WriteAllText(source, "break via headless");
            var output = Path.Combine(directory, "source.atc");

            var result = new HeadlessRunner().Encrypt(new HeadlessEncryptRequest
            {
                InputPaths = [source],
                OutputFilePath = output,
                Password = "pw",
                MissTypeLimits = 5,
                BreakFileWhenLimitExceeded = true,
            });

            Assert.True(result.Succeeded, result.ErrorMessage);

            var (limit, breakOnLimit) = DecryptionService.ReadPasswordLimit(output);
            Assert.Equal(5, limit);
            Assert.True(breakOnLimit);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task KeepTimeStamp_CopiesTheSourceTimesToTheArchive()
    {
        var directory = Directory.CreateTempSubdirectory("atc-wiring").FullName;
        try
        {
            var source = Path.Combine(directory, "old.txt");
            File.WriteAllText(source, "old file");
            var oldTime = new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(source, oldTime);

            var kept = Path.Combine(directory, "kept.atc");
            var keptResult = await new EncryptionService().EncryptAsync(
                [source], kept, "pw", new EncryptionOptions { KeepTimeStamp = true });
            Assert.True(keptResult.Success, keptResult.ErrorMessage);
            Assert.Equal(oldTime, File.GetLastWriteTimeUtc(kept));

            var fresh = Path.Combine(directory, "fresh.atc");
            var freshResult = await new EncryptionService().EncryptAsync(
                [source], fresh, "pw", new EncryptionOptions { KeepTimeStamp = false });
            Assert.True(freshResult.Success, freshResult.ErrorMessage);
            Assert.True(File.GetLastWriteTimeUtc(fresh) > oldTime.AddYears(1));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void HeadlessRunner_PassesKeepTimeStamp()
    {
        var directory = Directory.CreateTempSubdirectory("atc-wiring").FullName;
        try
        {
            var source = Path.Combine(directory, "old.txt");
            File.WriteAllText(source, "old file");
            var oldTime = new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(source, oldTime);
            var output = Path.Combine(directory, "old.atc");

            var result = new HeadlessRunner().Encrypt(new HeadlessEncryptRequest
            {
                InputPaths = [source],
                OutputFilePath = output,
                Password = "pw",
                KeepTimeStamp = true,
            });

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(oldTime, File.GetLastWriteTimeUtc(output));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SelfExecutable_MapsToTheFileTypeSetting()
    {
        // /exeout=1 相当: 設定の種類が EXE になる（GUI 経路の EncryptionViewModel はこれを見る）
        var on = new CliOptions { SelfExecutableOption = true }.ApplyTo(new AppSettings());
        Assert.Equal(EncryptionFileTypes.Exe, on.EncryptionFileType);

        // /entype=2 相当（種類 ATC + 自己実行形式）も EXE に落ち着く
        var byType = new CliOptions { EncryptionType = CliEncryptionType.Atc, SelfExecutableOption = true }
            .ApplyTo(new AppSettings());
        Assert.Equal(EncryptionFileTypes.Exe, byType.EncryptionFileType);

        // /exeout=0 は、設定で EXE が選ばれていても .atc に戻す
        var off = new CliOptions { SelfExecutableOption = false }
            .ApplyTo(new AppSettings { EncryptionFileType = EncryptionFileTypes.Exe });
        Assert.Equal(EncryptionFileTypes.Atc, off.EncryptionFileType);

        // 指定が無ければ設定のまま
        var untouched = new CliOptions().ApplyTo(new AppSettings { EncryptionFileType = EncryptionFileTypes.Exe });
        Assert.Equal(EncryptionFileTypes.Exe, untouched.EncryptionFileType);
    }

    [Fact]
    public void GuiOnlyOptions_AreListedForTheNotice()
    {
        var given = CliParser.Parse(["/opf=1", "/exit", "/front=0"]).GuiOnlyOptionsGiven();
        Assert.Equal(["/exit", "/opf", "/front"], given);

        Assert.Empty(CliParser.Parse(["/orgdt=1", "/del=1"]).GuiOnlyOptionsGiven());
    }

    [Fact]
    public void ApplyTo_LeavesUnspecifiedItemsAsSaved()
    {
        // 設定画面の内容が既定になり、引数で触った項目だけが変わる
        var saved = new AppSettings
        {
            KeepTimeStamp = true,
            DeleteMode = DeleteModes.Wipe,
            SaveToSameFolder = true,
            SaveToFolderPath = "/tmp/saved",
        };

        var effective = CliParser.Parse(["/del=0"]).ApplyTo(saved.Clone());

        Assert.True(effective.KeepTimeStamp);
        Assert.Equal(DeleteModes.None, effective.DeleteMode);
        Assert.True(effective.SaveToSameFolder);
        Assert.Equal("/tmp/saved", effective.SaveToFolderPath);

        // 複製に対して上書きしているので、元の設定は変わらない
        Assert.Equal(DeleteModes.Wipe, saved.DeleteMode);
    }
}
