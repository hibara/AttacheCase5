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

/// <summary>
/// パスワード ZIP の作成画面。
/// 流れは暗号化(ATC)と同じ 2 段階（入力+強度メーター → 確認 → 実行）。
/// 旧 Windows 版のパスワード ZIP と同じ体験にそろえている。
/// </summary>
public class ZipPasswordEncryptionViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainViewModel;
    private readonly IEncryptionService _encryptionService;
    /// <summary>ドロップ・選択されたすべての対象（設定「複数ファイルの場合」でジョブに分ける。暗号化(ATC)画面と同じ）</summary>
    private readonly string[] _targetPaths;
    private string _targetPath;
    private int _currentStep = 1; // 1: パスワード入力, 2: 確認, 3: 処理中, 4: 完了
    private string _password = string.Empty;
    private string _passwordConfirm = string.Empty;
    private int _progress;
    private bool _isProcessing;
    private string _statusMessage = string.Empty;
    private string _currentFilePath = string.Empty;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly LocalizationService _localize = LocalizationService.Instance;

    /// <summary>この実行に適用する設定と条件</summary>
    private readonly RunContext _ctx;

    private AppSettings Settings => _ctx.Settings;

    public ZipPasswordEncryptionViewModel(MainWindowViewModel mainViewModel, string[] targetPaths)
        : this(mainViewModel, targetPaths, RunContext.ForGui())
    {
    }

    /// <summary>
    /// 設定（および コマンドライン引数）を適用するコンストラクタ。
    /// 暗号方式は設定の「ZIP の暗号方式」、保存先は ZIP 用の保存先設定に従う。
    /// 複数の対象は暗号化(ATC)画面と同じく設定「複数ファイルの場合」でジョブに分ける
    /// （5.5.0 は先頭 1 件しか処理していなかった。CodeRabbit PR#20）
    /// </summary>
    public ZipPasswordEncryptionViewModel(MainWindowViewModel mainViewModel, string[] targetPaths, RunContext context)
    {
        _mainViewModel = mainViewModel;
        _targetPaths = targetPaths;
        _targetPath = targetPaths.Length > 0 ? targetPaths[0] : string.Empty;
        _ctx = context;
        _encryptionService = new ZipPasswordEncryptionService(ToZipMethod(context.Settings.ZipAlgorithm));

        NextCommand = new RelayCommand(MoveToConfirmation, () => !string.IsNullOrEmpty(Password));
        BackCommand = new RelayCommand(GoBack);
        EncryptCommand = new AsyncRelayCommand(ExecuteEncryptionAsync, CanExecuteEncryption);
        CancelCommand = new RelayCommand(Cancel);

        InitFileTypeSelector();

        if (context.Password != null)
        {
            Password = context.Password;
            PasswordConfirm = context.Password;
            _ = SafeExecuteEncryptionAsync();
        }
        else if (context.Settings.ZipAlgorithm == 3)
        {
            // 「なし」= 暗号化しないただの ZIP。パスワードを訊かずにそのまま作る
            _ = SafeExecuteEncryptionAsync();
        }
    }

    /// <summary>
    /// 設定の ZipAlgorithm がこの画面で扱える範囲（0〜3）か。
    /// 画面を作る前に呼ぶ。範囲外だと <see cref="ToZipMethod"/> が
    /// ArgumentOutOfRangeException を投げてコンストラクタが失敗するため。
    /// </summary>
    public static bool IsSupportedAlgorithm(int algorithm) => algorithm is >= 0 and <= 3;

    private static ZipPasswordEncryptionMethod ToZipMethod(int algorithm) => algorithm switch
    {
        0 => ZipPasswordEncryptionMethod.ZipCrypto,
        1 => ZipPasswordEncryptionMethod.Aes128,
        2 => ZipPasswordEncryptionMethod.Aes256,
        3 => ZipPasswordEncryptionMethod.None,
        // 知らない値を一番弱い ZipCrypto に落とすと、設定ファイルの破損や
        // 将来の値の追加で、気づかないうちに強度が下がる
        _ => throw new ArgumentOutOfRangeException(
            nameof(algorithm), algorithm, "Unknown ZIP encryption method."),
    };

    #region Properties

    // =====================================================================
    // 出力ファイルの種類（ATC / EXE / ZIP）を選ぶコンボボックス。
    //   この画面は ZIP。ATC / EXE を選ぶと暗号化(ATC)画面へ切り替える。
    // =====================================================================

    private IReadOnlyList<EncryptionFileTypeOption> _fileTypeOptions =
        EncryptionFileTypeOption.BuildAll();
    private EncryptionFileTypeOption? _selectedFileType;
    private bool _suppressFileTypeNavigation;

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

    /// <summary>この画面は常に ZIP を選択状態にする。</summary>
    private void InitFileTypeSelector()
    {
        var zip = _fileTypeOptions.FirstOrDefault(o => o.Type == EncryptionFileTypes.Zip);
        if (zip == null) return;

        _suppressFileTypeNavigation = true;
        SelectedFileType = zip;
        _suppressFileTypeNavigation = false;
    }

    /// <summary>
    /// コンボボックスで ATC / EXE に切り替えたら、選択を覚えてから
    /// 暗号化(ATC)画面へ対象と選んだ種類をそのまま渡して遷移する。ZIP はこの画面のまま。
    /// 「前に使ったファイルタイプで暗号化する」が OFF だと覚える処理は何もしないので、
    /// 遷移先の初期選択は引数で渡す。
    /// </summary>
    private void OnFileTypeSelected(int type)
    {
        if (type == EncryptionFileTypes.Zip) return;

        _ctx.RememberEncryptionFileType(type);
        _mainViewModel.StartEncryption(_targetPaths, type);
    }

    /// <summary>現在のステップ (1: パスワード入力, 2: 確認, 3: 処理中, 4: 完了)</summary>
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

    /// <summary>暗号方式が ZipCrypto か（弱い方式なので画面で注意を出す）</summary>
    public bool IsZipCryptoSelected => Settings.ZipAlgorithm == 0;

    public bool IsPasswordInputStep => CurrentStep == 1;
    public bool IsPasswordConfirmStep => CurrentStep == 2;
    public bool IsProcessingStep => CurrentStep == 3;

    /// <summary>完了ステップを表示するか（100% のまま止めて利用者の操作を待つ）</summary>
    public bool IsCompletedStep => CurrentStep == 4;

    /// <summary>進捗パネルを表示するか（処理中と完了で同じ画面を使う）</summary>
    public bool IsProgressVisible => CurrentStep is 3 or 4;

    /// <summary>進捗パネルのボタン文言。処理中は「キャンセル」、完了後は「戻る」</summary>
    public string ProgressButtonText =>
        IsCompletedStep ? _localize["Button_Back"] : _localize["Button_Cancel"];

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                ((RelayCommand)NextCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)EncryptCommand).NotifyCanExecuteChanged();
                UpdatePasswordStrength();
            }
        }
    }

    public string PasswordConfirm
    {
        get => _passwordConfirm;
        set
        {
            if (SetProperty(ref _passwordConfirm, value))
            {
                OnPropertyChanged(nameof(IsPasswordMatch));
                OnPropertyChanged(nameof(ConfirmPasswordBackground));
                ((AsyncRelayCommand)EncryptCommand).NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>パスワードが一致しているか</summary>
    public bool IsPasswordMatch => !string.IsNullOrEmpty(Password) && Password == PasswordConfirm;

    /// <summary>確認用テキストボックスの背景色（暗号化(ATC)画面と同じ。一致 = 薄緑、未入力・不一致 = 薄いオレンジ）</summary>
    public IBrush ConfirmPasswordBackground =>
        IsPasswordMatch
            ? ThemeBrushes.Get("AppPasswordMatchBackground", Color.FromRgb(0xEE, 0xFF, 0xEB))
            : ThemeBrushes.Get("AppPasswordMismatchBackground", Color.FromRgb(255, 228, 196));

    // =====================================================================
    // パスワード強度メーター（zxcvbn）。暗号化(ATC)画面と同じ表示
    // =====================================================================

    private int _passwordStrengthScore = -1; // -1: 空

    /// <summary>強度メーターを出すか（設定「パスワード強度メーターを表示する」）</summary>
    public bool IsPasswordStrengthMeterVisible => Settings.ShowPasswordStrengthMeter;

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
        if (string.IsNullOrEmpty(_password))
        {
            score = -1;
        }
        else
        {
            try
            {
                score = Zxcvbn.Core.EvaluatePassword(_password).Score;
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

    private PasswordVisibilityViewModel? _passwordVisibility;

    /// <summary>パスワード入力欄の目玉アイコン（表示／伏せ字の切り替え）</summary>
    public PasswordVisibilityViewModel PasswordVisibility => _passwordVisibility ??= new(_ctx);

    public int Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    /// <summary>
    /// 現在処理中のファイルのフルパス（進捗バーの上に出す。完了後は出来上がった ZIP のパス）。
    /// ATC の暗号化画面と同じ表示（利用者要望 2026-10-25。以前は ZIP 側だけ無かった）
    /// </summary>
    public string CurrentFilePath
    {
        get => _currentFilePath;
        set => SetProperty(ref _currentFilePath, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string TargetPath
    {
        get => _targetPath;
        set => SetProperty(ref _targetPath, value);
    }

    #endregion

    #region Commands

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand EncryptCommand { get; }
    public ICommand CancelCommand { get; }

    #endregion

    #region Methods

    /// <summary>パスワード入力から確認画面へ進む。</summary>
    private void MoveToConfirmation()
    {
        CurrentStep = 2;
    }

    /// <summary>前の画面に戻る。</summary>
    private void GoBack()
    {
        if (CurrentStep == 2)
        {
            // 確認画面から入力画面へ（確認欄は打ち直し）
            CurrentStep = 1;
            PasswordConfirm = string.Empty;
        }
        else if (CurrentStep != 3)
        {
            _mainViewModel.BackToMain();
        }
    }

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

    private bool CanExecuteEncryption()
    {
        return !IsProcessing &&
               !string.IsNullOrEmpty(Password) &&
               Password == PasswordConfirm;
    }

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
        // 上書きを断られたときに戻る画面（確認画面、またはコマンドライン起動なら入力画面）
        var previousStep = CurrentStep;

        CurrentStep = 3;

        IsProcessing = true;

        // 設定「処理中は最小化する」と Dock アイコンの進捗表示
        WindowBehaviorService.Instance.BeginProcessing(_ctx);
        Progress = 0;
        StatusMessage = _localize["ZipEncryption_Processing"];

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            // 処理単位（入力の組と出力先）は暗号化(ATC)画面・CLI と共通の EncryptionJobPlan で決める。
            // 出力先の決定は指定の矛盾（/oneby と単一ファイル出力など）で InvalidOperationException を
            // 投げうる。GUI の EncryptCommand はこれを直接呼ぶので、握らないと UI の未処理例外経路へ
            // 再送出され、CurrentStep も 3 のまま固まる
            List<EncryptionJob> jobs;
            try
            {
                jobs = EncryptionJobPlan.Build(_targetPaths, _ctx, inputs => CliOutputPath.ForZip(inputs, _ctx));
            }
            catch (InvalidOperationException ex)
            {
                await DialogHelper.NotifyAsync(
                    _ctx.IsUnattended, _localize["Dialog_ErrorTitle"], ex.Message);
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

            var options = new EncryptionOptions
            {
                CompressionLevel = Settings.CompressionLevelValue,
                KeepTimeStamp = Settings.KeepTimeStamp,
                ArchiveName = string.Empty
            };

            string lastOutputPath = string.Empty;
            foreach (var job in jobs)
            {
                var outputPath = job.OutputPath;
                TargetPath = job.InputPaths[0];

                // 上書き確認（設定「同名ファイルがあるとき上書きの確認をする」、/ow=0, /y, /force で省略）。
                // ZipPasswordEncryptionService は出来上がった一時ファイルを無条件に差し替えるので、
                // ここで聞かないと既存の ZIP が黙って上書きされる。ATC の暗号化と同じ扱いにする。
                // 無人実行では確認できないため、上書きせずに終わる（終了コード 5）
                if (File.Exists(outputPath) && Settings.ConfirmOverwrite)
                {
                    var confirmed = await DialogHelper.ConfirmAsync(
                        _ctx.IsUnattended,
                        _localize["Dialog_OverwriteTitle"],
                        _localize.GetString("Dialog_OverwriteFileMessage", Path.GetFileName(outputPath)));

                    if (!confirmed)
                    {
                        _mainViewModel.ReportCommandLineResult(AtcExitCode.OutputExists);
                        if (_ctx.IsCommandLineRun)
                        {
                            _mainViewModel.BackToMain();
                        }
                        else
                        {
                            CurrentStep = previousStep;
                        }
                        return;
                    }
                }

                // 進捗通知は UI スレッドへ後回しで届く。小さなファイルだと処理が同期的に終わって
                // 先に「完了」を表示したあとで最後の「処理中: …」が届き、完了表示を上書きしていた
                //（2026-10-25 実測）。終わったあとに届いた通知は捨てる
                var finished = false;
                var progressReporter = new Progress<EncryptionProgress>(p =>
                {
                    if (finished) return;
                    Progress = p.ProgressPercentage / 100;
                    WindowBehaviorService.Instance.ReportProgress(Progress);
                    StatusMessage = p.StatusMessage;
                    if (!string.IsNullOrEmpty(p.CurrentFilePath))
                        CurrentFilePath = p.CurrentFilePath;
                });

                var result = await _encryptionService.EncryptAsync(
                    job.InputPaths,
                    outputPath,
                    Password,
                    options,
                    progressReporter,
                    _cancellationTokenSource.Token);
                finished = true;

                if (result.Success)
                {
                    // 暗号化に成功したときだけ元ファイルを削除する（設定「削除」）
                    await PostProcessHelper.DeleteOriginalsAsync(job.InputPaths, _ctx);
                    lastOutputPath = outputPath;
                    continue;
                }

                if (result.WasCancelled)
                {
                    _mainViewModel.ReportCommandLineResult(AtcExitCode.Canceled);
                    StatusMessage = _localize["Encryption_Cancelled"];
                    await Task.Delay(1000);
                    _mainViewModel.BackToMain();
                    return;
                }

                // 記録し忘れると、/exit=1 の無人実行が失敗しても終了コード 0 になり、
                // 呼び出し元のスクリプトが失敗に気づけない
                _mainViewModel.ReportCommandLineResult(
                    AtcExitCode.FromEngineReturnCode(result.ErrorCode));
                StatusMessage = _localize.GetString("Encryption_Error", result.ErrorMessage);
                await Task.Delay(3000);

                // 無人実行では入力し直しようがないので、入力画面に戻さず終わりにする
                if (_ctx.IsUnattended)
                {
                    _mainViewModel.BackToMain();
                }
                else
                {
                    CurrentStep = 1;
                }
                return;
            }

            StatusMessage = _localize["Encryption_Complete"];
            CurrentFilePath = lastOutputPath;
            Progress = 100;

            // 設定「処理後に終了する」のときは従来どおり終わらせる。
            // そうでなければ 100% のまま完了画面で止めて、利用者の操作を待つ
            if (Settings.ExitAfterProcess)
            {
                _mainViewModel.CompleteRun(_ctx);
            }
            else
            {
                CurrentStep = 4;
            }
        }
        finally
        {
            IsProcessing = false;
            WindowBehaviorService.Instance.EndProcessing();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    #endregion
}
