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
// 設定画面。
//
// 編集対象は「保存されている設定の複製」。保存を押したときだけ
// settings.json に書き戻す。コマンドライン引数で上書きされた値は
// この画面には入ってこない（その実行にだけ効くもののため）。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using AttacheCase.Cli;
using AttacheCase.Models;
using AttacheCase.Services;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;

namespace AttacheCase.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainViewModel;
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localize = LocalizationService.Instance;

    private AppSettings _settings;

    public SettingsViewModel(MainWindowViewModel mainViewModel, SettingsService? settingsService = null)
    {
        _mainViewModel = mainViewModel;
        _settingsService = settingsService ?? SettingsService.Instance;

        // 保存されている設定の複製を編集する（保存を押すまで実際の設定は変わらない）
        _settings = _settingsService.Current.Clone();

        // 関連付けアイコンは「実際に登録されているもの」を初期値にする。
        // settings.json の値だけだと、選んで「関連付けする」を押したが「保存」は
        // 押していないときに、次回この画面が別のアイコンを選択状態にしてしまう
        if (OperatingSystem.IsWindows())
        {
            var registered = Platform.WindowsFileAssociation.ReadRegisteredAtcIconIndex();
            if (registered is int n && n != _settings.AssociationIconIndex)
            {
                _settings.AssociationIconIndex = n;
            }
        }

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => _mainViewModel.BackToMain());
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        BrowseSaveToFolderCommand = new AsyncRelayCommand(
            () => BrowseFolderAsync(p => SaveToFolderPath = p));
        BrowseZipSaveToFolderCommand = new AsyncRelayCommand(
            () => BrowseFolderAsync(p => ZipSaveToFolderPath = p));
        BrowseDecryptSaveToFolderCommand = new AsyncRelayCommand(
            () => BrowseFolderAsync(p => DecryptSaveToFolderPath = p));
        BrowseEncryptPasswordFileCommand = new AsyncRelayCommand(
            () => BrowseFileAsync(p => EncryptPasswordFilePath = p));
        BrowseDecryptPasswordFileCommand = new AsyncRelayCommand(
            () => BrowseFileAsync(p => DecryptPasswordFilePath = p));
        AssociateFilesCommand = new AsyncRelayCommand(() => LaunchAtcSetupAsync(associate: true));
        UnassociateFilesCommand = new AsyncRelayCommand(() => LaunchAtcSetupAsync(associate: false));

        RegisterDeviceWithCodeCommand = new AsyncRelayCommand(RegisterDeviceWithCodeAsync);
        ClearRememberedPasswordsCommand = new AsyncRelayCommand(ClearRememberedPasswordsAsync);
        IssueMigrationCodeCommand = new AsyncRelayCommand(IssueMigrationCodeAsync);
        CreatePortableCommand = new AsyncRelayCommand(CreatePortableAsync);
        RemovePortableCommand = new AsyncRelayCommand(RemovePortableAsync);

        LoadRememberedPasswords();
    }

    #region Commands

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RestoreDefaultsCommand { get; }
    public ICommand BrowseSaveToFolderCommand { get; }
    public ICommand BrowseZipSaveToFolderCommand { get; }
    public ICommand BrowseDecryptSaveToFolderCommand { get; }
    public ICommand BrowseEncryptPasswordFileCommand { get; }
    public ICommand BrowseDecryptPasswordFileCommand { get; }
    public ICommand AssociateFilesCommand { get; }
    public ICommand UnassociateFilesCommand { get; }
    public ICommand RegisterDeviceWithCodeCommand { get; }
    public ICommand ClearRememberedPasswordsCommand { get; }
    public ICommand IssueMigrationCodeCommand { get; }
    public ICommand CreatePortableCommand { get; }
    public ICommand RemovePortableCommand { get; }

    #endregion

    #region パスワードの記憶

    private readonly RememberedPasswordService _rememberedPasswords = RememberedPasswordService.Instance;

    private string _rememberedEncryptPassword = string.Empty;
    private string _rememberedDecryptPassword = string.Empty;
    private string _migrationCodeInput = string.Empty;
    private string _issuedMigrationCode = string.Empty;

    /// <summary>
    /// 画面を開いた時点の保管庫のパスワードを入力欄へ読み込む。
    /// この端末で開けない（Locked）ときは欄を出さず、移行コードの入力を促す。
    /// </summary>
    private void LoadRememberedPasswords()
    {
        if (_rememberedPasswords.GetState() != RememberedPasswordState.Unlocked) return;

        _rememberedEncryptPassword =
            _rememberedPasswords.GetPassword(RememberedPasswordService.KeyEncryptPassword) ?? string.Empty;
        _rememberedDecryptPassword =
            _rememberedPasswords.GetPassword(RememberedPasswordService.KeyDecryptPassword) ?? string.Empty;
    }

    /// <summary>保管庫はあるが、この端末ではまだ開けない（移行コードが要る）。</summary>
    public bool IsVaultLocked => _rememberedPasswords.GetState() == RememberedPasswordState.Locked;

    /// <summary>パスワード入力欄を出せる状態か（Locked でなければ出す）。</summary>
    public bool CanEditRememberedPasswords => !IsVaultLocked;

    public bool RememberEncryptPassword
    {
        get => _settings.RememberEncryptPassword;
        set => SetSetting(v => _settings.RememberEncryptPassword = v, _settings.RememberEncryptPassword, value);
    }

    public bool RememberDecryptPassword
    {
        get => _settings.RememberDecryptPassword;
        set => SetSetting(v => _settings.RememberDecryptPassword = v, _settings.RememberDecryptPassword, value);
    }

    public string RememberedEncryptPassword
    {
        get => _rememberedEncryptPassword;
        set => SetProperty(ref _rememberedEncryptPassword, value);
    }

    public string RememberedDecryptPassword
    {
        get => _rememberedDecryptPassword;
        set => SetProperty(ref _rememberedDecryptPassword, value);
    }

    public string MigrationCodeInput
    {
        get => _migrationCodeInput;
        set => SetProperty(ref _migrationCodeInput, value);
    }

    /// <summary>発行した移行コード（表示用。この画面でしか見られない）。</summary>
    public string IssuedMigrationCode
    {
        get => _issuedMigrationCode;
        private set
        {
            if (SetProperty(ref _issuedMigrationCode, value))
            {
                OnPropertyChanged(nameof(HasIssuedMigrationCode));
            }
        }
    }

    public bool HasIssuedMigrationCode => _issuedMigrationCode.Length > 0;

    /// <summary>
    /// 「保存」の中で呼ばれる。フラグが立っている項目だけ保管庫へ書き、
    /// 外れている項目は保管庫からも消す。Locked のときは触らない。
    /// </summary>
    private void ApplyRememberedPasswords()
    {
        if (IsVaultLocked) return;

        try
        {
            _rememberedPasswords.SetPasswords(
                _settings.RememberEncryptPassword ? _rememberedEncryptPassword : null,
                _settings.RememberDecryptPassword ? _rememberedDecryptPassword : null);
        }
        catch (Exception ex)
        {
            // 保管庫に書けなくても、設定そのものの保存は続ける
            System.Diagnostics.Debug.WriteLine("記憶パスワードの保存に失敗: " + ex.Message);
        }
    }

    /// <summary>移行コードを入力して、この端末を信頼済みに登録する。</summary>
    private async Task RegisterDeviceWithCodeAsync()
    {
        var ok = false;
        try
        {
            ok = _rememberedPasswords.RegisterThisDeviceWithCode(MigrationCodeInput);
        }
        catch (Exception)
        {
            // 書き込み失敗なども「登録できませんでした」に含める
        }

        if (!ok)
        {
            await DialogHelper.NotifyAsync(
                unattended: false,
                _localize["Dialog_ErrorTitle"],
                _localize["Settings_RegisterDeviceFailed"]);
            return;
        }

        MigrationCodeInput = string.Empty;
        LoadRememberedPasswords();
        OnPropertyChanged(nameof(IsVaultLocked));
        OnPropertyChanged(nameof(CanEditRememberedPasswords));
        OnPropertyChanged(nameof(RememberedEncryptPassword));
        OnPropertyChanged(nameof(RememberedDecryptPassword));

        await DialogHelper.NotifyAsync(
            unattended: false,
            _localize["Dialog_InfoTitle"],
            _localize["Settings_RegisterDeviceDone"],
            DialogIcon.Info);
    }

    /// <summary>記憶した内容（保管庫ファイルごと）をすべて消す。</summary>
    private async Task ClearRememberedPasswordsAsync()
    {
        var confirmed = await DialogHelper.ConfirmAsync(
            unattended: false,
            _localize["Dialog_ConfirmTitle"],
            _localize["Settings_RememberClearConfirm"]);
        if (!confirmed) return;

        _rememberedPasswords.DeleteAll();

        RememberedEncryptPassword = string.Empty;
        RememberedDecryptPassword = string.Empty;
        RememberEncryptPassword = false;
        RememberDecryptPassword = false;
        IssuedMigrationCode = string.Empty;
        OnPropertyChanged(nameof(IsVaultLocked));
        OnPropertyChanged(nameof(CanEditRememberedPasswords));
    }

    /// <summary>
    /// 他の PC 用の移行コードを発行する。既存のコードは無効になり、
    /// 使われた時点でも無効になる（使い捨て）。
    /// </summary>
    private async Task IssueMigrationCodeAsync()
    {
        // 発行には DEK が要る。保管庫が無い（記憶パスワード未使用）か、
        // この端末で開けないときは発行できない
        string? code = null;
        try
        {
            code = _rememberedPasswords.IssueMigrationCode();
        }
        catch (Exception)
        {
            // 書き込み失敗も「発行できません」に含める
        }

        if (code == null)
        {
            await DialogHelper.NotifyAsync(
                unattended: false,
                _localize["Dialog_ErrorTitle"],
                _localize["Settings_IssueCodeNeedUnlocked"]);
            return;
        }

        IssuedMigrationCode = code;
    }

    #endregion

    #region 設定の持ち運び（ポータブル設定）

    /// <summary>ポータブル設定を使用中か（次回起動から有効になる分は含まない）。</summary>
    public bool IsPortableMode => _settingsService.IsPortableMode;

    public string PortableStatusText
    {
        get
        {
            if (_settingsService.IsPortableMode && _settingsService.PortableFilePath != null)
            {
                return _localize.GetString("Settings_PortableStatusInUse", _settingsService.PortableFilePath);
            }

            var found = Settings.PortableSettings.Find();
            return found != null
                ? _localize.GetString("Settings_PortableStatusPending", found)
                : _localize["Settings_PortableStatusNormal"];
        }
    }

    /// <summary>現在の設定を _AtcCase.json として実行ファイルの隣へ書き出す。</summary>
    private async Task CreatePortableAsync()
    {
        try
        {
            var path = _settingsService.CreatePortableCopy();
            OnPropertyChanged(nameof(PortableStatusText));

            await DialogHelper.NotifyAsync(
                unattended: false,
                _localize["Dialog_InfoTitle"],
                _localize.GetString("Settings_PortableCreated", path),
                DialogIcon.Info);
        }
        catch (Exception ex)
        {
            await DialogHelper.NotifyAsync(
                unattended: false,
                _localize["Dialog_ErrorTitle"],
                _localize["Settings_PortableCreateFailed"] + Environment.NewLine + ex.Message);
        }
    }

    /// <summary>ポータブル設定（と持ち運び用の保管庫の複製）を削除する。</summary>
    private async Task RemovePortableAsync()
    {
        var confirmed = await DialogHelper.ConfirmAsync(
            unattended: false,
            _localize["Dialog_ConfirmTitle"],
            _localize["Settings_PortableRemoveConfirm"]);
        if (!confirmed) return;

        if (_settingsService.RemovePortableCopy())
        {
            OnPropertyChanged(nameof(PortableStatusText));
            await DialogHelper.NotifyAsync(
                unattended: false,
                _localize["Dialog_InfoTitle"],
                _localize["Settings_PortableRemoved"],
                DialogIcon.Info);
        }
        else
        {
            await DialogHelper.NotifyAsync(
                unattended: false,
                _localize["Dialog_ErrorTitle"],
                _localize["Settings_PortableRemoveFailed"]);
        }
    }

    #endregion

    #region 関連付け（Windows のみ）

    /// <summary>関連付けセクションを表示するか（Windows のみ）</summary>
    public bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>関連付けアイコン（.atc）。1〜4 = 同梱の Icons\atc1.ico〜atc4.ico</summary>
    public int AssociationIconIndex
    {
        get => Math.Clamp(_settings.AssociationIconIndex, 1, 4);
        set
        {
            var clamped = Math.Clamp(value, 1, 4);
            if (AssociationIconIndex == clamped) return;

            _settings.AssociationIconIndex = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAssociationIcon1));
            OnPropertyChanged(nameof(IsAssociationIcon2));
            OnPropertyChanged(nameof(IsAssociationIcon3));
            OnPropertyChanged(nameof(IsAssociationIcon4));
        }
    }

    // RadioButton との受け渡し用（XAML の TwoWay バインド先）
    public bool IsAssociationIcon1
    {
        get => AssociationIconIndex == 1;
        set { if (value) AssociationIconIndex = 1; }
    }

    public bool IsAssociationIcon2
    {
        get => AssociationIconIndex == 2;
        set { if (value) AssociationIconIndex = 2; }
    }

    public bool IsAssociationIcon3
    {
        get => AssociationIconIndex == 3;
        set { if (value) AssociationIconIndex = 3; }
    }

    public bool IsAssociationIcon4
    {
        get => AssociationIconIndex == 4;
        set { if (value) AssociationIconIndex = 4; }
    }

    /// <summary>
    /// 昇格ヘルパー AtcSetup.exe を起動して .atc などの関連付けを設定・解除する。
    /// 本体は昇格せず、UAC を出すのはこの小さなツールだけ（Windows 版と同じ流儀）。
    /// </summary>
    private async Task LaunchAtcSetupAsync(bool associate)
    {
        if (!OperatingSystem.IsWindows()) return;

        var setupPath = Path.Combine(AppContext.BaseDirectory, "AtcSetup", "AtcSetup.exe");
        if (!File.Exists(setupPath))
        {
            await DialogHelper.NotifyAsync(
                unattended: false,
                _localize["Dialog_ErrorTitle"],
                _localize["Error_AtcSetupNotFound"] + Environment.NewLine + setupPath);
            return;
        }

        // -p で本体のパスを明示する（AtcSetup 単体のフォールバックは旧版のレジストリを
        // 見るため、統合版では必ず渡す）。
        // アイコンは同梱の .ico のパスで渡す（旧版は本体 exe 内のアイコン番号だったが、
        // 統合版の exe はアイコンを 1 つしか持たない）。「保存」前でも、この画面で
        // 選ばれている見たままのアイコンを適用する
        var appPath = Path.Combine(AppContext.BaseDirectory, "AttacheCase.exe");
        var iconsDir = Path.Combine(AppContext.BaseDirectory, "Icons");
        var atcIcon = Path.Combine(iconsDir, $"atc{AssociationIconIndex}.ico");
        var pvtIcon = Path.Combine(iconsDir, "atcpvt.ico");
        var pubIcon = Path.Combine(iconsDir, "atcpub.ico");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = associate
                ? $"-t=0 \"-p={appPath}\" \"-icn={atcIcon}\" \"-pvticn={pvtIcon}\" \"-pubicn={pubIcon}\""
                : "-t=1",
            Verb = "runas",
            UseShellExecute = true, // .NET (Core) の既定は false。runas には true が要る
        };

        System.Diagnostics.Process? process;
        try
        {
            process = System.Diagnostics.Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC で「いいえ」を選んだ場合など。何もしない（Windows 版と同じ）
            return;
        }

        if (!associate || process == null) return;

        // 関連付けが済んだら、選んだアイコンを「保存」を待たずに書き留める。
        // 実際にレジストリへ入ったアイコンを読み直して一致したときだけ保存する
        //（UAC を断られた・AtcSetup が失敗したときは何も変えない）
        try
        {
            await process.WaitForExitAsync();
        }
        catch (Exception)
        {
            return;
        }

        var registered = Platform.WindowsFileAssociation.ReadRegisteredAtcIconIndex();
        if (registered != AssociationIconIndex) return;
        if (_settingsService.Current.AssociationIconIndex == AssociationIconIndex) return;

        var live = _settingsService.Current.Clone();
        live.AssociationIconIndex = AssociationIconIndex;
        _settingsService.Save(live);
    }

    #endregion

    #region 一般

    public bool ExitAfterProcess
    {
        get => _settings.ExitAfterProcess;
        set => SetSetting(v => _settings.ExitAfterProcess = v, _settings.ExitAfterProcess, value);
    }

    public bool OpenDecryptedFile
    {
        get => _settings.OpenDecryptedFile;
        set => SetSetting(v => _settings.OpenDecryptedFile = v, _settings.OpenDecryptedFile, value);
    }

    public bool NotMaskPassword
    {
        get => _settings.NotMaskPassword;
        set => SetSetting(v => _settings.NotMaskPassword = v, _settings.NotMaskPassword, value);
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _settings.CheckForUpdatesOnStartup;
        set => SetSetting(
            v => _settings.CheckForUpdatesOnStartup = v,
            _settings.CheckForUpdatesOnStartup,
            value);
    }

    /// <summary>テーマ。0: 自動（OS に従う）、1: ライト、2: ダーク</summary>
    public int ThemeIndex
    {
        get => _settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        set
        {
            var theme = value switch { 1 => "light", 2 => "dark", _ => "auto" };
            SetSetting(v => _settings.Theme = v, _settings.Theme, theme);
        }
    }

    #endregion

    #region 表示言語

    /// <summary>0: システムに従う、1: 日本語、2: English</summary>
    public int LanguageIndex
    {
        get => _settings.Language switch { "ja" => 1, "en" => 2, _ => 0 };
        set
        {
            var language = value switch { 1 => "ja", 2 => "en", _ => string.Empty };
            SetSetting(v => _settings.Language = v, _settings.Language, language);
        }
    }

    #endregion

    #region ウィンドウ

    public bool MinimizeWhileProcessing
    {
        get => _settings.MinimizeWhileProcessing;
        set => SetSetting(v => _settings.MinimizeWhileProcessing = v, _settings.MinimizeWhileProcessing, value);
    }

    public bool ShowMenuBarIcon
    {
        get => _settings.ShowMenuBarIcon;
        set => SetSetting(v => _settings.ShowMenuBarIcon = v, _settings.ShowMenuBarIcon, value);
    }

    public bool WindowForeground
    {
        get => _settings.WindowForeground;
        set => SetSetting(v => _settings.WindowForeground = v, _settings.WindowForeground, value);
    }

    public bool NoMultipleInstance
    {
        get => _settings.NoMultipleInstance;
        set => SetSetting(v => _settings.NoMultipleInstance = v, _settings.NoMultipleInstance, value);
    }

    #endregion

    #region 暗号化

    /// <summary>暗号化ファイルの種類。true: .atc</summary>
    public bool IsFileTypeAtc
    {
        get => _settings.EncryptionFileType != EncryptionFileTypes.Zip;
        set
        {
            if (!value || IsFileTypeAtc) return;
            _settings.EncryptionFileType = EncryptionFileTypes.Atc;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFileTypeZip));
        }
    }

    /// <summary>暗号化ファイルの種類。true: パスワード付き ZIP</summary>
    public bool IsFileTypeZip
    {
        get => _settings.EncryptionFileType == EncryptionFileTypes.Zip;
        set
        {
            if (!value || IsFileTypeZip) return;
            _settings.EncryptionFileType = EncryptionFileTypes.Zip;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFileTypeAtc));
        }
    }

    public bool SaveToSameFolder
    {
        get => _settings.SaveToSameFolder;
        set => SetSetting(v => _settings.SaveToSameFolder = v, _settings.SaveToSameFolder, value);
    }

    public string SaveToFolderPath
    {
        get => _settings.SaveToFolderPath;
        set => SetSetting(v => _settings.SaveToFolderPath = v, _settings.SaveToFolderPath, value);
    }

    public bool ConfirmOverwrite
    {
        get => _settings.ConfirmOverwrite;
        set => SetSetting(v => _settings.ConfirmOverwrite = v, _settings.ConfirmOverwrite, value);
    }

    public bool EncryptOneByOne
    {
        get => _settings.EncryptOneByOne;
        set => SetSetting(v => _settings.EncryptOneByOne = v, _settings.EncryptOneByOne, value);
    }

    public bool KeepTimeStamp
    {
        get => _settings.KeepTimeStamp;
        set => SetSetting(v => _settings.KeepTimeStamp = v, _settings.KeepTimeStamp, value);
    }

    public bool IncludeOriginalExtension
    {
        get => _settings.IncludeOriginalExtension;
        set => SetSetting(v => _settings.IncludeOriginalExtension = v, _settings.IncludeOriginalExtension, value);
    }

    public bool UseAutoName
    {
        get => _settings.UseAutoName;
        set => SetSetting(v => _settings.UseAutoName = v, _settings.UseAutoName, value);
    }

    public string AutoNameFormat
    {
        get => _settings.AutoNameFormat;
        set
        {
            SetSetting(v => _settings.AutoNameFormat = v, _settings.AutoNameFormat, value);
            OnPropertyChanged(nameof(AutoNameExample));
        }
    }

    public bool AutoNameRandomUpperCase
    {
        get => _settings.AutoNameRandomUpperCase;
        set => SetRandomCharset(v => _settings.AutoNameRandomUpperCase = v, _settings.AutoNameRandomUpperCase, value);
    }

    public bool AutoNameRandomLowerCase
    {
        get => _settings.AutoNameRandomLowerCase;
        set => SetRandomCharset(v => _settings.AutoNameRandomLowerCase = v, _settings.AutoNameRandomLowerCase, value);
    }

    public bool AutoNameRandomNumbers
    {
        get => _settings.AutoNameRandomNumbers;
        set => SetRandomCharset(v => _settings.AutoNameRandomNumbers = v, _settings.AutoNameRandomNumbers, value);
    }

    public bool AutoNameRandomSymbols
    {
        get => _settings.AutoNameRandomSymbols;
        set => SetRandomCharset(v => _settings.AutoNameRandomSymbols = v, _settings.AutoNameRandomSymbols, value);
    }

    /// <summary>
    /// 書式を実際に展開してみせる例。書式を打ち替えるたびに更新される。
    /// </summary>
    public string AutoNameExample
    {
        get
        {
            if (string.IsNullOrEmpty(AutoNameFormat)) return string.Empty;

            // 連番の衝突判定でファイルを探しに行かないよう、実在しない場所を基準にする
            var samplePath = Path.Combine(Path.GetTempPath(), "AttacheCase-sample", "Test.atc");

            var generated = AutoFileName.Format(
                AutoNameFormat, samplePath, AutoFileName.CharsetFrom(_settings), out _);

            return _localize.GetString("Settings_AutoNameExample", generated);
        }
    }

    public bool UseCamouflageExtension
    {
        get => _settings.UseCamouflageExtension;
        set => SetSetting(v => _settings.UseCamouflageExtension = v, _settings.UseCamouflageExtension, value);
    }

    public string CamouflageExtension
    {
        get => _settings.CamouflageExtension;
        set => SetSetting(v => _settings.CamouflageExtension = v, _settings.CamouflageExtension, value);
    }

    public bool IsCompressionNone
    {
        get => _settings.CompressionLevel == 0;
        set => SetCompression(value, 0);
    }

    public bool IsCompressionFastest
    {
        get => _settings.CompressionLevel == 1;
        set => SetCompression(value, 1);
    }

    public bool IsCompressionOptimal
    {
        get => _settings.CompressionLevel == 2;
        set => SetCompression(value, 2);
    }


    #endregion

    #region パスワード ZIP

    /// <summary>暗号化しない（ただの ZIP）。旧版の「なし」= 3</summary>
    public bool IsZipNone
    {
        get => _settings.ZipAlgorithm == 3;
        set => SetZipAlgorithm(value, 3);
    }

    public bool IsZipCrypto
    {
        get => _settings.ZipAlgorithm == 0;
        set => SetZipAlgorithm(value, 0);
    }

    public bool IsZipAes128
    {
        get => _settings.ZipAlgorithm == 1;
        set => SetZipAlgorithm(value, 1);
    }

    public bool IsZipAes256
    {
        get => _settings.ZipAlgorithm == 2;
        set => SetZipAlgorithm(value, 2);
    }

    public bool ZipSaveToSameFolder
    {
        get => _settings.ZipSaveToSameFolder;
        set => SetSetting(v => _settings.ZipSaveToSameFolder = v, _settings.ZipSaveToSameFolder, value);
    }

    public string ZipSaveToFolderPath
    {
        get => _settings.ZipSaveToFolderPath;
        set => SetSetting(v => _settings.ZipSaveToFolderPath = v, _settings.ZipSaveToFolderPath, value);
    }

    #endregion

    #region 復号

    public bool DecryptSaveToSameFolder
    {
        get => _settings.DecryptSaveToSameFolder;
        set => SetSetting(v => _settings.DecryptSaveToSameFolder = v, _settings.DecryptSaveToSameFolder, value);
    }

    public string DecryptSaveToFolderPath
    {
        get => _settings.DecryptSaveToFolderPath;
        set => SetSetting(v => _settings.DecryptSaveToFolderPath = v, _settings.DecryptSaveToFolderPath, value);
    }

    public bool NoParentFolder
    {
        get => _settings.NoParentFolder;
        set => SetSetting(v => _settings.NoParentFolder = v, _settings.NoParentFolder, value);
    }

    public bool SetTimeStampToNow
    {
        get => _settings.SetTimeStampToNow;
        set => SetSetting(v => _settings.SetTimeStampToNow = v, _settings.SetTimeStampToNow, value);
    }

    public bool ContinueOnHashMismatch
    {
        get => _settings.ContinueOnHashMismatch;
        set => SetSetting(v => _settings.ContinueOnHashMismatch = v, _settings.ContinueOnHashMismatch, value);
    }

    #endregion

    #region 高度な設定（パスワードファイル）

    public bool AllowPasswordFileDragDrop
    {
        get => _settings.AllowPasswordFileDragDrop;
        set => SetSetting(v => _settings.AllowPasswordFileDragDrop = v, _settings.AllowPasswordFileDragDrop, value);
    }

    public bool CheckPasswordFileOnEncrypt
    {
        get => _settings.CheckPasswordFileOnEncrypt;
        set => SetSetting(v => _settings.CheckPasswordFileOnEncrypt = v, _settings.CheckPasswordFileOnEncrypt, value);
    }

    public string EncryptPasswordFilePath
    {
        get => _settings.EncryptPasswordFilePath;
        set => SetSetting(v => _settings.EncryptPasswordFilePath = v, _settings.EncryptPasswordFilePath, value);
    }

    public bool CheckPasswordFileOnDecrypt
    {
        get => _settings.CheckPasswordFileOnDecrypt;
        set => SetSetting(v => _settings.CheckPasswordFileOnDecrypt = v, _settings.CheckPasswordFileOnDecrypt, value);
    }

    public string DecryptPasswordFilePath
    {
        get => _settings.DecryptPasswordFilePath;
        set => SetSetting(v => _settings.DecryptPasswordFilePath = v, _settings.DecryptPasswordFilePath, value);
    }

    public bool NoErrorWhenPasswordFileMissing
    {
        get => _settings.NoErrorWhenPasswordFileMissing;
        set => SetSetting(v => _settings.NoErrorWhenPasswordFileMissing = v,
            _settings.NoErrorWhenPasswordFileMissing, value);
    }

    public bool ExecuteWithPasswordFileWithoutConfirm
    {
        get => _settings.ExecuteWithPasswordFileWithoutConfirm;
        set => SetSetting(v => _settings.ExecuteWithPasswordFileWithoutConfirm = v,
            _settings.ExecuteWithPasswordFileWithoutConfirm, value);
    }

    #endregion

    #region 高度な設定（パスワードの入力制限）

    /// <summary>1〜10 回。プルダウンの位置は「回数 - 1」</summary>
    public int MissTypeLimitsIndex
    {
        get => Math.Clamp(_settings.MissTypeLimits, 1, 10) - 1;
        set => SetSetting(v => _settings.MissTypeLimits = Math.Clamp(v + 1, 1, 10),
            Math.Clamp(_settings.MissTypeLimits, 1, 10) - 1, value);
    }

    public bool BreakFileWhenLimitExceeded
    {
        get => _settings.BreakFileWhenLimitExceeded;
        set => SetSetting(v => _settings.BreakFileWhenLimitExceeded = v,
            _settings.BreakFileWhenLimitExceeded, value);
    }

    #endregion

    #region 削除

    public int DeleteMode
    {
        get => _settings.DeleteMode;
        set => SetSetting(v => _settings.DeleteMode = v, _settings.DeleteMode, value);
    }

    public int DeleteEncryptedFileMode
    {
        get => _settings.DeleteEncryptedFileMode;
        set => SetSetting(v => _settings.DeleteEncryptedFileMode = v, _settings.DeleteEncryptedFileMode, value);
    }

    public int DeleteRandomTimes
    {
        get => _settings.DeleteRandomTimes;
        set => SetSetting(v => _settings.DeleteRandomTimes = Math.Clamp(v, 0, 99), _settings.DeleteRandomTimes, value);
    }

    /// <summary>NumericUpDown（decimal?）との受け渡し用</summary>
    public decimal DeleteRandomTimesValue
    {
        get => DeleteRandomTimes;
        set
        {
            DeleteRandomTimes = (int)value;
            OnPropertyChanged();
        }
    }

    public int DeleteZeroTimes
    {
        get => _settings.DeleteZeroTimes;
        set => SetSetting(v => _settings.DeleteZeroTimes = Math.Clamp(v, 0, 99), _settings.DeleteZeroTimes, value);
    }

    /// <summary>NumericUpDown（decimal?）との受け渡し用</summary>
    public decimal DeleteZeroTimesValue
    {
        get => DeleteZeroTimes;
        set
        {
            DeleteZeroTimes = (int)value;
            OnPropertyChanged();
        }
    }

    public bool ConfirmDelete
    {
        get => _settings.ConfirmDelete;
        set => SetSetting(v => _settings.ConfirmDelete = v, _settings.ConfirmDelete, value);
    }

    #endregion

    #region Methods

    private void Save()
    {
        // パスワードの実体は settings.json ではなく保管庫（_AtcVault.json）へ
        ApplyRememberedPasswords();

        _settingsService.Save(_settings);
        _mainViewModel.BackToMain();
    }

    private void RestoreDefaults()
    {
        _settings = new AppSettings();
        // どのプロパティが変わったか個別に追えないので、すべて再取得させる
        OnPropertyChanged(string.Empty);
    }

    private async Task BrowseFolderAsync(Action<string> apply)
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _localize["Settings_SelectFolder"],
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            apply(folders[0].Path.LocalPath);
        }
    }

    /// <summary>
    /// ランダム文字列の文字種。変えたら例も作り直す。
    /// </summary>
    private void SetRandomCharset(Action<bool> assign, bool currentValue, bool newValue,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (currentValue == newValue) return;

        assign(newValue);
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(AutoNameExample));
    }

    private async Task BrowseFileAsync(Action<string> apply)
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _localize["Settings_SelectPasswordFile"],
            AllowMultiple = false,
        });

        if (files.Count > 0)
        {
            apply(files[0].Path.LocalPath);
        }
    }

    private void SetCompression(bool selected, int level)
    {
        if (!selected || _settings.CompressionLevel == level) return;

        _settings.CompressionLevel = level;
        OnPropertyChanged(nameof(IsCompressionNone));
        OnPropertyChanged(nameof(IsCompressionFastest));
        OnPropertyChanged(nameof(IsCompressionOptimal));
    }

    private void SetZipAlgorithm(bool selected, int algorithm)
    {
        if (!selected || _settings.ZipAlgorithm == algorithm) return;

        _settings.ZipAlgorithm = algorithm;
        OnPropertyChanged(nameof(IsZipNone));
        OnPropertyChanged(nameof(IsZipCrypto));
        OnPropertyChanged(nameof(IsZipAes128));
        OnPropertyChanged(nameof(IsZipAes256));
    }

    private void SetSetting<T>(Action<T> assign, T currentValue, T newValue,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (Equals(currentValue, newValue)) return;

        assign(newValue);
        OnPropertyChanged(propertyName);
    }

    #endregion
}
