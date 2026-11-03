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
using System.Threading.Tasks;
using System.Windows.Input;
using AttacheCase.Services;
using CommunityToolkit.Mvvm.Input;

namespace AttacheCase.ViewModels;

/// <summary>
/// RSA 公開鍵暗号（XML 鍵ファイル方式）の画面状態。
/// </summary>
public enum RsaWorkflowState
{
    Initial,                 // 初期状態（鍵・ファイル待ち）
    WaitingForFile,          // 公開鍵ドロップ済み、通常ファイル待ち
    WaitingForEncryptedFile, // 秘密鍵ドロップ済み、RSA 暗号化ファイル待ち
    WaitingForPublicKey,     // 通常ファイルドロップ済み、公開鍵待ち
    WaitingForPrivateKey,    // RSA 暗号化ファイルドロップ済み、秘密鍵待ち
}

/// <summary>
/// RSA 公開鍵暗号 専用 ViewModel。Post-PPAP（鍵サーバ方式）は
/// <see cref="PostPpapViewModel"/> に分離されている。
/// </summary>
public class PublicKeyViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainViewModel;
    private readonly IRsaKeyGenerator _keyGenerator;
    private readonly LocalizationService _localize = LocalizationService.Instance;

    private RsaWorkflowState _state = RsaWorkflowState.Initial;
    private string _message = string.Empty;
    private string? _droppedKeyPath;
    private string? _droppedFilePath;
    private string _keyGuid = string.Empty;
    private string _droppedFileName = string.Empty;

    public PublicKeyViewModel(MainWindowViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        _keyGenerator = new RsaKeyGeneratorService();

        GenerateKeyCommand = new AsyncRelayCommand(GenerateKeyPairAsync);
        CancelCommand = new RelayCommand(Cancel);

        UpdateMessage();
    }

    #region 状態・表示用プロパティ

    public RsaWorkflowState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(IsInitialState));
            OnPropertyChanged(nameof(IsWaitingState));
            OnPropertyChanged(nameof(IsPublicKeyLoaded));
            OnPropertyChanged(nameof(IsPrivateKeyLoaded));
            OnPropertyChanged(nameof(IsRsaEncryptedFileLoaded));
            OnPropertyChanged(nameof(IsWaitingForPublicKey));
            UpdateMessage();
        }
    }

    public bool IsInitialState => State == RsaWorkflowState.Initial;
    public bool IsWaitingState => State != RsaWorkflowState.Initial;
    public bool IsPublicKeyLoaded => State == RsaWorkflowState.WaitingForFile;
    public bool IsPrivateKeyLoaded => State == RsaWorkflowState.WaitingForEncryptedFile;
    public bool IsRsaEncryptedFileLoaded => State == RsaWorkflowState.WaitingForPrivateKey;
    public bool IsWaitingForPublicKey => State == RsaWorkflowState.WaitingForPublicKey;

    public string KeyGuid
    {
        get => _keyGuid;
        set => SetProperty(ref _keyGuid, value);
    }

    public string DroppedFileName
    {
        get => _droppedFileName;
        set => SetProperty(ref _droppedFileName, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    #endregion

    #region Commands

    public ICommand GenerateKeyCommand { get; }
    public ICommand CancelCommand { get; }

    #endregion

    #region RSA 鍵ペア生成

    public async Task GenerateKeyPairAsync()
    {
        var result = await _mainViewModel.ShowSaveFileDialogAsync(
            _localize["PublicKey_SaveKeyPairTitle"],
            "PublicPairKey",
            new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType(_localize["PublicKey_AllFiles"])
                    { Patterns = new[] { "*.*" } }
            });
        if (result == null) return;

        var directory = Path.GetDirectoryName(result) ??
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var baseFileName = Path.GetFileNameWithoutExtension(result);

        // ".atcpub" のように拡張子だけの名前だと GetFileNameWithoutExtension が
        // 空を返し、鍵ファイルが ".atcpub" / ".atcpvt"（名前無し）になってしまう
        if (string.IsNullOrEmpty(baseFileName))
        {
            Message = _localize["PublicKey_InvalidBaseFileName"];
            return;
        }

        var (publicKeyPath, privateKeyPath) = await Task.Run(() =>
            _keyGenerator.GenerateKeyPair(directory, baseFileName));
        Message = _localize.GetString("PublicKey_KeyPairGeneratedMessage",
            Path.GetFileName(publicKeyPath), Path.GetFileName(privateKeyPath));
    }

    #endregion

    #region ファイルドロップ

    private void Cancel()
    {
        _droppedKeyPath = null;
        _droppedFilePath = null;
        State = RsaWorkflowState.Initial;
        _mainViewModel.BackToMain();
    }

    public void OnFileDropped(string filePath, int fileType)
    {
        fileType = NormalizeFileType(filePath, fileType);
        switch (State)
        {
            case RsaWorkflowState.Initial:
                HandleInitialDrop(filePath, fileType);
                break;
            case RsaWorkflowState.WaitingForFile:
                HandleWaitingForFileDrop(filePath, fileType);
                break;
            case RsaWorkflowState.WaitingForEncryptedFile:
                HandleWaitingForEncryptedFileDrop(filePath, fileType);
                break;
            case RsaWorkflowState.WaitingForPublicKey:
                HandleWaitingForPublicKeyDrop(filePath, fileType);
                break;
            case RsaWorkflowState.WaitingForPrivateKey:
                HandleWaitingForPrivateKeyDrop(filePath, fileType);
                break;
        }
    }

    /// <summary>
    /// FileTypeDetector は旧 AttacheCase 4 の RSA 署名だけを 4（RSA 暗号化データ）と判定し、
    /// 5.5 が書く RSA 鍵ファイル方式の v150 ファイルは 1（ATC）/ 2（EXE）になる。
    /// この画面では「秘密鍵で復号するファイル」として同じに扱いたいので 4 に寄せる
    /// （2026-10-26: ここで弾かれていたため、秘密鍵のあとに .atc を落としても無反応だった）。
    /// </summary>
    private static int NormalizeFileType(string filePath, int fileType)
    {
        if (fileType is 1 or 2 &&
            DecryptionService.GetPublicKeyEncryptionKind(filePath) == PublicKeyEncryptionKind.RsaKeyFile)
            return 4;
        return fileType;
    }

    private void HandleInitialDrop(string filePath, int fileType)
    {
        switch (fileType)
        {
            // 通常ファイル → 公開鍵待ち、公開鍵 → 暗号化するファイル待ち。
            // 5.0 で一度畳んだ入口を 5.5 で復活させた（利用者判断 2026-09-12）。
            // 再び畳むときは、この 2 つを
            //   Message = _localize["PublicKey_EncryptionRetiredMessage"]; に戻す
            case 0:
                _droppedFilePath = filePath;
                DroppedFileName = Path.GetFileName(filePath);
                State = RsaWorkflowState.WaitingForPublicKey;
                break;
            case 5:
                _droppedKeyPath = filePath;
                LoadKeyGuid(filePath);
                State = RsaWorkflowState.WaitingForFile;
                break;
            case 4:
                _droppedFilePath = filePath;
                DroppedFileName = Path.GetFileName(filePath);
                State = RsaWorkflowState.WaitingForPrivateKey;
                break;
            case 6:
                _droppedKeyPath = filePath;
                LoadKeyGuid(filePath);
                State = RsaWorkflowState.WaitingForEncryptedFile;
                break;
            default:
                // 1/2/3/7（ATC/EXE/ZIP）など、この画面で扱えない種別。
                // 無反応だと利用者が固まったように感じるので、案内を出す
                Message = _localize["PublicKey_ErrorDropUnsupported"];
                break;
        }
    }

    private void LoadKeyGuid(string keyPath)
    {
        try
        {
            var xml = System.Xml.Linq.XElement.Load(keyPath);
            KeyGuid = xml.Element("id")?.Value ?? _localize["PublicKey_NoGuid"];
        }
        catch
        {
            KeyGuid = _localize["PublicKey_ReadError"];
        }
    }

    private void HandleWaitingForFileDrop(string filePath, int fileType)
    {
        if (fileType is 4 or 5 or 6)
        {
            Message = _localize["PublicKey_ErrorDropNormalFile"];
            return;
        }
        _mainViewModel.StartRsaEncryption(_droppedKeyPath!, filePath);
    }

    private void HandleWaitingForEncryptedFileDrop(string filePath, int fileType)
    {
        if (fileType != 4)
        {
            Message = _localize["PublicKey_ErrorDropEncryptedFile"];
            return;
        }
        _mainViewModel.StartRsaDecryption(_droppedKeyPath!, filePath);
    }

    private void HandleWaitingForPublicKeyDrop(string filePath, int fileType)
    {
        if (fileType != 5)
        {
            Message = _localize["PublicKey_ErrorDropPublicKey"];
            return;
        }
        _droppedKeyPath = filePath;
        LoadKeyGuid(filePath);
        _mainViewModel.StartRsaEncryption(_droppedKeyPath, _droppedFilePath!);
    }

    private void HandleWaitingForPrivateKeyDrop(string filePath, int fileType)
    {
        if (fileType == 5)
        {
            Message = _localize["PublicKey_ErrorDropPrivateKeyNotPublic"];
            return;
        }
        if (fileType != 6)
        {
            Message = _localize["PublicKey_ErrorDropPrivateKey"];
            return;
        }
        _droppedKeyPath = filePath;
        LoadKeyGuid(filePath);
        _mainViewModel.StartRsaDecryption(_droppedKeyPath, _droppedFilePath!);
    }

    #endregion

    private void UpdateMessage()
    {
        Message = State switch
        {
            // 鍵読込済みの 2 状態は案内文をビュー側に固定で持つので、Message はエラー表示専用（空で始める）
            RsaWorkflowState.WaitingForFile => string.Empty,
            RsaWorkflowState.WaitingForEncryptedFile => string.Empty,
            RsaWorkflowState.WaitingForPublicKey =>
                _localize.GetString("PublicKey_WaitingForPublicKeyMessage", DroppedFileName),
            RsaWorkflowState.WaitingForPrivateKey =>
                _localize.GetString("PublicKey_WaitingForPrivateKeyMessage", DroppedFileName),
            _ => string.Empty,
        };
    }

    public void Reset()
    {
        _droppedKeyPath = null;
        _droppedFilePath = null;
        KeyGuid = string.Empty;
        DroppedFileName = string.Empty;
        State = RsaWorkflowState.Initial;
    }
}
