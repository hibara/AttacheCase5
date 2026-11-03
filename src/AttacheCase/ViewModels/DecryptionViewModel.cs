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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using AttacheCase.Cli;
using AttacheCase.Services;
using AttacheCase.Models;

namespace AttacheCase.ViewModels;

public class DecryptionViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainViewModel;
    private readonly IDecryptionService _decryptionService;
    private string _targetPath;
    private int _currentStep = 1; // 1: パスワード入力, 2: 処理中
    private string _password = string.Empty;
    private int _progress;
    private bool _isProcessing;
    private string _statusMessage = string.Empty;
    private string _currentFilePath = string.Empty;
    private int _missTypeLimits;

    /// <summary>この暗号化ファイルは、制限回数を超えたら破壊してよいか</summary>
    private bool _breakFileOnLimit;
    private int _passwordMissCount;
    private CancellationTokenSource? _cancellationTokenSource;
    private static readonly byte[] StringTokenRsaBytes = Encoding.ASCII.GetBytes("_AttacheCase_Rsa");
    private readonly string? _rsaPrivateKeyXml;
    private readonly LocalizationService _localize = LocalizationService.Instance;

    /// <summary>この実行に適用する設定と条件</summary>
    private readonly RunContext _ctx;

    /// <summary>2 つ目以降の復号対象（コマンドラインで複数指定されたとき）</summary>
    private readonly Queue<string> _pendingPaths = new();

    /// <summary>
    /// 公開鍵方式（Post-PPAP）で暗号化されたファイル。
    /// 秘密鍵は Keychain から取り出されるため、パスワード入力は要らない。
    /// </summary>
    private bool _isPublicKeyMode;

    private bool IsRsaMode => !string.IsNullOrEmpty(_rsaPrivateKeyXml);

    /// <summary>パスワードを入力させずに復号を始めてよいか</summary>
    private bool IsKeyBasedMode => IsRsaMode || _isPublicKeyMode;

    private AppSettings Settings => _ctx.Settings;

    /// <summary>
    /// /exit=1 での無人実行。パスワードが違っても入力を待たずに切り上げる。
    /// </summary>
    private bool IsUnattendedRun => _ctx.IsUnattended;

    // 通常のコンストラクタ
    public DecryptionViewModel(MainWindowViewModel mainViewModel, string targetPath, IDecryptionService? decryptionService = null)
    {
      _mainViewModel = mainViewModel;
      _targetPath = targetPath;
      _decryptionService = decryptionService ?? new DecryptionService();

      // ミスタイプ回数制限を取得
      _ctx = RunContext.ForGui();
      (_missTypeLimits, _breakFileOnLimit) = DecryptionService.ReadPasswordLimit(_targetPath);

      DecryptCommand = new AsyncRelayCommand(ExecuteDecryptionAsync, CanExecuteDecryption);
      CancelCommand = new RelayCommand(Cancel);
      ClearPasswordFileCommand = new RelayCommand(ClearPasswordFile);

      _isPublicKeyMode = IsPublicKeyFile(_targetPath);

      if (IsKeyBasedMode)
      {
        // 公開鍵方式（Post-PPAP）のファイルはパスワードを聞いても意味がないので、
        // 入力画面を出さずに Keychain の秘密鍵でそのまま復号を始める
        _ = SafeExecuteDecryptionAsync();
      }
      else
      {
        // 記憶パスワードで即実行が始まったら、パスワードファイルの自動チェックは要らない
        if (!ApplyRememberedPassword())
        {
          _ = SafeApplyConfiguredPasswordFileAsync();
        }
      }
    }

    /// <summary>
    /// 設定「復号パスワードを記憶する」が有効なら、保管庫のパスワードを事前入力する。
    /// さらに「確認なしに記憶パスワードで実行する」（旧 fMemPasswordExe）なら、入力を待たずに
    /// そのまま復号を始める。この端末で保管庫を開けない場合（初めての PC）は何もしない。
    /// </summary>
    /// <returns>復号を始めたら true（呼び出し側はパスワードファイルの自動チェックを省く）</returns>
    private bool ApplyRememberedPassword()
    {
      if (!_ctx.Settings.RememberDecryptPassword) return false;

      try
      {
        var remembered = Services.RememberedPasswordService.Instance
            .GetPassword(Services.RememberedPasswordService.KeyDecryptPassword);
        if (string.IsNullOrEmpty(remembered)) return false;

        Password = remembered;
      }
      catch (Exception)
      {
        // 保管庫が読めなくても、手入力で先へ進めるようにする
        return false;
      }

      if (!_ctx.Settings.ExecuteWithRememberedPassword) return false;

      // 「回数超過で破壊する」ファイルでは自動実行しない。記憶パスワードと違うパスワードの
      // ファイルだと、この自動試行が誤入力として数えられ、上限 1 回なら利用者が何もしないうちに
      // 破壊されてしまう（CodeRabbit PR#19 の指摘）。事前入力にとどめ、実行は利用者に任せる
      if (_breakFileOnLimit) return false;

      _ = SafeExecuteDecryptionAsync();
      return true;
    }

    // RSAモード用コンストラクタを追加
    public DecryptionViewModel(MainWindowViewModel mainViewModel, string targetPath,
      IDecryptionService? decryptionService, string? rsaPrivateKeyXml)
    {
      _mainViewModel = mainViewModel;
      _targetPath = targetPath;
      _decryptionService = decryptionService ?? new DecryptionService();
      _rsaPrivateKeyXml = rsaPrivateKeyXml;
      _ctx = RunContext.ForGui();
      ClearPasswordFileCommand = new RelayCommand(ClearPasswordFile);

      (_missTypeLimits, _breakFileOnLimit) = DecryptionService.ReadPasswordLimit(_targetPath);

      DecryptCommand = new AsyncRelayCommand(ExecuteDecryptionAsync, CanExecuteDecryption);
      CancelCommand = new RelayCommand(Cancel);

      // RSAモードの場合は直接復号開始
      if (IsRsaMode)
      {
        _ = SafeExecuteDecryptionAsync();
      }
    }

    /// <summary>
    /// コマンドライン起動用のコンストラクタ。
    /// パスワード（/p, /dpfile）が渡されているか、公開鍵方式のファイルであれば、
    /// 画面を表示したうえで入力を待たずに復号を開始する。
    /// </summary>
    public DecryptionViewModel(MainWindowViewModel mainViewModel, string[] targetPaths,
      IDecryptionService? decryptionService, RunContext context, string? rsaPrivateKeyXml = null)
    {
      _mainViewModel = mainViewModel;
      _targetPath = targetPaths.Length > 0 ? targetPaths[0] : string.Empty;
      _decryptionService = decryptionService ?? new DecryptionService();
      _rsaPrivateKeyXml = rsaPrivateKeyXml;
      _ctx = context;

      for (var i = 1; i < targetPaths.Length; i++)
      {
        _pendingPaths.Enqueue(targetPaths[i]);
      }

      (_missTypeLimits, _breakFileOnLimit) = DecryptionService.ReadPasswordLimit(_targetPath);
      _isPublicKeyMode = IsPublicKeyFile(_targetPath);
      _passwordBinary = context.PasswordBinary;

      DecryptCommand = new AsyncRelayCommand(ExecuteDecryptionAsync, CanExecuteDecryption);
      CancelCommand = new RelayCommand(Cancel);
      ClearPasswordFileCommand = new RelayCommand(ClearPasswordFile);

      if (context.Password != null)
      {
        Password = context.Password;
      }

      if (IsKeyBasedMode || context.HasPassword)
      {
        _ = SafeExecuteDecryptionAsync();
      }
      else
      {
        // 記憶パスワードで即実行が始まったら、パスワードファイルの自動チェックは要らない
        if (!ApplyRememberedPassword())
        {
          _ = SafeApplyConfiguredPasswordFileAsync();
        }
      }
    }

    #region Properties

    /// <summary>現在のステップ (1: パスワード入力, 2: 処理中, 3: 完了)</summary>
    private int CurrentStep
    {
      get => _currentStep;
      set
      {
        if (!SetProperty(ref _currentStep, value)) return;

        OnPropertyChanged(nameof(IsPasswordInputStep));
        OnPropertyChanged(nameof(IsProcessingStep));
        OnPropertyChanged(nameof(IsCompletedStep));
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(ProgressButtonText));
      }
    }

    /// <summary>パスワードの代わりにパスワードファイルを使っているか</summary>
    public bool IsPasswordFileMode => _passwordBinary != null;

    /// <summary>「パスワードファイル: key.bin」の表示</summary>
    public string PasswordFileLabel => _passwordFilePath == null
      ? string.Empty
      : _localize.GetString("PasswordFile_InUse", Path.GetFileName(_passwordFilePath));

    public bool IsPasswordInputStep => CurrentStep == 1;
    public bool IsProcessingStep => CurrentStep == 2;

    /// <summary>「復号後に暗号化ファイルを削除する」チェックボックスを出すか（設定「メインウィンドウに削除のチェックボックスを表示する」）</summary>
    public bool IsDeleteCheckBoxVisible => Settings.DecryptShowDeleteCheckBox;

    private bool? _deleteEncryptedFileAfterDecryption;

    /// <summary>
    /// 復号後に暗号化ファイルを削除するか。初期値は設定（DeleteEncryptedFileMode != 0）で、画面のチェックボックスで
    /// この実行だけ切り替えられる（設定には書き戻さない。旧版と同じ）
    /// </summary>
    public bool DeleteEncryptedFileAfterDecryption
    {
        get => _deleteEncryptedFileAfterDecryption ?? Settings.DeleteEncryptedFileMode != DeleteModes.None;
        set
        {
            if (value == DeleteEncryptedFileAfterDecryption) return;
            _deleteEncryptedFileAfterDecryption = value;
            OnPropertyChanged();
        }
    }

    /// <summary>完了ステップを表示するか（100% のまま止めて利用者の操作を待つ）</summary>
    public bool IsCompletedStep => CurrentStep == 3;

    /// <summary>進捗パネルを表示するか（処理中と完了で同じ画面を使う）</summary>
    public bool IsProgressVisible => CurrentStep is 2 or 3;

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
          ((AsyncRelayCommand)DecryptCommand).NotifyCanExecuteChanged();
        }
      }
    }

    /// <summary>パスワードファイルから作った鍵素材（使っていなければ null）</summary>
    private byte[]? _passwordBinary;

    private string? _passwordFilePath;

    private PasswordVisibilityViewModel? _passwordVisibility;

    /// <summary>パスワード入力欄の目玉アイコン（表示／伏せ字の切り替え）</summary>
    public PasswordVisibilityViewModel PasswordVisibility => _passwordVisibility ??= new(_ctx);

    public int Progress
    {
      get => _progress;
      set => SetProperty(ref _progress, value);
    }

    private bool IsProcessing
    {
      get => _isProcessing;
      set => SetProperty(ref _isProcessing, value);
    }

    public string StatusMessage
    {
      get => _statusMessage;
      set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentFilePath
    {
      get => _currentFilePath;
      set => SetProperty(ref _currentFilePath, value);
    }


    public string TargetPath
    {
      get => _targetPath;
      set => SetProperty(ref _targetPath, value);
    }

    #endregion

    #region Commands

    public ICommand DecryptCommand { get; }
    public ICommand ClearPasswordFileCommand { get; } = new RelayCommand(() => { });
    public ICommand CancelCommand { get; }

    #endregion

    #region Methods

    /// <summary>
    /// v150 の公開鍵方式（Post-PPAP）で暗号化されたファイルかどうか判定
    /// </summary>
    private static bool IsPublicKeyFile(string filePath)
    {
      return !string.IsNullOrEmpty(filePath) &&
             File.Exists(filePath) &&
             DecryptionService.IsPublicKeyEncrypted(filePath);
    }

    /// <summary>
    /// 暗号化ファイルがRSAで暗号化されたものかどうか判定
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    private static bool IsRsaEncryption(string filePath)
    {
      try
      {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        // 使用時
        fs.Seek(4, SeekOrigin.Begin);
        Span<byte> buffer = stackalloc byte[16];
        fs.ReadExactly(buffer);

        if (buffer.SequenceEqual(StringTokenRsaBytes))
        {
          return true;
        }
        else
        {
          return false;
        }
      }
      catch
      {
        return false;
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
        var outputDirectory = DetermineOutputDirectory(TargetPath);

        // ファイルリスト取得用オプション
        var listOptions = new DecryptionOptions
        {
            PasswordBinary = _passwordBinary,
            IsUseRsaEncryption = IsRsaMode,
            RsaPrivateKeyXml = _rsaPrivateKeyXml ?? string.Empty
        };

        // ファイルリストを取得して上書き確認（RSAモードでも通常モードでも実行）
        var fileListResult = await _decryptionService.GetFileListAsync(
            TargetPath,
            IsKeyBasedMode || IsPasswordFileMode ? string.Empty : Password,
            listOptions,
            CancellationToken.None);

        if (!fileListResult.Success)
        {
            // 鍵方式（RSA / Post-PPAP）の場合はパスワードを聞き直しても意味がない
            if (IsKeyBasedMode)
            {
                await DialogHelper.NotifyAsync(
                    IsUnattendedRun,
                    _localize["Dialog_ErrorTitle"],
                    fileListResult.ErrorMessage ?? _localize["Decryption_FileListFailed"]);
                _mainViewModel.ReportCommandLineResult(AtcExitCode.FromEngineReturnCode(fileListResult.ErrorCode));
                _mainViewModel.BackToMain();
                return;
            }

            // 通常モードでパスワードエラーの場合
            if (fileListResult.ErrorCode == ErrorCodes.CRYPTOGRAPHIC_EXCEPTION ||
                fileListResult.ErrorCode == ErrorCodes.PASSWORD_TOKEN_NOT_FOUND)
            {
                _passwordMissCount++;

                if (_passwordMissCount >= _missTypeLimits)
                {
                    // 打ち切り。ここで抜けないと、間違ったパスワードのまま復号処理へ進む
                    // （いまは HandleMissTypeLimitExceededAsync の末尾で終了するので
                    //   たまたま止まっているだけ）
                    await HandleMissTypeLimitExceededAsync();
                    return;
                }
                else
                {
                    await DialogHelper.NotifyAsync(
                        IsUnattendedRun,
                        _localize["Dialog_ErrorTitle"],
                        _localize.GetString("Decryption_PasswordWrongRemaining", _missTypeLimits - _passwordMissCount),
                        DialogIcon.Warning);
                    Password = string.Empty;

                    // 無人実行では入力し直しようがないので、ここで終わりにする
                    if (IsUnattendedRun)
                    {
                        _mainViewModel.ReportCommandLineResult(AtcExitCode.DecryptionFailed);
                        _mainViewModel.BackToMain();
                    }
                    return;
                }
            }
            else
            {
                await DialogHelper.NotifyAsync(
                    IsUnattendedRun,
                    _localize["Dialog_ErrorTitle"],
                    fileListResult.ErrorMessage ?? _localize["Decryption_UnknownError"]);

                _mainViewModel.ReportCommandLineResult(AtcExitCode.FromEngineReturnCode(fileListResult.ErrorCode));
                if (IsUnattendedRun)
                {
                    _mainViewModel.BackToMain();
                }
                return;
            }
        }

        // 上書き確認：既存のファイル/フォルダーがあるか確認
        var existingItems = new List<string>();
        foreach (var relativePath in fileListResult.FilePaths)
        {
            var fullPath = Path.Combine(outputDirectory, relativePath.TrimEnd('\\', '/'));

            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                existingItems.Add(relativePath);
            }
        }

        if (existingItems.Count > 0 && Settings.DecryptConfirmOverwrite)
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
                IsUnattendedRun, _localize["Dialog_OverwriteTitle"], message);

            if (!confirmed)
            {
                _mainViewModel.ReportCommandLineResult(AtcExitCode.OutputExists);
                if (IsKeyBasedMode || _ctx.IsCommandLineRun)
                {
                    _mainViewModel.BackToMain();
                }
                return; // キャンセル
            }
        }

        // 処理中画面へ遷移
        CurrentStep = 2;

        IsProcessing = true;

        // 設定「処理中は最小化する」と Dock アイコンの進捗表示
        WindowBehaviorService.Instance.BeginProcessing(_ctx);
        Progress = 0;
        StatusMessage = IsRsaMode ? _localize["Decryption_RsaProcessing"] : _localize["Decryption_Processing"];

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var options = new DecryptionOptions
            {
                // 「復号時に合わせる」設定のときだけ復号日時にする（既定は記録された元のタイムスタンプ）
                RestoreTimeStamp = !Settings.SetTimeStampToNow,
                NoParentFolder = Settings.NoParentFolder,
                SalvageIgnoreHashCheck = Settings.ContinueOnHashMismatch,
                PasswordBinary = _passwordBinary,
                IsUseRsaEncryption = IsRsaMode,
                RsaPrivateKeyXml = _rsaPrivateKeyXml ?? string.Empty
            };

            var progressReporter = new Progress<EncryptionProgress>(p =>
            {
                Progress = p.ProgressPercentage / 100;
                WindowBehaviorService.Instance.ReportProgress(Progress);
                if (!string.IsNullOrEmpty(p.StatusMessage))
                    StatusMessage = p.StatusMessage;
                if (!string.IsNullOrEmpty(p.CurrentFilePath))
                    CurrentFilePath = p.CurrentFilePath;
            });

            var decryptResult = await _decryptionService.DecryptAsync(
                TargetPath,
                outputDirectory,
                IsKeyBasedMode || IsPasswordFileMode ? string.Empty : Password,
                options,
                progressReporter,
                _cancellationTokenSource.Token);

            if (decryptResult.Success)
            {
                // 復号したファイルを関連付けられたアプリで開く（設定「復号後に開く」）
                PostProcessHelper.OpenDecryptedItems(_ctx, outputDirectory, fileListResult.FilePaths);

                // 復号に成功したときだけ暗号化ファイルを削除する（/delenc。画面のチェックボックスで切り替え可）
                await PostProcessHelper.DeleteEncryptedFileAsync(TargetPath, _ctx, DeleteEncryptedFileAfterDecryption);

                StatusMessage = _localize["Decryption_Complete"];
                CurrentFilePath = outputDirectory;
                Progress = 100;

                // 複数指定されていれば次の対象へ進む
                if (await MoveToNextTargetAsync()) return;

                // 設定「処理後に終了する」のときは従来どおり終わらせる。
                // そうでなければ 100% のまま完了画面で止めて、利用者が「戻る」か
                // 左メニューを触るまで待つ（一瞬で画面が消えると処理されたのか分からないため）
                if (Settings.ExitAfterProcess)
                {
                    _mainViewModel.CompleteRun(_ctx);
                    return;
                }

                CurrentStep = 3;
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
                if (IsUnattendedRun)
                {
                    Console.Error.WriteLine(decryptResult.ErrorMessage);
                }
                _mainViewModel.ReportCommandLineResult(AtcExitCode.FromEngineReturnCode(decryptResult.ErrorCode));

                // 鍵方式（RSA / Post-PPAP）の場合
                if (IsKeyBasedMode)
                {
                    await DialogHelper.NotifyAsync(
                        IsUnattendedRun,
                        _localize["Dialog_ErrorTitle"],
                        decryptResult.ErrorMessage ?? _localize["Decryption_DecryptionFailed"]);
                    _mainViewModel.BackToMain();
                }
                // 通常モードでパスワードエラーの場合
                else if (decryptResult.ErrorCode == ErrorCodes.CRYPTOGRAPHIC_EXCEPTION ||
                         decryptResult.ErrorCode == ErrorCodes.PASSWORD_TOKEN_NOT_FOUND)
                {
                    _passwordMissCount++;

                    if (_passwordMissCount >= _missTypeLimits)
                    {
                        StatusMessage = _localize["Decryption_PasswordMissExceeded"];
                        await HandleMissTypeLimitExceededAsync();
                    }
                    else if (IsUnattendedRun)
                    {
                        // 無人実行では入力し直しようがないので、ここで終わりにする
                        StatusMessage = _localize.GetString("Decryption_PasswordWrongRemaining", _missTypeLimits - _passwordMissCount);
                        await Task.Delay(1000);
                        _mainViewModel.BackToMain();
                    }
                    else
                    {
                        StatusMessage = _localize.GetString("Decryption_PasswordWrongRemaining", _missTypeLimits - _passwordMissCount);
                        await Task.Delay(2000);

                        CurrentStep = 1;
                        Password = string.Empty;
                        IsProcessing = false;
                                                    }
                }
                else if (IsUnattendedRun)
                {
                    StatusMessage = _localize.GetString("Decryption_Error", decryptResult.ErrorMessage);
                    await Task.Delay(1000);
                    _mainViewModel.BackToMain();
                }
                else
                {
                    // その他のエラー
                    StatusMessage = _localize.GetString("Decryption_Error", decryptResult.ErrorMessage);
                    await Task.Delay(3000);
                    CurrentStep = 1;
                                        }
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
    /// パスワードの入力ミスが制限回数に達したときの処理。
    /// 暗号化ファイル側が「破壊してよい」と記録していれば破壊してから終了する。
    /// </summary>
    private async Task HandleMissTypeLimitExceededAsync()
    {
      if (_breakFileOnLimit && DecryptionService.BreakTheFile(TargetPath))
      {
        await DialogHelper.NotifyAsync(
          IsUnattendedRun,
          _localize["Dialog_ErrorTitle"],
          _localize["Decryption_FileBroken"]);
      }
      else
      {
        await DialogHelper.NotifyAsync(
          IsUnattendedRun,
          _localize["Dialog_ErrorTitle"],
          _localize["Decryption_PasswordMissExceeded"]);
      }

      // Environment.Exit はプロセスを即座に落とすので、finally も
      // CancellationTokenSource の後始末も Avalonia の終了処理も走らない。
      // MainWindowViewModel 側と同じ終了経路にそろえる
      _mainViewModel.ReportCommandLineResult(AtcExitCode.DecryptionFailed);

      if (IsUnattendedRun)
      {
        _mainViewModel.FinishCommandLineRun();
      }
      else
      {
        // 画面から実行しているときはアプリごと終わらせず、最初の画面へ戻す
        _mainViewModel.BackToMain();
      }
    }

    /// <summary>
    /// 設定「復号時にパスワードファイルを自動チェックする」を適用する。
    /// </summary>
    private async Task ApplyConfiguredPasswordFileAsync()
    {
      var material = await PasswordFileLoader.TryLoadConfiguredAsync(_ctx, forDecryption: true);
      if (!material.IsAvailable) return;

      SetPasswordFile(material);

      // 設定「確認無しにパスワードファイルで実行する」
      if (Settings.ExecuteWithPasswordFileWithoutConfirm)
      {
        await ExecuteDecryptionAsync();
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

      // 旧版（AttacheCase 4 / 5.0）と同じく、鍵素材（SHA-256）の 16 進表記を入力欄に見せる
      // （利用者依頼 2026-10-29）。欄は無効化されるので表示専用で、復号は常に _passwordBinary で行う
      Password = PasswordFileHash.ToDisplayText(material.Material!);

      OnPropertyChanged(nameof(IsPasswordFileMode));
      OnPropertyChanged(nameof(PasswordFileLabel));
    }

    private void ClearPasswordFile()
    {
      _passwordBinary = null;
      _passwordFilePath = null;

      // 表示用に入れていた 16 進表記を消し、入力し直せる状態に戻す
      Password = string.Empty;

      OnPropertyChanged(nameof(IsPasswordFileMode));
      OnPropertyChanged(nameof(PasswordFileLabel));
    }

    private string DetermineOutputDirectory(string atcFilePath)
    {
      return CliOutputPath.ForDecryption(atcFilePath, _ctx);
    }

    /// <summary>
    /// コマンドラインで複数のファイルが指定されていた場合、次の対象へ進む。
    /// </summary>
    /// <returns>次の対象の処理を開始したら true</returns>
    private async Task<bool> MoveToNextTargetAsync()
    {
      if (_pendingPaths.Count == 0) return false;

      TargetPath = _pendingPaths.Dequeue();

      // 対象ごとにミスタイプ上限と暗号化方式を読み直す
      (_missTypeLimits, _breakFileOnLimit) = DecryptionService.ReadPasswordLimit(TargetPath);
      _isPublicKeyMode = IsPublicKeyFile(TargetPath);

      Progress = 0;
      _passwordMissCount = 0;

      await ExecuteDecryptionAsync();
      return true;
    }

    #endregion
}