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
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        ToggleEncryptPasswordEditCommand = new RelayCommand(ToggleEncryptPasswordEdit);
        ToggleDecryptPasswordEditCommand = new RelayCommand(ToggleDecryptPasswordEdit);
        IssueMigrationCodeCommand = new AsyncRelayCommand(IssueMigrationCodeAsync);
        CreatePortableCommand = new AsyncRelayCommand(CreatePortableAsync);
        RemovePortableCommand = new AsyncRelayCommand(RemovePortableAsync);

        LoadRememberedPasswords();

        Sections = BuildSections();
        // 前回開いていた区分へ戻す（保存やキャンセルで一度閉じて開き直しても、
        // 同じ場所から続けられる）。無ければ先頭
        _selectedSection = Sections.FirstOrDefault(x => x.Key == _lastSectionKey) ?? Sections[0];
    }

    #region 区分（左の一覧）

    /// <summary>
    /// 設定画面の左に並べる区分。項目が多くスクロールが長かったので、区分ごとに
    /// 右側の中身を切り替える形にした（2026-10-25、利用者要望）。Windows 専用の区分は
    /// Windows でだけ並ぶ。Key は XAML 側の IsVisible（ObjectConverters.Equal の
    /// ConverterParameter）と対で、変えるときは SettingsView.axaml も直す
    /// </summary>
    public sealed record SettingsSection(string Key, string Title);

    public const string SectionGeneral = "general";
    public const string SectionWindow = "window";
    public const string SectionEncrypt = "encrypt";
    public const string SectionZip = "zip";
    public const string SectionDecrypt = "decrypt";
    public const string SectionDelete = "delete";
    public const string SectionPasswordFile = "passwordfile";
    public const string SectionAdvanced = "advanced";
    public const string SectionAssociation = "association";
    public const string SectionRemember = "remember";
    public const string SectionPortable = "portable";

    /// <summary>最後に開いていた区分（画面を作り直しても引き継ぐ）</summary>
    private static string _lastSectionKey = SectionGeneral;

    private SettingsSection _selectedSection;

    public IReadOnlyList<SettingsSection> Sections { get; }

    public SettingsSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            // ListBox は選択解除で null を流してくることがあるので、そのときは動かさない
            if (value is null) return;
            if (!SetProperty(ref _selectedSection, value)) return;
            _lastSectionKey = value.Key;
            OnPropertyChanged(nameof(SelectedSectionKey));
            OnPropertyChanged(nameof(SelectedSectionTitle));
        }
    }

    /// <summary>XAML の IsVisible 切り替え用（区分ごとの中身が Equal 比較で見る）</summary>
    public string SelectedSectionKey => _selectedSection.Key;

    /// <summary>右側の見出し</summary>
    public string SelectedSectionTitle => _selectedSection.Title;

    /// <summary>
    /// 「コマンドライン引数で指定した値はここには保存されない」注記を出すか。
    /// 引数で設定を上書きした起動のときだけ（通常起動では出さない）
    /// </summary>
    public bool ShowCommandLineNote => _mainViewModel.HasCommandLineSettingOverride;

    private IReadOnlyList<SettingsSection> BuildSections()
    {
        // 並び順は利用者指定（2026-10-26）。Windows 専用の 2 区分は末尾
        var list = new List<SettingsSection>
        {
            new(SectionGeneral, _localize.GetString("Settings_General")),
            new(SectionWindow, _localize.GetString("Settings_Window")),
            new(SectionRemember, _localize.GetString("Settings_RememberSection")),
            new(SectionEncrypt, _localize.GetString("Settings_Encrypt")),
            new(SectionDecrypt, _localize.GetString("Settings_Decrypt")),
            new(SectionZip, _localize.GetString("Settings_Zip")),
            new(SectionDelete, _localize.GetString("Settings_Delete")),
            new(SectionAdvanced, _localize.GetString("Settings_Advanced")),
            new(SectionPasswordFile, _localize.GetString("Settings_PasswordFile")),
        };
        if (IsWindows)
        {
            list.Add(new(SectionAssociation, _localize.GetString("Settings_AssociationNav")));
            list.Add(new(SectionPortable, _localize.GetString("Settings_PortableNav")));
        }
        return list;
    }

    #endregion

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

        // 記憶済みなら伏せ字の確定表示から始める（本物は欄に載せない）。未設定なら入力待ち
        _isEncryptPasswordEditing = _rememberedEncryptPassword.Length == 0;
        _isDecryptPasswordEditing = _rememberedDecryptPassword.Length == 0;
        _encryptPasswordEditText = string.Empty;
        _decryptPasswordEditText = string.Empty;
    }

    private void NotifyRememberedPasswordFields()
    {
        OnPropertyChanged(nameof(IsEncryptPasswordEditing));
        OnPropertyChanged(nameof(IsDecryptPasswordEditing));
        OnPropertyChanged(nameof(CanTypeEncryptPassword));
        OnPropertyChanged(nameof(CanTypeDecryptPassword));
        OnPropertyChanged(nameof(RememberedEncryptPasswordText));
        OnPropertyChanged(nameof(RememberedDecryptPasswordText));
        OnPropertyChanged(nameof(EncryptPasswordButtonTip));
        OnPropertyChanged(nameof(DecryptPasswordButtonTip));
    }

    /// <summary>保管庫はあるが、この端末ではまだ開けない（移行コードが要る）。</summary>
    public bool IsVaultLocked => _rememberedPasswords.GetState() == RememberedPasswordState.Locked;

    /// <summary>パスワード入力欄を出せる状態か（Locked でなければ出す）。</summary>
    public bool CanEditRememberedPasswords => !IsVaultLocked;

    public bool RememberEncryptPassword
    {
        get => _settings.RememberEncryptPassword;
        set
        {
            SetSetting(v => _settings.RememberEncryptPassword = v, _settings.RememberEncryptPassword, value);
            OnPropertyChanged(nameof(CanTypeEncryptPassword));
            OnPropertyChanged(nameof(HasRememberedPassword));
        }
    }

    public bool RememberDecryptPassword
    {
        get => _settings.RememberDecryptPassword;
        set
        {
            SetSetting(v => _settings.RememberDecryptPassword = v, _settings.RememberDecryptPassword, value);
            OnPropertyChanged(nameof(CanTypeDecryptPassword));
            OnPropertyChanged(nameof(HasRememberedPassword));
        }
    }

    /// <summary>
    /// 記憶パスワードを使っているか（どちらかの「記憶する」が ON）。
    /// 「設定の持ち運び」の保管庫・移行コードの案内は、これが true のときだけ出す（利用者指示 2026-10-26:
    /// 持ち運び目的でない利用者に _AtcVault.json の話を見せると混乱の元）。
    /// </summary>
    public bool HasRememberedPassword => RememberEncryptPassword || RememberDecryptPassword;

    /// <summary>記憶パスワードがあれば確認なしに実行する（旧 fMemPasswordExe）</summary>
    public bool ExecuteWithRememberedPassword
    {
        get => _settings.ExecuteWithRememberedPassword;
        set => SetSetting(v => _settings.ExecuteWithRememberedPassword = v, _settings.ExecuteWithRememberedPassword, value);
    }

    /// <summary>暗号化画面のパスワード強度メーターを表示する（旧 fPasswordStrengthMeter）</summary>
    public bool ShowPasswordStrengthMeter
    {
        get => _settings.ShowPasswordStrengthMeter;
        set => SetSetting(v => _settings.ShowPasswordStrengthMeter = v, _settings.ShowPasswordStrengthMeter, value);
    }

    // ---- 記憶パスワードの入力欄 -------------------------------------------------
    //   入力 → 確定（✓）で本物は VM の中だけに置き、欄には長さに関係なく 16 文字の
    //   伏せ字を出す（欄の Text を読む道具で抜かれないため。利用者指示 2026-10-26）。
    //   変更（✎）で本物を欄に戻して全選択で編集可能にする。
    //   保存時に入力中のままなら、その欄の内容をそのまま確定として扱う。

    private const string MaskedPasswordPlaceholder = "●●●●●●●●●●●●●●●●";

    private string _encryptPasswordEditText = string.Empty;
    private string _decryptPasswordEditText = string.Empty;
    private bool _isEncryptPasswordEditing = true;
    private bool _isDecryptPasswordEditing = true;

    public ICommand ToggleEncryptPasswordEditCommand { get; }
    public ICommand ToggleDecryptPasswordEditCommand { get; }

    /// <summary>暗号化パスワード欄が入力中か（false = 確定済みで伏せ字表示・編集不可）</summary>
    public bool IsEncryptPasswordEditing
    {
        get => _isEncryptPasswordEditing;
        private set
        {
            if (!SetProperty(ref _isEncryptPasswordEditing, value)) return;
            OnPropertyChanged(nameof(CanTypeEncryptPassword));
            OnPropertyChanged(nameof(RememberedEncryptPasswordText));
            OnPropertyChanged(nameof(EncryptPasswordButtonTip));
        }
    }

    /// <summary>復号パスワード欄が入力中か</summary>
    public bool IsDecryptPasswordEditing
    {
        get => _isDecryptPasswordEditing;
        private set
        {
            if (!SetProperty(ref _isDecryptPasswordEditing, value)) return;
            OnPropertyChanged(nameof(CanTypeDecryptPassword));
            OnPropertyChanged(nameof(RememberedDecryptPasswordText));
            OnPropertyChanged(nameof(DecryptPasswordButtonTip));
        }
    }

    public bool CanTypeEncryptPassword => RememberEncryptPassword && IsEncryptPasswordEditing;
    public bool CanTypeDecryptPassword => RememberDecryptPassword && IsDecryptPasswordEditing;

    public string EncryptPasswordButtonTip =>
        _localize[IsEncryptPasswordEditing ? "Settings_ConfirmPasswordTip" : "Settings_EditPasswordTip"];

    public string DecryptPasswordButtonTip =>
        _localize[IsDecryptPasswordEditing ? "Settings_ConfirmPasswordTip" : "Settings_EditPasswordTip"];

    /// <summary>暗号化パスワード欄の文字列。確定済みのときは伏せ字（本物ではない）</summary>
    public string RememberedEncryptPasswordText
    {
        get => _isEncryptPasswordEditing ? _encryptPasswordEditText : MaskedPasswordPlaceholder;
        set
        {
            if (!_isEncryptPasswordEditing) return;
            SetProperty(ref _encryptPasswordEditText, value ?? string.Empty);
        }
    }

    /// <summary>復号パスワード欄の文字列。確定済みのときは伏せ字（本物ではない）</summary>
    public string RememberedDecryptPasswordText
    {
        get => _isDecryptPasswordEditing ? _decryptPasswordEditText : MaskedPasswordPlaceholder;
        set
        {
            if (!_isDecryptPasswordEditing) return;
            SetProperty(ref _decryptPasswordEditText, value ?? string.Empty);
        }
    }

    private void ToggleEncryptPasswordEdit()
    {
        if (IsEncryptPasswordEditing)
        {
            // 確定: 本物を VM に取り込み、欄は伏せ字へ
            _rememberedEncryptPassword = _encryptPasswordEditText;
            _encryptPasswordEditText = string.Empty;
            IsEncryptPasswordEditing = false;
        }
        else
        {
            // 変更: 本物を欄に戻す（ビュー側が全選択にする）
            _encryptPasswordEditText = _rememberedEncryptPassword;
            IsEncryptPasswordEditing = true;
        }
    }

    private void ToggleDecryptPasswordEdit()
    {
        if (IsDecryptPasswordEditing)
        {
            _rememberedDecryptPassword = _decryptPasswordEditText;
            _decryptPasswordEditText = string.Empty;
            IsDecryptPasswordEditing = false;
        }
        else
        {
            _decryptPasswordEditText = _rememberedDecryptPassword;
            IsDecryptPasswordEditing = true;
        }
    }

    /// <summary>入力中のまま保存されたときは、欄の内容をそのまま確定として扱う</summary>
    private void CommitPendingPasswordEdits()
    {
        if (_isEncryptPasswordEditing) _rememberedEncryptPassword = _encryptPasswordEditText;
        if (_isDecryptPasswordEditing) _rememberedDecryptPassword = _decryptPasswordEditText;
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

        CommitPendingPasswordEdits();

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
        NotifyRememberedPasswordFields();

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

        _rememberedEncryptPassword = string.Empty;
        _rememberedDecryptPassword = string.Empty;
        _encryptPasswordEditText = string.Empty;
        _decryptPasswordEditText = string.Empty;
        _isEncryptPasswordEditing = true;
        _isDecryptPasswordEditing = true;
        NotifyRememberedPasswordFields();
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

    /// <summary>
    /// 現在の設定を _AtcCase.json として書き出す。保存先はフォルダー選択で決める
    /// （初期位置は実行ファイルのフォルダー。インストール版を使いながら ZIP 版を別の場所や
    /// USB メモリに展開している、という運用では「exe の隣に固定」だと書き出せないため。
    /// 利用者指示 2026-10-29）。読む側は「起動した AttacheCase.exe と同じフォルダー」の
    /// _AtcCase.json だけを見るので、別のフォルダーへ書いた分はそこの exe を起動したときに効く。
    /// </summary>
    private async Task CreatePortableAsync()
    {
        try
        {
            var folder = await PickFolderAsync(
                _localize["Settings_PortableSelectFolder"], AppContext.BaseDirectory);
            if (folder == null) return;

            var target = Path.Combine(folder, Settings.PortableSettings.FileName);
            if (File.Exists(target))
            {
                var overwrite = await DialogHelper.ConfirmAsync(
                    unattended: false,
                    _localize["Dialog_ConfirmTitle"],
                    _localize.GetString("Settings_PortableOverwriteConfirm", target));
                if (!overwrite) return;
            }

            var path = _settingsService.CreatePortableCopy(folder);
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

    /// <summary>
    /// 「送る」メニューにアタッシェケースがあるか。設定値ではなく、送るフォルダーに
    /// ショートカットがあるかどうかの実態そのもの（利用者指示: レジストリや settings.json に
    /// 持たず、リンクがあれば ON、無ければ OFF）。切り替えるとその場で作成・削除する。
    /// 失敗したときは通知を出してチェックを実態に戻す。
    /// </summary>
    public bool IsSendToShortcutInstalled
    {
        get => OperatingSystem.IsWindows() && Platform.WindowsSendToShortcut.Exists();
        set
        {
            if (!OperatingSystem.IsWindows()) return;
            if (value == Platform.WindowsSendToShortcut.Exists()) return;
            _ = ApplySendToShortcutAsync(value);
        }
    }

    private async Task ApplySendToShortcutAsync(bool install)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (install)
            {
                // 単一ファイル発行では Assembly.Location が空になるので ProcessPath を使う
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    throw new InvalidOperationException("実行ファイルのパスを取得できません。");
                }

                Platform.WindowsSendToShortcut.Create(exePath, _localize.IsJapanese);
            }
            else
            {
                Platform.WindowsSendToShortcut.Remove();
            }
        }
        catch (Exception ex)
        {
            await DialogHelper.NotifyAsync(
                unattended: false,
                _localize["Dialog_ErrorTitle"],
                _localize["Error_SendToShortcut"] + Environment.NewLine + ex.Message);
        }

        // 成否にかかわらず、画面のチェックをフォルダーの実態に合わせ直す
        OnPropertyChanged(nameof(IsSendToShortcutInstalled));
    }

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

    /// <summary>
    /// 「アイコンを表示する」のラベル。同じ設定（TrayIcon）でも置き場の名前が OS で違うので、
    /// Windows は「タスクトレイ」、macOS は「メニューバー」と出す（利用者指示 2026-10-26、タスクトレイへ訂正 10-27）。
    /// </summary>
    public string ShowMenuBarIconLabel => _localize.GetString(
        OperatingSystem.IsWindows() ? "Settings_ShowTaskbarIcon" : "Settings_ShowMenuBarIcon");

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

    /// <summary>PC がタブレットモードのとき最大化で起動しない（Windows のみ表示）</summary>
    public bool NoMaximizeInTabletMode
    {
        get => _settings.NoMaximizeInTabletMode;
        set => SetSetting(v => _settings.NoMaximizeInTabletMode = v, _settings.NoMaximizeInTabletMode, value);
    }

    #endregion

    #region 暗号化

    /// <summary>
    /// 暗号化ファイルの種類。true: .atc。
    /// EXE は Windows 専用なので、Windows 以外で設定値が EXE のときは .atc として表示する
    /// （メイン画面の出力種別コンボと同じ扱い）。
    /// </summary>
    public bool IsFileTypeAtc
    {
        get => _settings.EncryptionFileType == EncryptionFileTypes.Atc
               || (_settings.EncryptionFileType == EncryptionFileTypes.Exe && !IsWindows);
        set
        {
            if (!value || IsFileTypeAtc) return;
            _settings.EncryptionFileType = EncryptionFileTypes.Atc;
            NotifyFileTypeChanged();
        }
    }

    /// <summary>暗号化ファイルの種類。true: 自己実行形式（.exe、Windows 専用）</summary>
    public bool IsFileTypeExe
    {
        get => IsWindows && _settings.EncryptionFileType == EncryptionFileTypes.Exe;
        set
        {
            if (!value || !IsWindows || IsFileTypeExe) return;
            _settings.EncryptionFileType = EncryptionFileTypes.Exe;
            NotifyFileTypeChanged();
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
            NotifyFileTypeChanged();
        }
    }

    private void NotifyFileTypeChanged()
    {
        OnPropertyChanged(nameof(IsFileTypeAtc));
        OnPropertyChanged(nameof(IsFileTypeExe));
        OnPropertyChanged(nameof(IsFileTypeZip));
    }

    /// <summary>
    /// 前に使ったファイルタイプで暗号化する（旧 fEncryptionSameFileTypeBefore）。
    /// ON のあいだは上の「種類」ラジオは効かない（画面では無効化）。
    /// </summary>
    public bool UseLastEncryptionFileType
    {
        get => _settings.UseLastEncryptionFileType;
        set => SetSetting(v => _settings.UseLastEncryptionFileType = v, _settings.UseLastEncryptionFileType, value);
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

    // 複数ファイルの場合（旧版と同じ 3 択。AllFilePack / EncryptOneByOne の 2 フラグは排他）

    /// <summary>通常: 入力 1 つにつき 1 つの暗号化ファイル（フォルダーはフォルダー単位）</summary>
    public bool IsMultipleFilesNormal
    {
        get => !_settings.AllFilePack && !_settings.EncryptOneByOne;
        set
        {
            if (!value || IsMultipleFilesNormal) return;
            _settings.AllFilePack = false;
            _settings.EncryptOneByOne = false;
            NotifyMultipleFilesModeChanged();
        }
    }

    /// <summary>複数のファイルを一つにまとめる（/allpack）</summary>
    public bool IsMultipleFilesAllPack
    {
        get => _settings.AllFilePack;
        set
        {
            if (!value || IsMultipleFilesAllPack) return;
            _settings.AllFilePack = true;
            _settings.EncryptOneByOne = false;
            NotifyMultipleFilesModeChanged();
        }
    }

    /// <summary>フォルダー内のファイルを一つずつ暗号化する（/oneby）</summary>
    public bool IsMultipleFilesOneByOne
    {
        get => !_settings.AllFilePack && _settings.EncryptOneByOne;
        set
        {
            if (!value || IsMultipleFilesOneByOne) return;
            _settings.AllFilePack = false;
            _settings.EncryptOneByOne = true;
            NotifyMultipleFilesModeChanged();
        }
    }

    private void NotifyMultipleFilesModeChanged()
    {
        OnPropertyChanged(nameof(IsMultipleFilesNormal));
        OnPropertyChanged(nameof(IsMultipleFilesAllPack));
        OnPropertyChanged(nameof(IsMultipleFilesOneByOne));
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

    /// <summary>復号時、同名ファイルがあるとき上書きの確認をする（暗号化側の ConfirmOverwrite とは別の設定）</summary>
    public bool DecryptConfirmOverwrite
    {
        get => _settings.DecryptConfirmOverwrite;
        set => SetSetting(v => _settings.DecryptConfirmOverwrite = v, _settings.DecryptConfirmOverwrite, value);
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

    // 旧版と同じ構成: 暗号化側・復号側それぞれに「削除する / メインウィンドウにチェックボックス / 確認」、
    // 削除方法（通常 / ごみ箱 / 完全削除）は両側で共有。保存形式は 5.5.0 のまま
    // （DeleteMode / DeleteEncryptedFileMode = 0 なら削除しない、1〜3 なら方法）で、
    // 共有の方法は DeleteMethod に覚えておき、ON にしたときにそれを *Mode へ写す

    /// <summary>暗号化後に元のファイルを削除する（DeleteMode != 0）</summary>
    public bool DeleteOriginalAfterEncryption
    {
        get => _settings.DeleteMode != DeleteModes.None;
        set
        {
            if (value == DeleteOriginalAfterEncryption) return;
            _settings.DeleteMode = value ? _settings.EffectiveDeleteMethod : DeleteModes.None;
            OnPropertyChanged();
            NotifyDeleteOptionsChanged();
        }
    }

    /// <summary>暗号化画面に削除のチェックボックスを表示する</summary>
    public bool EncryptShowDeleteCheckBox
    {
        get => _settings.EncryptShowDeleteCheckBox;
        set => SetSetting(v => _settings.EncryptShowDeleteCheckBox = v, _settings.EncryptShowDeleteCheckBox, value);
    }

    /// <summary>復号後に暗号化ファイルを削除する（DeleteEncryptedFileMode != 0）</summary>
    public bool DeleteEncryptedFileAfterDecryption
    {
        get => _settings.DeleteEncryptedFileMode != DeleteModes.None;
        set
        {
            if (value == DeleteEncryptedFileAfterDecryption) return;
            _settings.DeleteEncryptedFileMode = value ? _settings.EffectiveDeleteMethod : DeleteModes.None;
            OnPropertyChanged();
            NotifyDeleteOptionsChanged();
        }
    }

    /// <summary>復号画面に削除のチェックボックスを表示する</summary>
    public bool DecryptShowDeleteCheckBox
    {
        get => _settings.DecryptShowDeleteCheckBox;
        set => SetSetting(v => _settings.DecryptShowDeleteCheckBox = v, _settings.DecryptShowDeleteCheckBox, value);
    }

    public bool ConfirmDeleteAfterDecryption
    {
        get => _settings.ConfirmDeleteAfterDecryption;
        set => SetSetting(v => _settings.ConfirmDeleteAfterDecryption = v, _settings.ConfirmDeleteAfterDecryption, value);
    }

    public bool IsDeleteMethodNormal
    {
        get => _settings.EffectiveDeleteMethod == DeleteModes.Delete;
        set { if (value) SetDeleteMethod(DeleteModes.Delete); }
    }

    public bool IsDeleteMethodTrash
    {
        get => _settings.EffectiveDeleteMethod == DeleteModes.Trash;
        set { if (value) SetDeleteMethod(DeleteModes.Trash); }
    }

    public bool IsDeleteMethodWipe
    {
        get => _settings.EffectiveDeleteMethod == DeleteModes.Wipe;
        set { if (value) SetDeleteMethod(DeleteModes.Wipe); }
    }

    /// <summary>「完全削除の場合」の回数指定が意味を持つか（他の項目は独立。利用者判断 2026-10-29）</summary>
    public bool IsWipeOptionsEnabled => IsDeleteMethodWipe;

    private void SetDeleteMethod(int method)
    {
        if (_settings.EffectiveDeleteMethod == method && _settings.DeleteMethod == method) return;
        _settings.DeleteMethod = method;
        // 削除 ON の側は実際に使う方法も揃える
        if (_settings.DeleteMode != DeleteModes.None) _settings.DeleteMode = method;
        if (_settings.DeleteEncryptedFileMode != DeleteModes.None) _settings.DeleteEncryptedFileMode = method;
        NotifyDeleteOptionsChanged();
    }

    private void NotifyDeleteOptionsChanged()
    {
        OnPropertyChanged(nameof(IsDeleteMethodNormal));
        OnPropertyChanged(nameof(IsDeleteMethodTrash));
        OnPropertyChanged(nameof(IsDeleteMethodWipe));
        OnPropertyChanged(nameof(IsWipeOptionsEnabled));
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
        var folder = await PickFolderAsync(_localize["Settings_SelectFolder"]);
        if (folder != null) apply(folder);
    }

    /// <summary>
    /// フォルダー選択ダイアログを出し、選ばれたフォルダーのパスを返す（キャンセルは null）。
    /// </summary>
    /// <param name="startFolder">最初に開くフォルダー。無い・開けないときは OS の既定。</param>
    private static async Task<string?> PickFolderAsync(string title, string? startFolder = null)
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return null;

        IStorageFolder? start = null;
        if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder))
        {
            start = await window.StorageProvider.TryGetFolderFromPathAsync(startFolder);
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
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
