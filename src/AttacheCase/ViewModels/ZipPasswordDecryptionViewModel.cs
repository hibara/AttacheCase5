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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using AttacheCase.Cli;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.ViewModels;

public class ZipPasswordDecryptionViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainViewModel;
    private readonly IDecryptionService _decryptionService;
    private string _targetPath;
    private int _currentStep = 1; // 1: パスワード入力, 2: 処理中, 3: 完了
    private string _password = string.Empty;
    private int _progress;
    private bool _isProcessing;
    private string _statusMessage = string.Empty;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly LocalizationService _localize = LocalizationService.Instance;

    /// <summary>この実行に適用する設定と条件</summary>
    private readonly RunContext _ctx;

    public ZipPasswordDecryptionViewModel(MainWindowViewModel mainViewModel, string targetPath, IDecryptionService? decryptionService = null)
    {
        _mainViewModel = mainViewModel;
        _targetPath = targetPath;
        _decryptionService = decryptionService ?? new ZipPasswordDecryptionService();
        _ctx = RunContext.ForGui();

        DecryptCommand = new AsyncRelayCommand(ExecuteDecryptionAsync, CanExecuteDecryption);
        CancelCommand = new RelayCommand(Cancel);
    }

    /// <summary>
    /// コマンドライン起動用のコンストラクタ。
    /// /p が渡されていれば入力を待たずに解凍を開始する。
    /// </summary>
    public ZipPasswordDecryptionViewModel(MainWindowViewModel mainViewModel, string targetPath, RunContext context)
    {
        _mainViewModel = mainViewModel;
        _targetPath = targetPath;
        _decryptionService = new ZipPasswordDecryptionService();
        _ctx = context;

        DecryptCommand = new AsyncRelayCommand(ExecuteDecryptionAsync, CanExecuteDecryption);
        CancelCommand = new RelayCommand(Cancel);

        if (context.Password != null)
        {
            Password = context.Password;
            _ = SafeExecuteDecryptionAsync();
        }
    }

    #region Properties

    public bool IsPasswordInputStep => _currentStep == 1;
    public bool IsProcessingStep => _currentStep == 2;

    /// <summary>完了ステップを表示するか（100% のまま止めて利用者の操作を待つ）</summary>
    public bool IsCompletedStep => _currentStep == 3;

    /// <summary>進捗パネルを表示するか（処理中と完了で同じ画面を使う）</summary>
    public bool IsProgressVisible => _currentStep is 2 or 3;

    /// <summary>進捗パネルのボタン文言。処理中は「キャンセル」、完了後は「戻る」</summary>
    public string ProgressButtonText =>
        IsCompletedStep ? _localize["Button_Back"] : _localize["Button_Cancel"];

    /// <summary>ステップを変えて、関係する表示をまとめて更新する</summary>
    private void SetStep(int step)
    {
        _currentStep = step;
        OnPropertyChanged(nameof(IsPasswordInputStep));
        OnPropertyChanged(nameof(IsProcessingStep));
        OnPropertyChanged(nameof(IsCompletedStep));
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(ProgressButtonText));
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                ((AsyncRelayCommand)DecryptCommand).NotifyCanExecuteChanged();
            }
        }
    }

    private PasswordVisibilityViewModel? _passwordVisibility;

    /// <summary>パスワード入力欄の目玉アイコン（表示／伏せ字の切り替え）</summary>
    public PasswordVisibilityViewModel PasswordVisibility => _passwordVisibility ??= new(_ctx);

    public int Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
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

    /// <summary>
    /// 暗号化対象のパス
    /// </summary>
    public string TargetPath
    {
        get => _targetPath;
        set => SetProperty(ref _targetPath, value);
    }

    public string HeaderText => _localize.GetString("ZipDecryption_HeaderFormat");

    #endregion

    #region Commands

    public ICommand DecryptCommand { get; }
    public ICommand CancelCommand { get; }

    #endregion

    #region Methods

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

    private bool CanExecuteDecryption()
    {
        return !IsProcessing;
    }

        /// <summary>
    /// 誰も待たない呼び出し（コンストラクタからの開始）用。
    /// 例外をそのままにすると、拾い手のないタスクの例外として消えてしまい、
    /// 画面は処理中のまま止まり、無人実行では終了コードも返らない。
    /// </summary>
    private async Task SafeExecuteDecryptionAsync()
    {
      try
      {
        await ExecuteDecryptionAsync();
      }
      catch (Exception ex)
      {
        Console.Error.WriteLine(ex);
        _mainViewModel.ReportCommandLineResult(AtcExitCode.GeneralError);
        StatusMessage = _localize.GetString("Decryption_Error", ex.Message);
        _mainViewModel.BackToMain();
      }
    }
    private async Task ExecuteDecryptionAsync()
    {
        // 展開先は /out → 復号の保存先設定 → ZIP と同じフォルダーの順で決める
        var outputDirectory = CliOutputPath.ForDecryption(Path.GetFullPath(_targetPath), _ctx);

        // ファイルリストを取得して上書き確認
        var fileListResult = await _decryptionService.GetFileListAsync(
            _targetPath,
            Password,
            CancellationToken.None);

        if (fileListResult.Success)
        {
            // 上書き確認：既存のファイル/フォルダーがあるか確認
            var existingItems = new System.Collections.Generic.List<string>();
            foreach (var relativePath in fileListResult.FilePaths)
            {
                var fullPath = Path.Combine(outputDirectory, relativePath.TrimEnd('\\', '/'));

                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    existingItems.Add(relativePath);
                }
            }

            if (existingItems.Count > 0 && _ctx.Settings.ConfirmOverwrite)
            {
                string message;
                if (existingItems.Count == 1)
                {
                    message = _localize.GetString("Dialog_OverwriteFileMessage", existingItems[0]);
                }
                else if (existingItems.Count <= 5)
                {
                    var itemList = string.Join("\n", existingItems);
                    message = _localize.GetString("Dialog_OverwriteMultipleFiles", itemList);
                }
                else
                {
                    message = _localize.GetString("Dialog_OverwriteCountFiles", existingItems.Count);
                }

                // 無人実行では確認できないため、上書きせずに終わる
                var confirmed = await DialogHelper.ConfirmAsync(
                    _ctx.IsUnattended, _localize["Dialog_OverwriteTitle"], message);

                if (!confirmed)
                {
                    // /exit=1 の無人実行で終了コードを記録しないと、上書き中止でも
                    // 0 が返り、呼び出し元スクリプトが失敗に気づけない
                    _mainViewModel.ReportCommandLineResult(AtcExitCode.OutputExists);
                    if (_ctx.IsUnattended)
                    {
                        _mainViewModel.BackToMain();
                    }
                    return; // キャンセル
                }
            }
        }

        // 処理中画面へ
        SetStep(2);

        IsProcessing = true;

        // 設定「処理中は最小化する」と Dock アイコンの進捗表示
        WindowBehaviorService.Instance.BeginProcessing(_ctx);
        Progress = 0;
        StatusMessage = _localize["ZipDecryption_Processing"];

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var options = new DecryptionOptions
            {
                // 「復号時に合わせる」設定のときだけ復号日時にする
                RestoreTimeStamp = !_ctx.Settings.SetTimeStampToNow,
                NoParentFolder = _ctx.Settings.NoParentFolder,
            };

            var progressReporter = new Progress<EncryptionProgress>(p =>
            {
                Progress = p.ProgressPercentage / 100;
                WindowBehaviorService.Instance.ReportProgress(Progress);
                StatusMessage = p.StatusMessage;
            });

            var decryptResult = await _decryptionService.DecryptAsync(
                _targetPath,
                outputDirectory,
                Password,
                options,
                progressReporter,
                _cancellationTokenSource.Token);

            if (decryptResult.Success)
            {
                // 復号したファイルを関連付けられたアプリで開く（設定「復号後に開く」）
                PostProcessHelper.OpenDecryptedItems(_ctx, outputDirectory, fileListResult.FilePaths);

                // 復号に成功したときだけ暗号化ファイルを削除する（設定「削除」）
                await PostProcessHelper.DeleteEncryptedFileAsync(_targetPath, _ctx);

                StatusMessage = _localize["Decryption_Complete"];
                Progress = 100;

                // 設定「処理後に終了する」のときは従来どおり終わらせる。
                // そうでなければ 100% のまま完了画面で止めて、利用者の操作を待つ
                if (_ctx.Settings.ExitAfterProcess)
                {
                    _mainViewModel.CompleteRun(_ctx);
                }
                else
                {
                    SetStep(3);
                }
            }
            else if (decryptResult.WasCancelled)
            {
                _mainViewModel.ReportCommandLineResult(AtcExitCode.Canceled);
                StatusMessage = _localize["Decryption_Cancelled"];
                await Task.Delay(1000);
                _mainViewModel.BackToMain();
            }
            else
            {
                // パスワードエラーまたはその他のエラー。/exit=1 で終了コードを
                // 記録しないと失敗しても 0 が返る（DecryptionViewModel と同じ扱い）
                _mainViewModel.ReportCommandLineResult(
                    AtcExitCode.FromEngineReturnCode(decryptResult.ErrorCode));
                await DialogHelper.NotifyAsync(
                    _ctx.IsUnattended,
                    _localize["Dialog_ErrorTitle"],
                    decryptResult.ErrorMessage ?? _localize["ZipDecryption_ExtractionFailed"]);

                // 無人実行（/exit=1）では入力し直しようがないので、ここで終わりにする
                if (_ctx.IsUnattended)
                {
                    _mainViewModel.BackToMain();
                    return;
                }

                // パスワード入力画面に戻る
                SetStep(1);
                Password = string.Empty;
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