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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using AttacheCase.Cli;
using AttacheCase.Services;
using AttacheCase.Models;

namespace AttacheCase.ViewModels;

public class EncryptionViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainViewModel;
    private readonly IEncryptionService _encryptionService;
    private readonly string[] _targetPaths;
    private string _targetPath;
    private int _currentStep = 1; // 1: パスワード入力, 2: パスワード確認, 3: 処理中
    private string _password1 = string.Empty;
    private string _password2 = string.Empty;
    private int _progress;
    private bool _isProcessing;
    private string _statusMessage = string.Empty;
    private string _currentFilePath = string.Empty;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly string? _rsaPublicKeyXml;
    private readonly LocalizationService _localize = LocalizationService.Instance;

    /// <summary>この実行に適用する設定と条件</summary>
    private readonly RunContext _ctx;

    private AppSettings Settings => _ctx.Settings;

    /// <summary>EXE（自己実行形式）で出力するか。コンボボックスの選択に従う</summary>
    private bool _createExecutable;

    /// <summary>初期化中の SelectedFileType 代入で画面遷移させないためのガード</summary>
    private bool _suppressFileTypeNavigation;

    private bool IsRsaMode => !string.IsNullOrEmpty(_rsaPublicKeyXml);

    /// <summary>
    /// /exit=1 での無人実行。誰も押さないダイアログを出してはいけない。
    /// </summary>
    private bool IsUnattendedRun => _ctx.IsUnattended;

    // 通常のコンストラクタ
    /// <param name="initialFileType">
    /// コンボボックスの初期選択（<see cref="EncryptionFileTypes"/>）。パスワード ZIP 画面のコンボボックスから
    /// ATC / EXE へ切り替えて来たときに、その選択を渡す（設定「前に使ったファイルタイプで暗号化する」が
    /// OFF だと設定には残らないので、ここで引き継ぐ）。null なら保存されている種類
    /// </param>
    public EncryptionViewModel(MainWindowViewModel mainViewModel, string[] targetPaths,
        IEncryptionService? encryptionService = null, int? initialFileType = null)
    {
        _mainViewModel = mainViewModel;
        _targetPaths = targetPaths;
        _targetPath = targetPaths.Length > 0 ? targetPaths[0] : string.Empty;
        _encryptionService = encryptionService ?? new EncryptionService();
        _ctx = RunContext.ForGui();

        NextCommand = new RelayCommand(MoveToConfirmation);
        BackCommand = new RelayCommand(GoBack);
        EncryptCommand = new AsyncRelayCommand(ExecuteEncryptionAsync, CanExecuteEncryption);
        CancelCommand = new RelayCommand(Cancel);
        ClearPasswordFileCommand = new RelayCommand(ClearPasswordFile);

        InitFileTypeSelector(initialFileType);
        // 記憶パスワードで即実行が始まったら、パスワードファイルの自動チェックは要らない
        if (!ApplyRememberedPassword())
        {
            _ = SafeApplyConfiguredPasswordFileAsync();
        }
    }

    /// <summary>
    /// 設定「暗号化パスワードを記憶する」が有効なら、保管庫のパスワードを事前入力する。
    /// さらに「確認なしに記憶パスワードで実行する」（旧 fMemPasswordExe）なら、入力・確認の
    /// 画面を出さずにそのまま暗号化を始める。この端末で保管庫を開けない場合（初めての PC）は何もしない。
    /// </summary>
    /// <returns>暗号化を始めたら true（呼び出し側はパスワードファイルの自動チェックを省く）</returns>
    private bool ApplyRememberedPassword()
    {
        if (!Settings.RememberEncryptPassword) return false;

        try
        {
            var remembered = RememberedPasswordService.Instance
                .GetPassword(RememberedPasswordService.KeyEncryptPassword);
            if (string.IsNullOrEmpty(remembered)) return false;

            Password1 = remembered;
            Password2 = remembered;
        }
        catch (Exception)
        {
            // 保管庫が読めなくても、手入力で先へ進めるようにする
            return false;
        }

        if (!Settings.ExecuteWithRememberedPassword) return false;

        CurrentStep = 3;
        _ = SafeExecuteEncryptionAsync();
        return true;
    }

    // RSAモード用コンストラクタを追加
    public EncryptionViewModel(MainWindowViewModel mainViewModel, string targetPath,
      IEncryptionService? encryptionService, string? rsaPublicKeyXml)
    {
      _mainViewModel = mainViewModel;
      _targetPath = targetPath;
      _targetPaths = [targetPath];
      _encryptionService = encryptionService ?? new EncryptionService();
      _rsaPublicKeyXml = rsaPublicKeyXml;
      _ctx = RunContext.ForGui();

      ClearPasswordFileCommand = new RelayCommand(ClearPasswordFile);
      NextCommand = new RelayCommand(MoveToConfirmation);
      BackCommand = new RelayCommand(GoBack);
      EncryptCommand = new AsyncRelayCommand(ExecuteEncryptionAsync, CanExecuteEncryption);
      CancelCommand = new RelayCommand(Cancel);

      InitFileTypeSelector();

      // RSAモードの場合は直接暗号化ステップへ
      if (IsRsaMode)
      {
        CurrentStep = 3;

        _ = SafeExecuteEncryptionAsync();
      }
    }

    /// <summary>
    /// コマンドライン起動用のコンストラクタ。
    /// 引数でパスワード（/p）またはパスワードファイル（/pfile）が渡されていれば、
    /// 画面を表示したうえで確認を挟まずに暗号化を開始する。
    /// </summary>
    public EncryptionViewModel(MainWindowViewModel mainViewModel, string[] targetPaths,
      IEncryptionService? encryptionService, RunContext context, string? rsaPublicKeyXml = null)
    {
      _mainViewModel = mainViewModel;
      _targetPaths = targetPaths;
      _targetPath = targetPaths.Length > 0 ? targetPaths[0] : string.Empty;
      _encryptionService = encryptionService ?? new EncryptionService();
      _ctx = context;
      _rsaPublicKeyXml = rsaPublicKeyXml;
      _passwordBinary = context.PasswordBinary;

      ClearPasswordFileCommand = new RelayCommand(ClearPasswordFile);
      NextCommand = new RelayCommand(MoveToConfirmation);
      BackCommand = new RelayCommand(GoBack);
      EncryptCommand = new AsyncRelayCommand(ExecuteEncryptionAsync, CanExecuteEncryption);
      CancelCommand = new RelayCommand(Cancel);

      InitFileTypeSelector();

      if (context.Password != null)
      {
        Password1 = context.Password;
        Password2 = context.Password;
      }

      // パスワードが決まっているなら、確認画面を挟まずに実行する
      if (IsRsaMode || context.HasPassword)
      {
        CurrentStep = 3;

        _ = SafeExecuteEncryptionAsync();
      }
      else
      {
        // 引数でパスワードが渡されていなければ、記憶パスワード → 設定の自動チェックの順
        // 記憶パスワードで即実行が始まったら、パスワードファイルの自動チェックは要らない
        if (!ApplyRememberedPassword())
        {
            _ = SafeApplyConfiguredPasswordFileAsync();
        }
      }
    }

    #region Properties

    // =====================================================================
    // 出力ファイルの種類（ATC / EXE / ZIP）を選ぶコンボボックス
    //   EXE は自己実行形式（Windows 専用）。ZIP を選ぶとパスワード ZIP 画面へ遷移。
    //   選んだ内容は暗号化の実行時に設定へ保存され、次回も同じ選択肢になる。
    // =====================================================================

    private IReadOnlyList<EncryptionFileTypeOption> _fileTypeOptions =
        EncryptionFileTypeOption.BuildAll();
    private EncryptionFileTypeOption? _selectedFileType;

    public IReadOnlyList<EncryptionFileTypeOption> FileTypeOptions => _fileTypeOptions;

    public EncryptionFileTypeOption? SelectedFileType
    {
        get => _selectedFileType;
        set
        {
            if (!SetProperty(ref _selectedFileType, value) || value == null) return;
            if (_suppressFileTypeNavigation) return;

            OnFileTypeSelected(value.Type);
        }
    }

    /// <summary>
    /// 保存されている種類（または呼び出し元が指定した種類）でコンボボックスの初期選択を決める。
    /// この画面は ATC / EXE を扱うので、値が ZIP（や非 Windows での EXE）なら
    /// ATC に丸める。ZIP は「パスワード ZIP」の左メニューから入る。
    /// </summary>
    private void InitFileTypeSelector(int? initialFileType = null)
    {
        var saved = initialFileType ?? Settings.EncryptionFileType;
        if (saved == EncryptionFileTypes.Exe && !OperatingSystem.IsWindows())
            saved = EncryptionFileTypes.Atc;

        _createExecutable = saved == EncryptionFileTypes.Exe;

        var initial = _fileTypeOptions.FirstOrDefault(o => o.Type == saved)
                      ?? _fileTypeOptions.FirstOrDefault(o => o.Type == EncryptionFileTypes.Atc)
                      ?? _fileTypeOptions[0];

        _suppressFileTypeNavigation = true;
        SelectedFileType = initial;
        _suppressFileTypeNavigation = false;
    }

    /// <summary>
    /// コンボボックスで種類が変わったときの処理。
    /// ATC / EXE はこの画面のまま（出力拡張子と自己実行形式フラグが変わるだけ）。
    /// ZIP はパスワード ZIP 画面へ、対象をそのまま渡して切り替える。
    /// </summary>
    private void OnFileTypeSelected(int type)
    {
        if (type == EncryptionFileTypes.Zip)
        {
            _mainViewModel.StartZipEncryption(_targetPaths);
            return;
        }

        _createExecutable = type == EncryptionFileTypes.Exe;
    }

    /// <summary>パスワードの代わりにパスワードファイルを使っているか</summary>
    public bool IsPasswordFileMode => _passwordBinary != null;

    /// <summary>「パスワードファイル: key.bin」の表示</summary>
    public string PasswordFileLabel => _passwordFilePath == null
        ? string.Empty
        : _localize.GetString("PasswordFile_InUse", Path.GetFileName(_passwordFilePath));

    /// <summary>
    /// 現在のステップ (1: パスワード入力, 2: パスワード確認, 3: 処理中, 4: 完了)
    /// </summary>
    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            if (!SetProperty(ref _currentStep, value)) return;

            OnPropertyChanged(nameof(IsPasswordInputStep));
            OnPropertyChanged(nameof(IsPasswordConfirmStep));
            OnPropertyChanged(nameof(IsProcessingStep));
            OnPropertyChanged(nameof(IsCompletedStep));
            OnPropertyChanged(nameof(IsProgressVisible));
            OnPropertyChanged(nameof(ProgressButtonText));
        }
    }

    /// <summary>
    /// パスワード入力ステップを表示するか
    /// </summary>
    public bool IsPasswordInputStep => CurrentStep == 1;

    /// <summary>
    /// パスワード確認ステップを表示するか
    /// </summary>
    public bool IsPasswordConfirmStep => CurrentStep == 2;

    /// <summary>
    /// 処理中ステップを表示するか
    /// </summary>
    public bool IsProcessingStep => CurrentStep == 3;

    /// <summary>
    /// 完了ステップを表示するか（100% のまま止めて利用者の操作を待つ）
    /// </summary>
    public bool IsCompletedStep => CurrentStep == 4;

    /// <summary>
    /// 進捗パネルを表示するか（処理中と完了で同じ画面を使う）
    /// </summary>
    public bool IsProgressVisible => CurrentStep is 3 or 4;

    /// <summary>
    /// 進捗パネルのボタン文言。処理中は「キャンセル」、完了後は「戻る」
    /// </summary>
    public string ProgressButtonText =>
        IsCompletedStep ? _localize["Button_Back"] : _localize["Button_Cancel"];

    /// <summary>
    /// 1回目のパスワード入力
    /// </summary>
    public string Password1
    {
        get => _password1;
        set
        {
            if (SetProperty(ref _password1, value))
            {
                ((RelayCommand)NextCommand).NotifyCanExecuteChanged();
                UpdatePasswordStrength();
                PasswordMismatchMessage = string.Empty;
            }
        }
    }

    /// <summary>
    /// 2回目のパスワード入力（確認用）
    /// </summary>
    public string Password2
    {
        get => _password2;
        set
        {
            if (SetProperty(ref _password2, value))
            {
                OnPropertyChanged(nameof(IsPasswordMatch));
                OnPropertyChanged(nameof(ConfirmPasswordBackground));
                ((AsyncRelayCommand)EncryptCommand).NotifyCanExecuteChanged();
                PasswordMismatchMessage = string.Empty;
            }
        }
    }

    /// <summary>
    /// 2 つの入力欄が一致しているか。ATC は空欄のパスワードを許す仕様なので、空欄 == 空欄も一致
    /// （利用者指示 2026-10-27: 1 回目を空欄にしたなら 2 回目も空欄で緑になるべき）
    /// </summary>
    public bool IsPasswordMatch =>
        string.Equals(Password1 ?? string.Empty, Password2 ?? string.Empty, StringComparison.Ordinal);

    private string _passwordMismatchMessage = string.Empty;

    /// <summary>
    /// 確認ステップで「暗号化」を押したときにパスワードが一致していなかった場合のエラー文。
    /// 旧版と同じく、不一致なら暗号化に進まずこの文言を出して入力を待つ。どちらかの欄を
    /// 打ち直したら消える（空 = 表示なし）
    /// </summary>
    public string PasswordMismatchMessage
    {
        get => _passwordMismatchMessage;
        private set
        {
            if (SetProperty(ref _passwordMismatchMessage, value))
            {
                OnPropertyChanged(nameof(HasPasswordMismatch));
            }
        }
    }

    public bool HasPasswordMismatch => _passwordMismatchMessage.Length > 0;

    // =====================================================================
    // パスワード強度メーター（zxcvbn）
    //   Windows 版の表示（脆弱/弱い/やや弱い/安全/高安全）を踏襲。
    //   実装は zxcvbn-core（NuGet, MIT）。強度は目安であって保証ではない
    // =====================================================================

    private int _passwordStrengthScore = -1; // -1: 空

    /// <summary>強度メーターを出すか（設定「パスワード強度メーターを表示する」。パスワードファイル方式では出さない）</summary>
    public bool IsPasswordStrengthMeterVisible => Settings.ShowPasswordStrengthMeter && !IsPasswordFileMode;

    /// <summary>「暗号化後に元のファイルを削除する」チェックボックスを出すか（設定「メインウィンドウに削除のチェックボックスを表示する」）</summary>
    public bool IsDeleteCheckBoxVisible => Settings.EncryptShowDeleteCheckBox;

    private bool? _deleteOriginalAfterEncryption;

    /// <summary>
    /// 暗号化後に元のファイルを削除するか。初期値は設定（DeleteMode != 0）で、画面のチェックボックスで
    /// この実行だけ切り替えられる（設定には書き戻さない。旧版と同じ）。
    /// 入力ステップと確認ステップの 2 つのチェックボックスは同じ値に結ぶ
    /// </summary>
    public bool DeleteOriginalAfterEncryption
    {
        get => _deleteOriginalAfterEncryption ?? Settings.DeleteMode != DeleteModes.None;
        set
        {
            if (value == DeleteOriginalAfterEncryption) return;
            _deleteOriginalAfterEncryption = value;
            OnPropertyChanged();
        }
    }

    /// <summary>強度スコア（0〜4）。空のときは -1</summary>
    public int PasswordStrengthScore => _passwordStrengthScore;

    /// <summary>強度バーの充足量（0〜4 を 0〜100 に換算。空は 0）</summary>
    public double PasswordStrengthValue => _passwordStrengthScore < 0 ? 0 : (_passwordStrengthScore + 1) * 20;

    public string PasswordStrengthLabel => _passwordStrengthScore switch
    {
        0 => _localize["Zxcvbn_Label0"],
        1 => _localize["Zxcvbn_Label1"],
        2 => _localize["Zxcvbn_Label2"],
        3 => _localize["Zxcvbn_Label3"],
        4 => _localize["Zxcvbn_Label4"],
        _ => "",
    };

    public IBrush PasswordStrengthBrush => _passwordStrengthScore switch
    {
        0 => Brushes.Red,
        1 => Brushes.OrangeRed,
        2 => Brushes.Orange,
        3 => Brushes.YellowGreen,
        4 => Brushes.Green,
        _ => Brushes.Transparent,
    };

    private void UpdatePasswordStrength()
    {
        int score;
        if (string.IsNullOrEmpty(_password1))
        {
            score = -1;
        }
        else
        {
            try
            {
                score = Zxcvbn.Core.EvaluatePassword(_password1).Score;
            }
            catch (Exception)
            {
                score = -1; // メーターは飾り。失敗しても入力は続けられる
            }
        }

        if (score == _passwordStrengthScore) return;

        _passwordStrengthScore = score;
        OnPropertyChanged(nameof(PasswordStrengthScore));
        OnPropertyChanged(nameof(PasswordStrengthValue));
        OnPropertyChanged(nameof(PasswordStrengthLabel));
        OnPropertyChanged(nameof(PasswordStrengthBrush));
    }

    /// <summary>
    /// 確認用テキストボックスの背景色。一致 = 薄緑（旧 Windows 版と同じ #EEFFEB、利用者要望 2026-10-25）、
    /// 不一致 = 薄いオレンジ。1 回目が空欄なら 2 回目も空欄で一致（緑）。実色は App.axaml のテーマ辞書（ダークは濃いめ）
    /// </summary>
    public IBrush ConfirmPasswordBackground =>
        IsPasswordMatch
            ? ThemeBrushes.Get("AppPasswordMatchBackground", Color.FromRgb(0xEE, 0xFF, 0xEB))
            : ThemeBrushes.Get("AppPasswordMismatchBackground", Color.FromRgb(255, 228, 196));

    /// <summary>パスワードファイルから作った鍵素材（使っていなければ null）</summary>
    private byte[]? _passwordBinary;

    private string? _passwordFilePath;

    private PasswordVisibilityViewModel? _passwordVisibility;

    /// <summary>パスワード入力欄の目玉アイコン（表示／伏せ字の切り替え）</summary>
    public PasswordVisibilityViewModel PasswordVisibility => _passwordVisibility ??= new(_ctx);

    /// <summary>
    /// 処理の進捗状況（0-100）
    /// </summary>
    public int Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    /// <summary>
    /// 処理中かどうか
    /// </summary>
    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// 現在処理中のファイルのフルパス（進捗表示用）
    /// </summary>
    public string CurrentFilePath
    {
        get => _currentFilePath;
        set => SetProperty(ref _currentFilePath, value);
    }

    /// <summary>
    /// 暗号化対象のパス
    /// </summary>
    public string TargetPath
    {
        get => _targetPath;
        set => SetProperty(ref _targetPath, value);
    }

    #endregion

    #region Commands

    public ICommand NextCommand { get; }
    public ICommand ClearPasswordFileCommand { get; } = new RelayCommand(() => { });
    public ICommand BackCommand { get; }
    public ICommand EncryptCommand { get; }
    public ICommand CancelCommand { get; }

    #endregion

    #region Command Methods

    /// <summary>
    /// パスワード確認画面へ進む
    /// </summary>
    private void MoveToConfirmation()
    {
        // パスワードファイルを使うときは、打ち間違いようがないので確認画面を挟まない
        if (IsPasswordFileMode)
        {
            _ = SafeExecuteEncryptionAsync();
            return;
        }

        CurrentStep = 2;
    }

    /// <summary>
    /// 前の画面に戻る
    /// </summary>
    private void GoBack()
    {
        if (CurrentStep == 2)
        {
            // パスワード確認画面からパスワード入力画面へ
            CurrentStep = 1;
            Password2 = string.Empty;
                    }
        else if (CurrentStep != 3)
        {
            // パスワード入力画面／完了画面からメイン画面へ（処理中は戻るボタン自体を出さない）
            _mainViewModel.BackToMain();
        }
    }

    /// <summary>
    /// キャンセル（メイン画面に戻る）
    /// </summary>
    private void Cancel()
    {
        if (IsProcessing)
        {
            _cancellationTokenSource?.Cancel();
        }
        else
        {
            _mainViewModel.BackToMain();
        }
    }

    /// <summary>
    /// 暗号化を実行できるか
    /// </summary>
    private bool CanExecuteEncryption()
    {
        return !IsProcessing;
    }

    /// <summary>
    /// 暗号化を実行する。
    /// /oneby が指定されている場合は、対象ファイルを 1 つずつ順に処理する。
    /// </summary>
        /// <summary>
    /// 誰も待たない呼び出し（コンストラクタからの開始）用。
    /// 例外をそのままにすると、拾い手のないタスクの例外として消えてしまい、
    /// 画面は処理中のまま止まり、無人実行では終了コードも返らない。
    /// </summary>
    private async Task SafeExecuteEncryptionAsync()
    {
      try
      {
        await ExecuteEncryptionAsync();
      }
      catch (Exception ex)
      {
        Console.Error.WriteLine(ex);
        _mainViewModel.ReportCommandLineResult(AtcExitCode.GeneralError);
        StatusMessage = _localize.GetString("Encryption_Error", ex.Message);
        _mainViewModel.BackToMain();
      }
    }
    private async Task ExecuteEncryptionAsync()
    {
        // 確認ステップ（2 回目の入力）から来たときは、2 つの入力が一致しなければ暗号化に進まない。
        // 暗号化に使うのは 1 回目の Password1 で、ここを素通りすると打ち間違えたパスワードの
        // ファイルができてしまう（利用者報告 2026-10-27）。記憶パスワード・コマンドライン・
        // パスワードファイル・RSA の各経路は確認ステップを通らないので対象外。
        // 空欄のパスワードは ATC では許す仕様（「次へ」に空欄の制限は無い）で、IsPasswordMatch も空欄 == 空欄を一致とする
        if (CurrentStep == 2 && !IsRsaMode && !IsPasswordFileMode && !IsPasswordMatch)
        {
            PasswordMismatchMessage = _localize["Encryption_PasswordMismatch"];
            return;
        }

        // 選んだ出力の種類（ATC / EXE）を覚える。次回も同じ選択肢になる。
        // コンボボックスを使うのは対話的な GUI 操作だけなので、RSA・コマンドライン
        // 経路では保存しない（コマンドラインの /entype や既存設定を上書きしない）
        if (!IsRsaMode && !_ctx.IsCommandLineRun)
        {
            _ctx.RememberEncryptionFileType(
                _createExecutable ? EncryptionFileTypes.Exe : EncryptionFileTypes.Atc);
        }

        // 実際に暗号化する単位（1 回の EncryptAsync に渡す入力の組と出力先）。設定「複数ファイルの場合」の
        // 3 択・出力名の一意化・入力の予約は CLI と共通の EncryptionJobPlan（出力先の矛盾や自動命名の
        // 不正文字は InvalidOperationException）
        List<EncryptionJob> jobs;
        try
        {
            jobs = EncryptionJobPlan.Build(_targetPaths, _ctx, DetermineOutputPath);
        }
        catch (InvalidOperationException ex)
        {
            await DialogHelper.NotifyAsync(IsUnattendedRun, _localize["Dialog_ErrorTitle"], ex.Message);
            _mainViewModel.ReportCommandLineResult(AtcExitCode.UsageError);
            _mainViewModel.BackToMain();
            return;
        }

        if (jobs.Count == 0)
        {
            _mainViewModel.ReportCommandLineResult(AtcExitCode.UsageError);
            StatusMessage = _localize.GetString("Encryption_Error", TargetPath);
            await Task.Delay(3000);
            _mainViewModel.BackToMain();
            return;
        }

        foreach (var job in jobs)
        {
            var completed = await EncryptOneAsync(job);
            if (!completed) return; // 中断・失敗時はその場で止める
        }

        // 設定「処理後に終了する」のときは従来どおり終わらせる。
        // そうでなければ 100% のまま完了画面で止めて、利用者が「戻る」か左メニューを
        // 触るまで待つ（小さいファイルだと一瞬で画面が消え、処理されたのか分からないため）
        if (Settings.ExitAfterProcess)
        {
            _mainViewModel.CompleteRun(_ctx);
            return;
        }

        Progress = 100;
        StatusMessage = _localize["Encryption_Complete"];
        CurrentStep = 4;
    }

    /// <summary>
    /// 1 つの暗号化ファイルを作る。
    /// </summary>
    /// <returns>次の対象へ進んでよければ true、中断・失敗なら false</returns>
    private async Task<bool> EncryptOneAsync(EncryptionJob job)
    {
        var inputPaths = job.InputPaths;
        var outputPath = job.OutputPath;
        TargetPath = inputPaths[0];

        // 上書き確認（/ow=0, /y, /force のときは確認しない）
        // 無人実行では確認できないため、上書きせずに終わる（Windows の CLI と同じ扱い）
        if (File.Exists(outputPath) && Settings.ConfirmOverwrite)
        {
            var confirmed = await DialogHelper.ConfirmAsync(
                IsUnattendedRun,
                _localize["Dialog_OverwriteTitle"],
                _localize.GetString("Dialog_OverwriteFileMessage", Path.GetFileName(outputPath)));

            if (!confirmed)
            {
                _mainViewModel.ReportCommandLineResult(AtcExitCode.OutputExists);
                if (IsRsaMode || _ctx.IsCommandLineRun)
                {
                    _mainViewModel.BackToMain();
                }
                else if (CurrentStep == 3)
                {
                    // 記憶パスワードの即実行（確認ステップを飛ばして 3 に入っている）から断られたとき。
                    // そのままだと処理中画面で止まったままになるので入力画面へ戻す
                    // （CodeRabbit PR#19 の指摘）。通常経路は確認ステップ（2）のまま止まる
                    CurrentStep = 1;
                }
                return false;
            }
        }

        CurrentStep = 3;

        IsProcessing = true;

        // 設定「処理中は最小化する」と Dock アイコンの進捗表示
        WindowBehaviorService.Instance.BeginProcessing(_ctx);
        Progress = 0;
        StatusMessage = IsRsaMode ? _localize["Encryption_RsaProcessing"] : _localize["Encryption_Processing"];

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            // 暗号化オプション
            var options = new EncryptionOptions
            {
                CompressionLevel = Settings.CompressionLevelValue,
                KeepTimeStamp = Settings.KeepTimeStamp,
                MissTypeLimits = Settings.MissTypeLimits,
                BreakFileWhenLimitExceeded = Settings.BreakFileWhenLimitExceeded,
                PasswordBinary = _passwordBinary,
                IsUseRsaEncryption = IsRsaMode,
                RsaPublicKeyXml = _rsaPublicKeyXml ?? string.Empty,
                // 自己実行形式（.exe）。RSA 経路では使わない
                CreateExecutable = _createExecutable && !IsRsaMode
            };

            var progressReporter = new Progress<EncryptionProgress>(p =>
            {
                Progress = p.ProgressPercentage / 100;
                WindowBehaviorService.Instance.ReportProgress(Progress);
                // StatusMessage が空のときは前回の表示を維持（"完了" の早期表示を防ぐ）
                if (!string.IsNullOrEmpty(p.StatusMessage))
                    StatusMessage = p.StatusMessage;
                if (!string.IsNullOrEmpty(p.CurrentFilePath))
                    CurrentFilePath = p.CurrentFilePath;
            });

            // RSAモードの場合、パスワードは内部で自動生成される
            var passwordToUse = IsRsaMode ? string.Empty : Password1;

            var result = await _encryptionService.EncryptAsync(
                inputPaths,
                outputPath,
                passwordToUse,
                options,
                progressReporter,
                _cancellationTokenSource.Token);

            if (result.Success)
            {
                // 暗号化に成功したときだけ元ファイルを削除する（/del。画面のチェックボックスで切り替え可）
                await PostProcessHelper.DeleteOriginalsAsync(inputPaths, _ctx, DeleteOriginalAfterEncryption);

                StatusMessage = _localize["Encryption_Complete"];
                CurrentFilePath = outputPath;
                Progress = 100;
                return true;
            }

            if (result.WasCancelled)
            {
                StatusMessage = _localize["Encryption_Cancelled"];
                _mainViewModel.ReportCommandLineResult(AtcExitCode.Canceled);
                await Task.Delay(1000);
                _mainViewModel.BackToMain();
                return false;
            }

            if (IsUnattendedRun)
            {
                Console.Error.WriteLine(result.ErrorMessage);
            }
            _mainViewModel.ReportCommandLineResult(AtcExitCode.FromEngineReturnCode(result.ErrorCode));
            StatusMessage = _localize.GetString("Encryption_Error", result.ErrorMessage);
            await Task.Delay(3000);
            if (IsRsaMode || _ctx.IsCommandLineRun)
            {
                _mainViewModel.BackToMain();
            }
            else
            {
                // パスワードファイル方式は確認画面（Step2）を通らないので、
                // 失敗時は入力画面（Step1）へ戻す。Step2 に置くと Password2 が
                // 空のまま EncryptCommand も押せず、行き止まりになる
                CurrentStep = IsPasswordFileMode ? 1 : 2;
            }
            return false;
        }
        finally
        {
            IsProcessing = false;
            WindowBehaviorService.Instance.EndProcessing();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    /// <summary>
    /// 暗号化ファイルの出力先を決める（規則は <see cref="CliOutputPath"/> に集約）。
    /// </summary>
    /// <summary>
    /// 投げっぱなしで呼ぶため、例外を握り潰さずに出す。
    /// </summary>
    private async Task SafeApplyConfiguredPasswordFileAsync()
    {
      try
      {
        await ApplyConfiguredPasswordFileAsync();
      }
      catch (Exception ex)
      {
        Console.Error.WriteLine("Password file: " + ex);
      }
    }

    /// <summary>
    /// 設定「暗号化時にパスワードファイルを自動チェックする」を適用する。
    /// </summary>
    private async Task ApplyConfiguredPasswordFileAsync()
    {
        var material = await PasswordFileLoader.TryLoadConfiguredAsync(_ctx, forDecryption: false);
        if (!material.IsAvailable) return;

        SetPasswordFile(material);

        // 設定「確認無しにパスワードファイルで実行する」
        if (Settings.ExecuteWithPasswordFileWithoutConfirm)
        {
            await ExecuteEncryptionAsync();
        }
    }

    /// <summary>
    /// パスワード入力欄へドロップされたファイルを、パスワードファイルとして使う。
    /// </summary>
    public async Task UsePasswordFileAsync(string path)
    {
        var material = await PasswordFileLoader.LoadAsync(path, _ctx);
        if (!material.IsAvailable) return;

        SetPasswordFile(material);
    }

    private void SetPasswordFile(PasswordFileMaterial material)
    {
        _passwordBinary = material.Material;
        _passwordFilePath = material.Path;

        // 旧版（AttacheCase 4 / 5.0）と同じく、鍵素材（SHA-256）の 16 進表記を 1 回目・確認の両欄に見せる
        // （利用者依頼 2026-10-29）。欄は無効化されるので表示専用で、暗号化は常に _passwordBinary
        // （EncryptionOptions.PasswordBinary が文字列より優先）で行う
        var display = PasswordFileHash.ToDisplayText(material.Material!);
        Password1 = display;
        Password2 = display;

        OnPropertyChanged(nameof(IsPasswordFileMode));
        OnPropertyChanged(nameof(IsPasswordStrengthMeterVisible));
        OnPropertyChanged(nameof(PasswordFileLabel));
    }

    private void ClearPasswordFile()
    {
        _passwordBinary = null;
        _passwordFilePath = null;

        // 表示用に入れていた 16 進表記を消し、入力し直せる状態に戻す
        Password1 = string.Empty;
        Password2 = string.Empty;

        OnPropertyChanged(nameof(IsPasswordFileMode));
        OnPropertyChanged(nameof(IsPasswordStrengthMeterVisible));
        OnPropertyChanged(nameof(PasswordFileLabel));
    }

    private string DetermineOutputPath(string[] inputPaths)
    {
      // 自己実行形式のときは拡張子を .exe にする（既定は .atc）
      var extension = _createExecutable && !IsRsaMode ? ".exe" : ".atc";
      return CliOutputPath.ForEncryption(inputPaths, _ctx, extension);
    }

    #endregion
}
