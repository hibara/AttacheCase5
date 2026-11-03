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
// 複数入力を暗号化の処理単位に分ける規則（EncryptionJobPlan）のテスト。
// GUI（ATC / パスワード ZIP）と CLI が共有する。
//----------------------------------------------------------------------

using AttacheCase.Cli;
using AttacheCase.Models;

namespace AttacheCase.Tests;

public class EncryptionJobPlanTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("atc-jobs").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string File_(string relative, string content = "x")
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static RunContext Ctx(AppSettings? settings = null, string? outputPath = null) => new()
    {
        Settings = settings ?? new AppSettings(),
        OutputPath = outputPath,
    };

    private static List<EncryptionJob> Plan(IEnumerable<string> inputs, RunContext ctx)
        => EncryptionJobPlan.Build(inputs, ctx, paths => CliOutputPath.ForEncryption(paths, ctx));

    [Fact]
    public void Normal_OneJobPerInput_WithUniqueOutputNames()
    {
        // report.txt と report.pdf は拡張子を外すとどちらも report.atc になる → 2 つ目は連番
        var txt = File_("report.txt");
        var pdf = File_("report.pdf");

        var jobs = Plan([txt, pdf], Ctx());

        Assert.Equal(2, jobs.Count);
        Assert.Equal(Path.Combine(_dir, "report.atc"), jobs[0].OutputPath);
        Assert.Equal(Path.Combine(_dir, "report_2.atc"), jobs[1].OutputPath);
        Assert.Equal([txt], jobs[0].InputPaths);
        Assert.Equal([pdf], jobs[1].InputPaths);
    }

    [Fact]
    public void Normal_NeverWritesOntoAnotherInput()
    {
        // report.txt の出力先 report.atc が、まだ処理していない入力 report.atc と同じになる
        var txt = File_("report.txt");
        var atc = File_("report.atc");

        var jobs = Plan([txt, atc], Ctx());

        var outputs = jobs.Select(j => j.OutputPath).ToList();
        Assert.DoesNotContain(txt, outputs);
        Assert.DoesNotContain(atc, outputs);
        Assert.Equal(outputs.Count, outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Normal_SameNameInDifferentFolders_CollectedIntoOneFolder()
    {
        var a = File_(Path.Combine("a", "data.xlsx"));
        var b = File_(Path.Combine("b", "data.xlsx"));
        var saveTo = Path.Combine(_dir, "out");
        Directory.CreateDirectory(saveTo);
        var settings = new AppSettings { SaveToSameFolder = true, SaveToFolderPath = saveTo };

        var jobs = Plan([a, b], Ctx(settings));

        Assert.Equal(Path.Combine(saveTo, "data.atc"), jobs[0].OutputPath);
        Assert.Equal(Path.Combine(saveTo, "data_2.atc"), jobs[1].OutputPath);
    }

    [Fact]
    public void Normal_FolderIsOneJob_AndNonexistentInputsAreDropped()
    {
        File_(Path.Combine("folder", "inner.txt"));
        var folder = Path.Combine(_dir, "folder");
        var missing = Path.Combine(_dir, "missing.txt");

        var jobs = Plan([folder, missing], Ctx());

        Assert.Single(jobs);
        Assert.Equal([folder], jobs[0].InputPaths);
        Assert.Equal(Path.Combine(_dir, "folder.atc"), jobs[0].OutputPath);
    }

    [Fact]
    public void AllFilePack_PutsEverythingInOneJob()
    {
        var a = File_("a.txt");
        var b = File_("b.txt");

        var jobs = Plan([a, b], Ctx(new AppSettings { AllFilePack = true }));

        Assert.Single(jobs);
        Assert.Equal([a, b], jobs[0].InputPaths);
        Assert.Equal(Path.Combine(_dir, "a.atc"), jobs[0].OutputPath);
    }

    [Fact]
    public void Normal_WithSingleOutputFile_PacksLikeTheOld50Cli()
    {
        // 旧 5.0 CLI の契約（回帰 F-03）: 複数入力 + /out=<ファイル名> は通常モードでも 1 本にまとめる
        var a = File_("a.txt");
        var b = File_("b.txt");
        var packed = Path.Combine(_dir, "packed.atc");

        var jobs = Plan([a, b], Ctx(outputPath: packed));

        Assert.Single(jobs);
        Assert.Equal([a, b], jobs[0].InputPaths);
        Assert.Equal(packed, jobs[0].OutputPath);
    }

    [Fact]
    public void OneByOne_ExpandsFolders_AndRejectsASingleOutputFile()
    {
        File_(Path.Combine("folder", "1.txt"));
        File_(Path.Combine("folder", "sub", "2.txt"));
        var folder = Path.Combine(_dir, "folder");
        var settings = new AppSettings { EncryptOneByOne = true };

        var jobs = Plan([folder], Ctx(settings));

        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Single(j.InputPaths));
        Assert.Contains(Path.Combine(folder, "1.atc"), jobs.Select(j => j.OutputPath));
        Assert.Contains(Path.Combine(folder, "sub", "2.atc"), jobs.Select(j => j.OutputPath));

        Assert.Throws<InvalidOperationException>(
            () => Plan([folder], Ctx(settings, Path.Combine(_dir, "one.atc"))));
    }

    [Fact]
    public void SingleJob_NeverWritesOntoItsOwnInput()
    {
        // 偽装拡張子に入力と同じ拡張子を指定すると photo.jpg → photo.jpg になる。暗黙の出力先なので連番で避ける
        var photo = File_("photo.jpg");
        var settings = new AppSettings { UseCamouflageExtension = true, CamouflageExtension = ".jpg" };

        var jobs = Plan([photo], Ctx(settings));

        Assert.Single(jobs);
        Assert.Equal(Path.Combine(_dir, "photo_2.jpg"), jobs[0].OutputPath);

        // まとめるときも同じ（a.txt + photo.jpg を 1 本にして偽装 .txt → a.txt は入力）
        var a = File_("a.txt");
        var packed = Plan([a, photo], Ctx(new AppSettings
        {
            AllFilePack = true, UseCamouflageExtension = true, CamouflageExtension = ".txt",
        }));
        Assert.Equal(Path.Combine(_dir, "a_2.txt"), packed[0].OutputPath);
    }

    [Fact]
    public void SingleJob_RejectsAnExplicitOutputThatIsAnInput()
    {
        // /out=<入力ファイル> は矛盾。黙って別名にせずエラーにする（連番にすると利用者の指定と違うものができる）
        var a = File_("a.txt");
        var b = File_("b.txt");

        var ex = Assert.Throws<InvalidOperationException>(() => Plan([a, b], Ctx(outputPath: b)));
        Assert.Contains(b, ex.Message);

        // 違うファイルを明示していれば通る
        var jobs = Plan([a, b], Ctx(outputPath: Path.Combine(_dir, "c.atc")));
        Assert.Equal(Path.Combine(_dir, "c.atc"), jobs[0].OutputPath);
    }

    [Fact]
    public void Folder_ExistingFilesInsideAreTreatedAsInput()
    {
        // 入力フォルダーの中にある既存ファイルは入力の一部。/out でそこを指すのは矛盾、暗黙の出力先なら連番で避ける
        var secret = File_(Path.Combine("folder", "secret.atc"));
        File_(Path.Combine("folder", "folder.atc"));
        var folder = Path.Combine(_dir, "folder");

        var ex = Assert.Throws<InvalidOperationException>(() => Plan([folder], Ctx(outputPath: secret)));
        Assert.Contains(secret, ex.Message);

        // 保存先がその入力フォルダー自身で folder.atc が既にある → folder_2.atc
        var settings = new AppSettings { SaveToSameFolder = true, SaveToFolderPath = folder };
        var jobs = Plan([folder], Ctx(settings));
        Assert.Equal(Path.Combine(folder, "folder_2.atc"), jobs[0].OutputPath);

        // まだ無いファイル名なら、入力フォルダーの中でもそのまま使える（/out の明示も通る）
        var fresh = Path.Combine(folder, "fresh.atc");
        Assert.Equal(fresh, Plan([folder], Ctx(outputPath: fresh))[0].OutputPath);

        // 通常モードで「フォルダー」と「その中のファイル」を同時に入力したときも、中の既存ファイルには書かない
        var inner = File_(Path.Combine("folder", "report.txt"));
        File_(Path.Combine("folder", "report.atc"));
        var both = Plan([folder, inner], Ctx());
        Assert.Equal(Path.Combine(folder, "report_2.atc"), both[1].OutputPath);
    }

    [Fact]
    public void Reservation_RootFolderInput_StillProtectsFilesBeneathIt()
    {
        // ルート（C:\ や /）は末尾に区切りが残るので、重ねて付けると C:\\ になって配下が前方一致しなくなる
        var root = Path.GetPathRoot(_dir)!;
        var existing = File_("under-root.atc");

        var reservation = new OutputPathReservation();
        reservation.Reserve([root]);
        Assert.NotEqual(existing, reservation.MakeUnique(existing));

        Assert.Throws<InvalidOperationException>(
            () => EncryptionJobPlan.ResolveSingleOutput([root], existing, outputIsExplicitFile: true));
    }

    [Fact]
    public void Reservation_NumbersDuplicates_IgnoringCaseOnWindowsAndMac()
    {
        var reservation = new OutputPathReservation();
        var first = Path.Combine(_dir, "Report.atc");

        Assert.Equal(first, reservation.MakeUnique(first));
        Assert.Equal(Path.Combine(_dir, "Report_2.atc"), reservation.MakeUnique(first));

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Equal(Path.Combine(_dir, "report_3.atc"), reservation.MakeUnique(Path.Combine(_dir, "report.atc")));
        }
    }
}
