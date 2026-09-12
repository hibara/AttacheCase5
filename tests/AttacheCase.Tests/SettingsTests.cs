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
// 設定の保存・読み込みと、コマンドライン引数による上書きのテスト。
//   - 設定は JSON に保存され、次回起動時に読み戻せること
//   - コマンドライン引数は「複製」に上書きされ、保存された設定を汚さないこと
//----------------------------------------------------------------------

using AttacheCase.Cli;
using AttacheCase.Models;
using AttacheCase.Services;
using AttacheCase.ViewModels;

namespace AttacheCase.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void MissingFile_YieldsDefaults()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("atc-settings").FullName, "settings.json");

        var service = new SettingsService(path);

        Assert.True(service.Current.ConfirmOverwrite);
        Assert.Equal(EncryptionFileTypes.Atc, service.Current.EncryptionFileType);
        Assert.Equal(3, service.Current.MissTypeLimits);
        Assert.Equal(0, service.Current.DeleteMode);

        // セキュリティ上の修正を届けられないほうが危ないので、既定で有効
        Assert.True(service.Current.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void 更新確認の設定を切ると保存されて読み戻せる()
    {
        var directory = Directory.CreateTempSubdirectory("atc-settings").FullName;
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = new SettingsService(path);
            var settings = service.Current.Clone();
            settings.CheckForUpdatesOnStartup = false;
            service.Save(settings);

            Assert.False(new SettingsService(path).Current.CheckForUpdatesOnStartup);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SavedSettings_AreReadBack()
    {
        var directory = Directory.CreateTempSubdirectory("atc-settings").FullName;
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = new SettingsService(path);
            var settings = service.Current.Clone();
            settings.ExitAfterProcess = true;
            settings.ConfirmOverwrite = false;
            settings.DeleteMode = DeleteModes.Wipe;
            settings.MissTypeLimits = 7;
            settings.UseAutoName = true;
            settings.AutoNameFormat = "<filename>_<date:yyyyMMdd><ext>";
            service.Save(settings);

            var reopened = new SettingsService(path);

            Assert.True(reopened.Current.ExitAfterProcess);
            Assert.False(reopened.Current.ConfirmOverwrite);
            Assert.Equal(DeleteModes.Wipe, reopened.Current.DeleteMode);
            Assert.Equal(7, reopened.Current.MissTypeLimits);
            Assert.True(reopened.Current.UseAutoName);
            Assert.Equal("<filename>_<date:yyyyMMdd><ext>", reopened.Current.AutoNameFormat);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void BrokenFile_FallsBackToDefaults()
    {
        var directory = Directory.CreateTempSubdirectory("atc-settings").FullName;
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, "{ this is not json");

            var service = new SettingsService(path);

            Assert.True(service.Current.ConfirmOverwrite);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

public class CommandLineOverrideTests
{
    [Fact]
    public void UnspecifiedOptions_LeaveTheStoredSettingAlone()
    {
        var stored = new AppSettings
        {
            KeepTimeStamp = true,
            DeleteMode = DeleteModes.Trash,
            MissTypeLimits = 5,
            ConfirmOverwrite = false,
        };

        var effective = CliParser.Parse(["/p=secret"]).ApplyTo(stored.Clone());

        Assert.True(effective.KeepTimeStamp);
        Assert.Equal(DeleteModes.Trash, effective.DeleteMode);
        Assert.Equal(5, effective.MissTypeLimits);
        Assert.False(effective.ConfirmOverwrite);
    }

    [Fact]
    public void ExplicitZero_TurnsTheSettingOff()
    {
        var stored = new AppSettings { KeepTimeStamp = true, DeleteMode = DeleteModes.Wipe };

        var effective = CliParser.Parse(["/orgdt=0", "/del=0"]).ApplyTo(stored.Clone());

        Assert.False(effective.KeepTimeStamp);
        Assert.Equal(DeleteModes.None, effective.DeleteMode);
    }

    [Fact]
    public void Overwrite_IsTheInverseOfTheConfirmSetting()
    {
        Assert.False(CliParser.Parse(["/ow=0"]).ApplyTo(new AppSettings()).ConfirmOverwrite);
        Assert.True(CliParser.Parse(["/ow=1"]).ApplyTo(new AppSettings()).ConfirmOverwrite);
        Assert.False(CliParser.Parse(["/y"]).ApplyTo(new AppSettings()).ConfirmOverwrite);
    }

    [Fact]
    public void SaveTo_TurnsOnTheSameFolderSetting()
    {
        var directory = Directory.CreateTempSubdirectory("atc-override").FullName;
        try
        {
            var effective = CliParser.Parse(["/saveto=" + directory]).ApplyTo(new AppSettings());

            Assert.True(effective.SaveToSameFolder);
            Assert.Equal(Path.GetFullPath(directory), effective.SaveToFolderPath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AutoNameAndCamouflage_AreTurnedOnByTheArgument()
    {
        var effective = CliParser.Parse(["/autoname=<filename>_x<ext>", "/camoext=jpg"]).ApplyTo(new AppSettings());

        Assert.True(effective.UseAutoName);
        Assert.Equal("<filename>_x<ext>", effective.AutoNameFormat);
        Assert.True(effective.UseCamouflageExtension);
        Assert.Equal(".jpg", effective.CamouflageExtension);
    }

    [Fact]
    public void EmptyAutoName_TurnsAutoNamingOff()
    {
        var stored = new AppSettings { UseAutoName = true, AutoNameFormat = "<filename><ext>" };

        var effective = CliParser.Parse(["/autoname="]).ApplyTo(stored.Clone());

        Assert.False(effective.UseAutoName);
    }

    [Fact]
    public void TheStoredSettingsAreNotTouched()
    {
        var directory = Directory.CreateTempSubdirectory("atc-override").FullName;
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = new SettingsService(path);
            var stored = service.Current.Clone();
            stored.MissTypeLimits = 4;
            service.Save(stored);

            // コマンドライン起動を模して、複製に対して上書きする
            var effective = CliParser.Parse(["/typelimit=9", "/exit=1"]).ApplyTo(service.Current.Clone());

            Assert.Equal(9, effective.MissTypeLimits);
            Assert.True(effective.ExitAfterProcess);

            // 保存されている設定も、読み直した内容も変わっていないこと
            Assert.Equal(4, service.Current.MissTypeLimits);
            Assert.False(service.Current.ExitAfterProcess);

            var reopened = new SettingsService(path);
            Assert.Equal(4, reopened.Current.MissTypeLimits);
            Assert.False(reopened.Current.ExitAfterProcess);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

public class SettingsViewModelTests
{
    private static (SettingsViewModel vm, SettingsService service, string directory) Create()
    {
        var directory = Directory.CreateTempSubdirectory("atc-settings-vm").FullName;
        var service = new SettingsService(Path.Combine(directory, "settings.json"));
        return (new SettingsViewModel(new MainWindowViewModel(), service), service, directory);
    }

    [Fact]
    public void EditingWithoutSaving_LeavesTheStoredSettingsAlone()
    {
        var (vm, service, directory) = Create();
        try
        {
            vm.ExitAfterProcess = true;
            vm.MissTypeLimitsIndex = 8;   // 9 回
            vm.DeleteMode = DeleteModes.Wipe;

            // 保存していないので、保存されている設定は既定のまま
            Assert.False(service.Current.ExitAfterProcess);
            Assert.Equal(3, service.Current.MissTypeLimits);
            Assert.Equal(DeleteModes.None, service.Current.DeleteMode);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_WritesTheEditedValues()
    {
        var (vm, service, directory) = Create();
        try
        {
            vm.ExitAfterProcess = true;
            vm.IsFileTypeZip = true;
            vm.IsZipAes256 = true;
            vm.IsCompressionOptimal = true;
            vm.MissTypeLimitsIndex = 8;   // 9 回
            vm.ConfirmOverwrite = false;

            vm.SaveCommand.Execute(null);

            Assert.True(service.Current.ExitAfterProcess);
            Assert.Equal(EncryptionFileTypes.Zip, service.Current.EncryptionFileType);
            Assert.Equal(2, service.Current.ZipAlgorithm);
            Assert.Equal(2, service.Current.CompressionLevel);
            Assert.Equal(9, service.Current.MissTypeLimits);
            Assert.False(service.Current.ConfirmOverwrite);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void FileTypeAndCompression_AreMutuallyExclusive()
    {
        var (vm, _, directory) = Create();
        try
        {
            Assert.True(vm.IsFileTypeAtc);

            vm.IsFileTypeZip = true;
            Assert.False(vm.IsFileTypeAtc);

            vm.IsCompressionNone = true;
            Assert.False(vm.IsCompressionFastest);
            Assert.False(vm.IsCompressionOptimal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RestoreDefaults_PutsEveryValueBack()
    {
        var (vm, _, directory) = Create();
        try
        {
            vm.ExitAfterProcess = true;
            vm.ConfirmOverwrite = false;
            vm.DeleteMode = DeleteModes.Trash;

            vm.RestoreDefaultsCommand.Execute(null);

            Assert.False(vm.ExitAfterProcess);
            Assert.True(vm.ConfirmOverwrite);
            Assert.Equal(DeleteModes.None, vm.DeleteMode);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void NumericValues_AreClamped()
    {
        var (vm, _, directory) = Create();
        try
        {
            // プルダウンは 1〜10 回しか選べない
            Assert.Equal(2, vm.MissTypeLimitsIndex);   // 既定 3 回

            vm.MissTypeLimitsIndex = 9;
            Assert.Equal(9, vm.MissTypeLimitsIndex);

            vm.DeleteRandomTimesValue = 5m;
            Assert.Equal(5, vm.DeleteRandomTimes);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

public class SettingsSelectionTests
{
    [Fact]
    public void OpeningSettings_HighlightsTheGearAndClearsTheMenuHighlight()
    {
        var main = new MainWindowViewModel();
        var encryptMenu = main.MenuItems[0];

        Assert.True(encryptMenu.IsSelected);
        Assert.False(main.IsSettingsSelected);

        main.OpenSettingsCommand.Execute(null);

        Assert.True(main.IsSettingsSelected);
        Assert.False(encryptMenu.IsSelected);

        // 設定画面を閉じると元に戻る
        main.BackToMain();

        Assert.False(main.IsSettingsSelected);
        Assert.True(encryptMenu.IsSelected);
    }

    [Fact]
    public void LeavingSettingsByAnotherMenuItem_MovesTheHighlight()
    {
        var main = new MainWindowViewModel();
        var encryptMenu = main.MenuItems[0];
        var zipMenu = main.MenuItems[3];

        main.OpenSettingsCommand.Execute(null);
        Assert.True(main.IsSettingsSelected);

        main.MenuItemClickCommand.Execute(zipMenu);

        Assert.False(main.IsSettingsSelected);
        Assert.True(zipMenu.IsSelected);
        Assert.False(encryptMenu.IsSelected);
    }

    [Fact]
    public void SettingsOpensFromAnotherScreenWhenNotProcessing()
    {
        // 仕様変更（2026-09-08）: 設定は実処理（暗号化・復号・通信）の最中で
        // なければ、どの画面からでも開ける。以前は「メイン画面のときだけ」で、
        // Post-PPAP などの画面から設定へ飛べなかった
        var main = new MainWindowViewModel();
        main.StartZipEncryption([Path.GetTempPath()]); // パスワード入力画面（処理前）

        main.OpenSettingsCommand.Execute(null);

        Assert.True(main.IsSettingsSelected);
    }
}

public class PasswordVisibilityTests
{
    [Fact]
    public void InitialState_FollowsTheSetting()
    {
        var hidden = new PasswordVisibilityViewModel(
            new RunContext { Settings = new AppSettings { NotMaskPassword = false } });
        var shown = new PasswordVisibilityViewModel(
            new RunContext { Settings = new AppSettings { NotMaskPassword = true } });

        Assert.False(hidden.IsVisible);
        Assert.Equal('●', hidden.PasswordChar);

        Assert.True(shown.IsVisible);
        Assert.Equal('\0', shown.PasswordChar);
    }

    [Fact]
    public void Toggling_SwitchesTheMaskAndTheIcon()
    {
        // 保存先はテスト用の場所にする（実際の設定ファイルを書き換えないため）
        var directory = Directory.CreateTempSubdirectory("atc-eye-toggle").FullName;
        var vm = new PasswordVisibilityViewModel(new RunContext
        {
            Settings = new AppSettings(),
            Store = new SettingsService(Path.Combine(directory, "settings.json")),
        });

        vm.ToggleCommand.Execute(null);

        Assert.True(vm.IsVisible);
        Assert.Equal('\0', vm.PasswordChar);

        vm.ToggleCommand.Execute(null);

        Assert.False(vm.IsVisible);
        Assert.Equal('●', vm.PasswordChar);

        Directory.Delete(directory, true);
    }

    [Fact]
    public void OnACommandLineRun_TheToggleIsNotWrittenBack()
    {
        var directory = Directory.CreateTempSubdirectory("atc-eye").FullName;
        var path = Path.Combine(directory, "settings.json");
        try
        {
            // コマンドライン起動を模す（設定の複製を持つ）
            var service = new SettingsService(path);
            var context = new RunContext
            {
                Settings = service.Current.Clone(),
                IsCommandLineRun = true,
            };

            var vm = new PasswordVisibilityViewModel(context);
            vm.ToggleCommand.Execute(null);

            // この実行のあいだは効く
            Assert.True(context.Settings.NotMaskPassword);

            // ファイルには書き戻らない
            Assert.False(File.Exists(path));
            Assert.False(new SettingsService(path).Current.NotMaskPassword);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

public class PasswordVisibilityPersistenceTests
{
    [Fact]
    public void OnAGuiRun_TheToggleIsSavedAndShowsUpInTheSettingsScreen()
    {
        var directory = Directory.CreateTempSubdirectory("atc-eye-gui").FullName;
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new SettingsService(path);
            var context = new RunContext { Settings = store.Current, Store = store };

            Assert.False(store.Current.NotMaskPassword);

            // 入力欄の目玉アイコンで「表示する」に切り替える
            new PasswordVisibilityViewModel(context).ToggleCommand.Execute(null);

            // 保存され、設定画面を開いたときにも反映される
            Assert.True(store.Current.NotMaskPassword);
            Assert.True(new SettingsService(path).Current.NotMaskPassword);
            Assert.True(new SettingsViewModel(new MainWindowViewModel(), store).NotMaskPassword);

            // 次に開いた入力欄も表示状態から始まる
            Assert.True(new PasswordVisibilityViewModel(context).IsVisible);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TheSettingsScreen_DrivesTheInitialStateOfTheInputField()
    {
        var directory = Directory.CreateTempSubdirectory("atc-eye-settings").FullName;
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new SettingsService(path);

            var settingsVm = new SettingsViewModel(new MainWindowViewModel(), store)
            {
                NotMaskPassword = true,
            };
            settingsVm.SaveCommand.Execute(null);

            var context = new RunContext { Settings = store.Current, Store = store };
            Assert.True(new PasswordVisibilityViewModel(context).IsVisible);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

public class AutoNameExampleTests
{
    private static SettingsViewModel Create(string directory) =>
        new(new MainWindowViewModel(), new SettingsService(Path.Combine(directory, "settings.json")));

    [Fact]
    public void TheDefaultFormatIsTheDatedOne()
    {
        Assert.Equal("<filename>_<date:yyyy-MM-dd><ext>", new AppSettings().AutoNameFormat);
    }

    [Fact]
    public void TheExampleFollowsTheFormat()
    {
        var directory = Directory.CreateTempSubdirectory("atc-autoname-vm").FullName;
        try
        {
            var vm = Create(directory);

            vm.AutoNameFormat = "<filename><ext>";
            Assert.Equal("ex). Test.atc", vm.AutoNameExample);

            vm.AutoNameFormat = "<filename>_backup<ext>";
            Assert.Equal("ex). Test_backup.atc", vm.AutoNameExample);

            vm.AutoNameFormat = "<filename>_<date:yyyy-MM-dd><ext>";
            Assert.Equal($"ex). Test_{DateTime.Now:yyyy-MM-dd}.atc", vm.AutoNameExample);

            vm.AutoNameFormat = "<filename>_<num:3><ext>";
            Assert.Equal("ex). Test_001.atc", vm.AutoNameExample);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TheExampleUpdatesWheneverTheFormatChanges()
    {
        var directory = Directory.CreateTempSubdirectory("atc-autoname-vm").FullName;
        try
        {
            var vm = Create(directory);
            var notified = new List<string>();
            vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? "");

            vm.AutoNameFormat = "<filename>-1<ext>";

            Assert.Contains(nameof(SettingsViewModel.AutoNameExample), notified);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ARandomFormatProducesTheRequestedLength()
    {
        var directory = Directory.CreateTempSubdirectory("atc-autoname-vm").FullName;
        try
        {
            var vm = Create(directory);
            vm.AutoNameFormat = "<random:8><ext>";

            var example = vm.AutoNameExample;

            Assert.StartsWith("ex). ", example);
            Assert.EndsWith(".atc", example);
            Assert.Equal("ex). ".Length + 8 + ".atc".Length, example.Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AnEmptyFormatShowsNothing()
    {
        var directory = Directory.CreateTempSubdirectory("atc-autoname-vm").FullName;
        try
        {
            var vm = Create(directory);
            vm.AutoNameFormat = "";

            Assert.Equal(string.Empty, vm.AutoNameExample);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

public class AutoNameRandomCharsetTests
{
    /// <summary>Windows と macOS のどちらかでファイル名に使えない文字</summary>
    private static readonly char[] ForbiddenInFileNames = ['\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0'];

    private static SettingsViewModel Create(string directory) =>
        new(new MainWindowViewModel(), new SettingsService(Path.Combine(directory, "settings.json")));

    [Fact]
    public void TheDefaultIsLettersAndNumbersWithoutSymbols()
    {
        var settings = new AppSettings();

        Assert.True(settings.AutoNameRandomUpperCase);
        Assert.True(settings.AutoNameRandomLowerCase);
        Assert.True(settings.AutoNameRandomNumbers);
        Assert.False(settings.AutoNameRandomSymbols);
    }

    [Fact]
    public void OnlyTheCheckedKindsAreUsed()
    {
        var settings = new AppSettings
        {
            AutoNameRandomUpperCase = false,
            AutoNameRandomLowerCase = false,
            AutoNameRandomNumbers = true,
            AutoNameRandomSymbols = false,
        };

        var generated = AutoFileName.Format(
            "<random:64>", Path.Combine(Path.GetTempPath(), "Test.atc"),
            AutoFileName.CharsetFrom(settings), out _);

        Assert.Equal(64, generated.Length);
        Assert.All(generated, c => Assert.True(char.IsAsciiDigit(c), $"'{c}' is not a digit"));
    }

    [Fact]
    public void SymbolsNeverContainCharactersForbiddenInFileNames()
    {
        var settings = new AppSettings
        {
            AutoNameRandomUpperCase = false,
            AutoNameRandomLowerCase = false,
            AutoNameRandomNumbers = false,
            AutoNameRandomSymbols = true,
        };

        // 乱数なので、十分な回数を回して 1 文字も禁止文字が出ないことを見る
        for (var i = 0; i < 200; i++)
        {
            var generated = AutoFileName.Format(
                "<random:64>", Path.Combine(Path.GetTempPath(), "Test.atc"),
                AutoFileName.CharsetFrom(settings), out var containsInvalidChar);

            Assert.False(containsInvalidChar);
            Assert.Equal(-1, generated.IndexOfAny(ForbiddenInFileNames));
        }
    }

    [Fact]
    public void UncheckingEverythingStillProducesAName()
    {
        var settings = new AppSettings
        {
            AutoNameRandomUpperCase = false,
            AutoNameRandomLowerCase = false,
            AutoNameRandomNumbers = false,
            AutoNameRandomSymbols = false,
        };

        var generated = AutoFileName.Format(
            "<random:8>", Path.Combine(Path.GetTempPath(), "Test.atc"),
            AutoFileName.CharsetFrom(settings), out _);

        Assert.Equal(8, generated.Length);
        Assert.All(generated, c => Assert.True(char.IsAsciiLetterUpper(c)));
    }

    [Fact]
    public void ChangingTheCharsetRefreshesTheExample()
    {
        var directory = Directory.CreateTempSubdirectory("atc-charset-vm").FullName;
        try
        {
            var vm = Create(directory);
            vm.UseAutoName = true;
            vm.AutoNameFormat = "<random:32><ext>";

            var notified = new List<string>();
            vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? "");

            vm.AutoNameRandomUpperCase = false;
            vm.AutoNameRandomLowerCase = false;
            vm.AutoNameRandomSymbols = false;

            Assert.Contains(nameof(SettingsViewModel.AutoNameExample), notified);

            // 数字だけが残っている状態の例になっている
            var example = vm.AutoNameExample.Replace("ex). ", "").Replace(".atc", "");
            Assert.All(example, c => Assert.True(char.IsAsciiDigit(c)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TheCharsetIsSaved()
    {
        var directory = Directory.CreateTempSubdirectory("atc-charset-save").FullName;
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = new SettingsService(path);
            var vm = new SettingsViewModel(new MainWindowViewModel(), service)
            {
                AutoNameRandomSymbols = true,
                AutoNameRandomNumbers = false,
            };
            vm.SaveCommand.Execute(null);

            var reopened = new SettingsService(path);
            Assert.True(reopened.Current.AutoNameRandomSymbols);
            Assert.False(reopened.Current.AutoNameRandomNumbers);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
