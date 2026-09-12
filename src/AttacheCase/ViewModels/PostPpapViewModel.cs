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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AttacheCase.Models;
using AttacheCase.Cli;
using AttacheCase.Services;
using CommunityToolkit.Mvvm.Input;

namespace AttacheCase.ViewModels;

/// <summary>
/// Post-PPAP（鍵サーバ経由の公開鍵暗号）の画面状態。
/// </summary>
public enum PostPpapState
{
    Initial,               // 初期状態（登録済みなら Home、未登録なら NotRegistered）
    Registering,           // メアド入力中
    AwaitingVerification,  // 認証メールリンクのクリック待ち
         // 通常ファイルドロップ後、宛先メアド入力
}

/// <summary>
/// Post-PPAP 専用 ViewModel。RSA（XML 鍵ファイル方式）は <see cref="PublicKeyViewModel"/> に分離。
/// </summary>
public class PostPpapViewModel : ViewModelBase
{
    private readonly LocalizationService _localize = LocalizationService.Instance;
    private int _encryptProgress;
    private bool _isEncryptionVisible;
    private string _encryptFilePath = string.Empty;

    private readonly MainWindowViewModel _mainViewModel;
    private readonly PostPpapManager _postPpap = new();

    private PostPpapState _state = PostPpapState.Initial;
    private string? _droppedFilePath;
    private string _droppedFileName = string.Empty;

    private string _emailInput = string.Empty;
    private string _recipientEmailInput = string.Empty;
    private string _selfAccountSummary = string.Empty;
    private string _selfAccountNotice = string.Empty;
    private string _verificationEmail = string.Empty;
    private string _statusText = string.Empty;
    private CancellationTokenSource? _verificationCts;
    private CancellationTokenSource? _operationCts;
    private bool _isBusy;

    /// <summary>検索して見つかった宛先の公開鍵（未検索・未登録なら null）</summary>
    private PostPpapKeyInfo? _foundRecipient;

    /// <summary>宛先を一度でも検索したか</summary>
    private bool _hasSearchedRecipient;

    /// <summary>この実行に適用する設定と条件</summary>
    private RunContext _ctx = RunContext.ForGui();

    public PostPpapViewModel(MainWindowViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;

        ShowRegisterCommand = new RelayCommand(() => State = PostPpapState.Registering);
        RegisterEmailCommand = new AsyncRelayCommand(RegisterEmailAsync, () => !IsBusy);
        CancelRegistrationCommand = new RelayCommand(CancelRegistration);
        EncryptForRecipientCommand = new AsyncRelayCommand(EncryptForRecipientAsync, () => !IsBusy);
        SearchRecipientCommand = new AsyncRelayCommand(SearchRecipientAsync, () => !IsBusy);
        CancelCommand = new AsyncRelayCommand(CancelAsync);

        RefreshAccountSummary();
    }

    #region 状態・表示用プロパティ

    public PostPpapState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(IsHome));
            OnPropertyChanged(nameof(IsNotRegistered));
            OnPropertyChanged(nameof(IsRegistering));
            OnPropertyChanged(nameof(IsAwaitingVerification));
        }
    }

    public bool IsHome => State == PostPpapState.Initial && _postPpap.HasAccounts;

    /// <summary>検索して宛先が見つかっている（パスワード不要で送れる）</summary>
    public bool IsRecipientFound => _foundRecipient != null;

    /// <summary>検索したが宛先が未登録だった（パスワード方式になる）</summary>
    public bool IsRecipientNotFound => _hasSearchedRecipient && _foundRecipient == null;

    /// <summary>
    /// 一度でも検索したか。ドロップ領域は検索するまで出さない（Windows 版と同じ）。
    /// </summary>
    public bool HasRecipientResult => _hasSearchedRecipient;
    public bool IsNotRegistered => State == PostPpapState.Initial && !_postPpap.HasAccounts;
    public bool IsRegistering => State == PostPpapState.Registering;
    public bool IsAwaitingVerification => State == PostPpapState.AwaitingVerification;

    public bool HasAccounts => _postPpap.HasAccounts;

    public string DroppedFileName
    {
        get => _droppedFileName;
        set => SetProperty(ref _droppedFileName, value);
    }

    public string EmailInput
    {
        get => _emailInput;
        set => SetProperty(ref _emailInput, value);
    }

    public string RecipientEmailInput
    {
        get => _recipientEmailInput;
        set
        {
            if (SetProperty(ref _recipientEmailInput, value)) ClearRecipientSearch();
        }
    }

    public string SelfAccountSummary
    {
        get => _selfAccountSummary;
        private set => SetProperty(ref _selfAccountSummary, value);
    }

    /// <summary>
    /// 自分のアドレスに添える一言（「（登録が完了しました）」）。
    /// 宛先の検索結果と紛らわしいので、下の StatusText ではなくアドレスの隣に出す。
    /// </summary>
    public string SelfAccountNotice
    {
        get => _selfAccountNotice;
        private set
        {
            if (SetProperty(ref _selfAccountNotice, value))
                OnPropertyChanged(nameof(HasSelfAccountNotice));
        }
    }

    public bool HasSelfAccountNotice => !string.IsNullOrEmpty(SelfAccountNotice);

    public string VerificationEmail
    {
        get => _verificationEmail;
        private set => SetProperty(ref _verificationEmail, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>暗号化の進捗（0-100）</summary>
    public int EncryptProgress
    {
        get => _encryptProgress;
        private set => SetProperty(ref _encryptProgress, value);
    }

    /// <summary>
    /// 進捗パネルを出すか。ドロップした時点で出し、完了後も 100% のまま残す
    /// （小さいファイルだと一瞬で終わってしまい、処理されたのか分からないため）
    /// </summary>
    public bool IsEncryptionVisible
    {
        get => _isEncryptionVisible;
        private set => SetProperty(ref _isEncryptionVisible, value);
    }

    /// <summary>進捗パネルに出すパス。処理中は対象ファイル、完了後は出来上がった暗号化ファイル</summary>
    public string EncryptFilePath
    {
        get => _encryptFilePath;
        private set => SetProperty(ref _encryptFilePath, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                (RegisterEmailCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (EncryptForRecipientCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    #endregion

    #region Commands

    public ICommand ShowRegisterCommand { get; }
    public ICommand RegisterEmailCommand { get; }
    public ICommand CancelRegistrationCommand { get; }
    public ICommand EncryptForRecipientCommand { get; }
    public ICommand SearchRecipientCommand { get; }
    public ICommand CancelCommand { get; }

    #endregion

    #region コマンドライン

    /// <summary>
    /// コマンドライン（/p-ppap=EMAIL）から、宛先を指定して暗号化を開始する。
    /// </summary>
    /// <returns>暗号化に成功したら true</returns>
    public async Task<bool> StartEncryptionFromCommandLineAsync(
        string filePath, string recipientEmail, RunContext context)
    {
        _ctx = context;
        _droppedFilePath = filePath;
        DroppedFileName = Path.GetFileName(filePath);
        RecipientEmailInput = recipientEmail;

        await EncryptForRecipientAsync();

        // 成功していれば Home に戻り、対象ファイルの保持も解除されている
        var succeeded = _droppedFilePath == null;
        if (!succeeded)
        {
            Console.Error.WriteLine(StatusText);
        }
        return succeeded;
    }

    #endregion

    #region ファイルドロップ

    /// <summary>
    /// メイン画面から Post-PPAP メニュー経由でファイルが落とされたときの入口。
    /// この時点では宛先が分からないので、ファイルを保持したまま画面を開く。
    /// 宛先の検索が成功した時点で暗号化する。
    /// </summary>
    public void PrepareEncryptionTarget(string filePath, int fileType)
    {
        if (fileType is 1 or 2 && _postPpap.HasAccounts)
        {
            _mainViewModel.StartDecryption(filePath);
            return;
        }

        if (fileType != 0 || !_postPpap.HasAccounts) return;

        _droppedFilePath = filePath;
        DroppedFileName = Path.GetFileName(filePath);
        StatusText = _localize.GetString("PostPpapView_PendingFile", DroppedFileName);
    }

    public void OnFileDropped(string filePath, int fileType)
    {
        // .atc ファイル（fileType 1/2）は通常の復号画面へ渡す。
        // DecryptionService が自動的に Post-PPAP 経路（Keychain の秘密鍵）を選ぶ。
        if (fileType is 1 or 2 && _postPpap.HasAccounts)
        {
            _mainViewModel.StartDecryption(filePath);
            return;
        }

        if (fileType != 0 || State != PostPpapState.Initial || !_postPpap.HasAccounts) return;

        // 宛先を検索して見つかっていれば、その公開鍵で暗号化する
        if (_foundRecipient != null)
        {
            // 暗号化中に 2 つ目を落とさせない。2 回目が _operationCts を差し替え、
            // 進行中の処理がそのトークンで動いたまま EncryptFilePath/EncryptProgress を
            // 奪い合い、出力先が同じだと同一ファイルへ同時書き込みになる
            if (IsBusy) return;
            _droppedFilePath = filePath;
            DroppedFileName = Path.GetFileName(filePath);
            IsEncryptionVisible = false; // 前回の完了表示を消してから始める
            _ = EncryptForRecipientAsync();
            return;
        }

        // 見つかっていなければ通常のパスワード方式で暗号化する（Windows 版と同じ）
        _mainViewModel.StartEncryption([filePath]);
    }

    /// <summary>
    /// 宛先メールアドレスが鍵サーバに登録されているか調べる。
    /// </summary>
    private async Task SearchRecipientAsync()
    {
        if (string.IsNullOrWhiteSpace(RecipientEmailInput))
        {
            StatusText = _localize["PostPpap_EnterRecipientEmail"];
            return;
        }

        IsBusy = true;
        StatusText = _localize["PostPpap_LookingUpKey"];

        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();

        try
        {
            _foundRecipient = await _postPpap.LookupPublicKeyAsync(RecipientEmailInput, _operationCts.Token);
            _hasSearchedRecipient = true;
            StatusText = string.Empty;

            // 先にファイルが落とされていたら、そのまま暗号化まで進める
            if (_foundRecipient != null && _droppedFilePath != null)
            {
                IsBusy = false;
                await EncryptForRecipientAsync();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = _localize["PostPpap_EncryptionCanceled"];
        }
        catch (Exception ex)
        {
            _foundRecipient = null;
            _hasSearchedRecipient = false;
            StatusText = _localize.GetString("PostPpap_Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsRecipientFound));
            OnPropertyChanged(nameof(IsRecipientNotFound));
            OnPropertyChanged(nameof(HasRecipientResult));
        }
    }

    /// <summary>宛先を打ち替えたら、前回の検索結果は捨てる</summary>
    private void ClearRecipientSearch()
    {
        if (!_hasSearchedRecipient && _foundRecipient == null) return;

        _foundRecipient = null;
        _hasSearchedRecipient = false;

        OnPropertyChanged(nameof(IsRecipientFound));
        OnPropertyChanged(nameof(IsRecipientNotFound));
        OnPropertyChanged(nameof(HasRecipientResult));
    }

    #endregion

    #region Post-PPAP フロー

    /// <summary>メアド登録 → 認証メール送信 → 認証完了待ち</summary>
    private async Task RegisterEmailAsync()
    {
        if (string.IsNullOrWhiteSpace(EmailInput))
        {
            StatusText = _localize["PostPpap_EnterEmail"];
            return;
        }

        IsBusy = true;
        StatusText = _localize["PostPpap_SendingRegistration"];

        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        var ct = _operationCts.Token;

        string pollToken;
        PostPpapKeyInfo pendingAccount;
        try
        {
            // RSA-4096 の鍵生成は数秒かかる同期処理で、UI スレッドで呼ぶと
            // マーキーが描かれる前に固まって見える。ワーカースレッドへ逃がす
            (pollToken, pendingAccount) = await Task.Run(
                () => _postPpap.StartRegistrationAsync(EmailInput, cancellationToken: ct), ct);
        }
        catch (OperationCanceledException)
        {
            IsBusy = false;
            StatusText = string.Empty;
            return;
        }
        catch (Exception ex)
        {
            IsBusy = false;
            StatusText = _localize.GetString("PostPpap_RegistrationFailed", ex.Message);
            return;
        }

        VerificationEmail = EmailInput;
        State = PostPpapState.AwaitingVerification;
        StatusText = _localize.GetString("PostPpap_VerificationSent", VerificationEmail);

        // バックグラウンドで認証完了をポーリング
        _verificationCts?.Dispose();
        _verificationCts = new CancellationTokenSource();
        try
        {
            var verified = await _postPpap.AwaitVerificationAsync(
                pollToken, pendingAccount, cancellationToken: _verificationCts.Token);

            if (verified)
            {
                // 完了の知らせは自分のアドレスの隣へ。宛先検索の結果欄には出さない。
                StatusText = string.Empty;
                SelfAccountNotice = _localize["PostPpap_RegistrationCompletedNotice"];
                RefreshAccountSummary();
                State = PostPpapState.Initial; // → Home
            }
            else
            {
                StatusText = _localize["PostPpap_VerificationTimedOut"];
                State = PostPpapState.Registering;
            }
        }
        catch (OperationCanceledException)
        {
            // 利用者が中断した（認証待ち画面の「戻る」→確認で「はい」）。
            // メール入力画面に置くと「戻れていない」ように見えるため、
            // Post-PPAP の入口まで戻す
            StatusText = string.Empty;
            State = PostPpapState.Initial;
        }
        catch (Exception ex)
        {
            StatusText = _localize.GetString("PostPpap_VerificationError", ex.Message);
            State = PostPpapState.Registering;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelRegistration()
    {
        _verificationCts?.Cancel();
        _operationCts?.Cancel();
        EmailInput = string.Empty;
        StatusText = string.Empty;
        State = PostPpapState.Initial;
    }

    /// <summary>宛先メアドで公開鍵検索 → 暗号化</summary>
    private async Task EncryptForRecipientAsync()
    {
        if (string.IsNullOrWhiteSpace(RecipientEmailInput))
        {
            StatusText = _localize["PostPpap_EnterRecipientEmail"];
            return;
        }
        if (string.IsNullOrEmpty(_droppedFilePath))
        {
            StatusText = _localize["PostPpap_NoTargetFile"];
            return;
        }

        IsBusy = true;
        StatusText = _localize["PostPpap_LookingUpKey"];

        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        var ct = _operationCts.Token;

        try
        {
            // 画面で検索済みならその結果を使う（コマンドライン経路ではここで引く）
            var recipient = _foundRecipient
                            ?? await _postPpap.LookupPublicKeyAsync(RecipientEmailInput, ct);
            if (recipient == null)
            {
                StatusText = _localize.GetString("PostPpap_RecipientNotRegistered", RecipientEmailInput);
                return;
            }

            // 設定とコマンドラインの指定（保存先・自動ファイル名・拡張子偽装）に従う
            var outputPath = CliOutputPath.ForEncryption([_droppedFilePath], _ctx);

            // 上書き確認（パスワード方式の暗号化と同じ扱い）。
            // 無人実行では確認できないので上書きせずに終わる
            if (File.Exists(outputPath) && _ctx.Settings.ConfirmOverwrite)
            {
                var confirmed = await DialogHelper.ConfirmAsync(
                    _ctx.IsUnattended,
                    _localize["Dialog_OverwriteTitle"],
                    _localize.GetString("Dialog_OverwriteFileMessage", Path.GetFileName(outputPath)));

                if (!confirmed)
                {
                    StatusText = _localize["PostPpap_EncryptionCanceled"];
                    return;
                }
            }

            StatusText = _localize["PostPpap_StartingEncryption"];

            EncryptProgress = 0;
            EncryptFilePath = _droppedFilePath;
            IsEncryptionVisible = true;

            var progressReporter = new Progress<EncryptionProgress>(p =>
            {
                EncryptProgress = p.ProgressPercentage / 100;
                if (!string.IsNullOrEmpty(p.CurrentFilePath))
                    EncryptFilePath = p.CurrentFilePath;
            });

            var result = await new EncryptionService().EncryptAsync(
                new[] { _droppedFilePath },
                outputPath,
                password: null!,
                new EncryptionOptions
                {
                    Recipients = new List<PostPpapKeyInfo> { recipient },
                },
                progressReporter,
                ct);

            StatusText = result.Success
                ? _localize.GetString("PostPpap_EncryptionCompleted", Path.GetFileName(outputPath))
                : _localize.GetString("PostPpap_EncryptionFailed", result.ErrorMessage);

            if (result.Success)
            {
                // 暗号化に成功したときだけ元ファイルを削除する（設定「削除」）
                await PostProcessHelper.DeleteOriginalsAsync([_droppedFilePath], _ctx);

                // 進捗は消さずに 100% のまま残し、出来上がったファイルを見せる。
                // 宛先はそのままにしておく（同じ相手に続けて送れるように）
                EncryptProgress = 100;
                EncryptFilePath = outputPath;

                _droppedFilePath = null;
                DroppedFileName = string.Empty;
                State = PostPpapState.Initial; // Home に戻る
            }
            else
            {
                IsEncryptionVisible = false;
            }
        }
        catch (OperationCanceledException)
        {
            IsEncryptionVisible = false;
            StatusText = _localize["PostPpap_EncryptionCanceled"];
        }
        catch (Exception ex)
        {
            IsEncryptionVisible = false;
            StatusText = _localize.GetString("PostPpap_Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 自分の登録アカウント（メールアドレス）を表示用に取り出す。
    /// </summary>
    private void RefreshAccountSummary()
    {
        var account = _postPpap.LoadKeyInfo();
        SelfAccountSummary = account?.Email ?? string.Empty;
    }

    #endregion

    private async Task CancelAsync()
    {
        // 認証メール待ちの間に離れると、確認の自動検出が止まり、リンクを開いても
        // 登録は完了しない。しかも同じアドレスの再登録は約 10 分ロックされる。
        // 気づかず戻ってしまわないよう、モーダルで一度確認する
        if (State == PostPpapState.AwaitingVerification)
        {
            var leave = await DialogHelper.ConfirmAsync(
                unattended: false,
                _localize["PostPpap_LeaveVerifyTitle"],
                _localize["PostPpap_LeaveVerifyMessage"]);
            if (!leave) return;

            // 取り消すと RegisterEmailAsync 側の catch が後始末をして
            // Post-PPAP の入口（Initial）へ戻す。メイン画面へは飛ばない
            _verificationCts?.Cancel();
            return;
        }

        _operationCts?.Cancel();
        _verificationCts?.Cancel();
        _droppedFilePath = null;
        DroppedFileName = string.Empty;
        RecipientEmailInput = string.Empty;
        SelfAccountNotice = string.Empty;
        State = PostPpapState.Initial;
        _mainViewModel.BackToMain();
    }

    public void Reset()
    {
        _droppedFilePath = null;
        DroppedFileName = string.Empty;
        EmailInput = string.Empty;
        RecipientEmailInput = string.Empty;
        StatusText = string.Empty;
        SelfAccountNotice = string.Empty;
        State = PostPpapState.Initial;
    }
}
