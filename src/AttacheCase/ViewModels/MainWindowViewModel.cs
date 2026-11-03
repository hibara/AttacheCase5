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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
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

public class MainWindowViewModel : ViewModelBase
{
    private readonly IDecryptionService _decryptionService;
    private readonly IEncryptionService _encryptionService;
    private readonly IFileTypeDetector _fileTypeDetector;
    private readonly LocalizationService _localize = LocalizationService.Instance;
    
    private ViewModelBase _currentView;
    private int _detectedFileType = -1;
    private string _dropMessage = string.Empty;
    private string _droppedPath = string.Empty;
    private MenuItemModel? _selectedMenuItem;

    /// <summary>
    ///   /exit=1 が指定され、コマンドラインからの処理が走っている状態。
    ///   処理が終わってメイン画面に戻る時点でアプリを終了する。
    /// </summary>
    private bool _isSettingsSelected;

    private bool _exitAfterProcess;

    /// <summary>/exit=1 で終了するときに返す終了コード</summary>
    private int _commandLineExitCode = AtcExitCode.Success;
    // 公開プロパティが string? なので、フィールド側も null 許容で揃える
    private string? _xmlPrivateKeyString;
    private string? _xmlPublicKeyString;

    public MainWindowViewModel(
        IFileTypeDetector? fileTypeDetector = null,
        IEncryptionService? encryptionService = null,
        IDecryptionService? decryptionService = null)
    {
        _fileTypeDetector = fileTypeDetector ?? new FileTypeDetectorService();
        _encryptionService = encryptionService ?? new EncryptionService();
        _decryptionService = decryptionService ?? new DecryptionService();
        _currentView = this;

        // 初期メッセージを設定
        _dropMessage = _localize["Main_DropMessage"];

        // メニュー項目の初期化
        MenuItems = new ObservableCollection<MenuItemModel>
        {
            new()
            {
                Type = MenuItemType.Encrypt,
                Title = _localize["Menu_Encryption"] + Environment.NewLine + "(ATC)",
                IconPath = ThemedIconPath("encryption")
            },
            new()
            {
                Type = MenuItemType.Decrypt,
                Title = _localize["Menu_Decryption"] + Environment.NewLine + "(ATC)",
                IconPath = ThemedIconPath("decryption")
            },
            new()
            {
                Type = MenuItemType.PublicKey,
                Title = _localize["Menu_Rsa"],
                IconPath = ThemedIconPath("rsa")
            },
            new()
            {
                Type = MenuItemType.PasswordZip,
                Title = _localize["Menu_ZipPassword"],
                IconPath = ThemedIconPath("zip")
            },
            new()
            {
                Type = MenuItemType.PostPpap,
                Title = _localize["Menu_PostPpap"],
                IconPath = ThemedIconPath("post-ppap")
            }
        };

        SelectedMenuItem = MenuItems[0];
        MenuItemClickCommand = new RelayCommand<MenuItemModel>(OnMenuItemClick);
        OpenSettingsCommand = new RelayCommand(ShowSettingsView);
    }

    public ObservableCollection<MenuItemModel> MenuItems { get; }

    /// <summary>左下の歯車アイコン（テーマに応じて明暗を選ぶ）。</summary>
    public string SettingsIconPath => ThemedIconPath("settings");

    /// <summary>
    /// テーマに応じたサイドバーアイコンのパス。
    /// SVG は中に色（黒線）を持っていてテーマでは変わらないため、
    /// ダークでは明色化した複製（Assets/Icons/dark/）を使う。
    /// テーマ変更時はウィンドウごと作り直されるので、生成時の解決で足りる
    /// （App.OnSettingsSaved がテーマ変更でも RecreateMainWindow を呼ぶ）。
    /// </summary>
    private static string ThemedIconPath(string name)
    {
        var dark = Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        return $"avares://AttacheCase/Assets/Icons/{(dark ? "dark/" : "")}{name}.svg";
    }

    private MenuItemModel? SelectedMenuItem
    {
        get => _selectedMenuItem;
        set
        {
            // 前の選択を解除する（選び直しのときは解除しない）
            if (_selectedMenuItem != null && !ReferenceEquals(_selectedMenuItem, value))
                _selectedMenuItem.IsSelected = false;

            // 同じ項目を選び直したときも選択表示は付け直す。
            // ここで早期 return すると、解除だけして付け直されず選択色が消える
            SetProperty(ref _selectedMenuItem, value);

            if (_selectedMenuItem != null) _selectedMenuItem.IsSelected = true;

            OnPropertyChanged(nameof(IsZipGuideVisible));
        }
    }

    public ICommand MenuItemClickCommand { get; }

    /// <summary>左下の歯車から設定画面を開く</summary>
    public ICommand OpenSettingsCommand { get; }

    public ViewModelBase CurrentView
    {
        get => _currentView;
        set
        {
            if (!SetProperty(ref _currentView, value)) return;

            OnPropertyChanged(nameof(IsMainView));
            OnPropertyChanged(nameof(IsZipGuideVisible));
            IsSettingsSelected = value is SettingsViewModel;
        }
    }

    public bool IsMainView => CurrentView == this;

    #region パスワード ZIP のガイド（メイン画面）

    /// <summary>
    /// 「パスワードZIP」選択中のメイン画面に、ドロップの案内と方式選択を出すか。
    /// 旧 Windows 版の ZIP タブ相当（ここで選んだ方式はその場で設定に保存される）。
    /// </summary>
    public bool IsZipGuideVisible => IsMainView && _selectedMenuItem?.Type == MenuItemType.PasswordZip;

    private static int ZipAlgorithmSetting => SettingsService.Instance.Current.ZipAlgorithm;

    public bool IsZipGuideNone
    {
        get => ZipAlgorithmSetting == 3;
        set { if (value) SetZipAlgorithmSetting(3); }
    }

    public bool IsZipGuideStandard
    {
        get => ZipAlgorithmSetting == 0;
        set { if (value) SetZipAlgorithmSetting(0); }
    }

    public bool IsZipGuideAes128
    {
        get => ZipAlgorithmSetting == 1;
        set { if (value) SetZipAlgorithmSetting(1); }
    }

    public bool IsZipGuideAes256
    {
        get => ZipAlgorithmSetting == 2;
        set { if (value) SetZipAlgorithmSetting(2); }
    }

    private void SetZipAlgorithmSetting(int value)
    {
        var settings = SettingsService.Instance.Current;
        if (settings.ZipAlgorithm == value) return;

        settings.ZipAlgorithm = value;
        SettingsService.Instance.Save(settings);

        OnPropertyChanged(nameof(IsZipGuideNone));
        OnPropertyChanged(nameof(IsZipGuideStandard));
        OnPropertyChanged(nameof(IsZipGuideAes128));
        OnPropertyChanged(nameof(IsZipGuideAes256));
    }

    #endregion

    /// <summary>
    ///   ほかのプロセスから渡された処理を受け付けられる状態か。
    ///   何かの処理中に横から差し替えられると困るので、
    ///   メイン画面か設定画面を出しているときだけ受け付ける。
    /// </summary>
    public bool CanAcceptCommandLine => CurrentView is MainWindowViewModel or SettingsViewModel;

    /// <summary>
    ///   コマンドライン引数で設定項目を上書きした起動か。設定画面が
    ///   「引数で指定した値はここには保存されない」注記を出すかどうかに使う
    ///   （引数なしの通常起動で出すと、何のことか分からないため。利用者指摘 2026-10-25）。
    ///   起動時の引数も、起動中のインスタンスへ転送されてきた引数も、ApplyCommandLineAsync が通る
    /// </summary>
    public bool HasCommandLineSettingOverride { get; private set; }

    /// <summary>
    ///   設定画面を表示中か。左下の歯車の背景色に使う。
    ///   表示中はメニュー項目の選択表示を一時的に外し、選択箇所が 2 つに見えないようにする。
    /// </summary>
    public bool IsSettingsSelected
    {
        get => _isSettingsSelected;
        private set
        {
            if (!SetProperty(ref _isSettingsSelected, value)) return;

            // 選択中のメニュー項目そのものは変えない（ドロップ時の振り分けに使うため）
            if (_selectedMenuItem != null) _selectedMenuItem.IsSelected = !value;
        }
    }

    public string DropMessage
    {
        get => _dropMessage;
        set => SetProperty(ref _dropMessage, value);
    }

    public string DroppedPath
    {
        get => _droppedPath;
        set => SetProperty(ref _droppedPath, value);
    }

    private int DetectedFileType
    {
        get => _detectedFileType;
        set => SetProperty(ref _detectedFileType, value);
    }

    /// <summary>
    ///   読み込まれた公開鍵の文字列
    /// </summary>
    public string? XmlPublicKeyString
    {
        get => _xmlPublicKeyString;
        set => SetProperty(ref _xmlPublicKeyString, value);
    }

    /// <summary>
    ///   読み込まれた秘密鍵の文字列
    /// </summary>
    public string? XmlPrivateKeyString
    {
        get => _xmlPrivateKeyString;
        set => SetProperty(ref _xmlPrivateKeyString, value);
    }

    private void OnMenuItemClick(MenuItemModel? menuItem)
    {
        if (menuItem == null) return;

        SelectedMenuItem = menuItem;

        // Post-PPAP メニューが選択された場合
        if (menuItem.Type == MenuItemType.PostPpap)
        {
            ShowPostPpapView();
            return; // ここで終了（BackToMainを呼ばない）
        }

        // RSA 公開鍵暗号メニューが選択された場合
        if (menuItem.Type == MenuItemType.PublicKey)
        {
            ShowPublicKeyView();
            return; // ここで終了（BackToMainを呼ばない）
        }

        // それ以外はメイン画面に戻る
        BackToMain();

        DropMessage = menuItem.Type switch
        {
            MenuItemType.Encrypt => _localize["Main_DropFilesToEncrypt"],
            MenuItemType.Decrypt => _localize["Main_DropFilesToDecrypt"],
            MenuItemType.PasswordZip => _localize["ZipPassword_DropFiles"],
            _ => _localize["Main_DropMessage"]
        };
    }

    public void OnFilesDropped(IEnumerable<string> paths)
    {
        var pathList = paths.ToList();

        if (pathList.Count == 0)
            return;

        var path = pathList[0];
        DroppedPath = path;

        // 種類の判定は先頭の項目で行う。暗号化へ進むときは落とされた全項目を渡す
        // （設定「複数ファイルの場合」で、1 つにまとめるか 1 つずつかが決まる）
        DetectedFileType = _fileTypeDetector.CheckFileType(path);

        ProcessDroppedItem(path, DetectedFileType, pathList);
    }

    /// <param name="path">先頭の項目（種類の判定に使う）</param>
    /// <param name="fileType">先頭の項目の種類</param>
    /// <param name="allPaths">落とされた全項目。暗号化（通常ファイル・フォルダー）のときだけ使う</param>
    private void ProcessDroppedItem(string path, int fileType, IReadOnlyList<string>? allPaths = null)
    {
        // 設定「パスワードファイルのドラッグ＆ドロップを許可する」
        // パスワード入力中の画面へのドロップは、暗号化対象ではなくパスワードファイルとして扱う
        if (SettingsService.Instance.Current.AllowPasswordFileDragDrop && File.Exists(path))
        {
            switch (CurrentView)
            {
                case EncryptionViewModel { IsProcessingStep: false } encryptViewModel:
                    _ = encryptViewModel.UsePasswordFileAsync(path);
                    return;

                case DecryptionViewModel { IsPasswordInputStep: true } decryptViewModel:
                    _ = decryptViewModel.UsePasswordFileAsync(path);
                    return;
            }
        }

        // Post-PPAP 画面を表示中の場合
        if (CurrentView is PostPpapViewModel postPpapVm)
        {
            postPpapVm.OnFileDropped(path, fileType);
            return;
        }

        // RSA 公開鍵暗号画面を表示中の場合
        if (CurrentView is PublicKeyViewModel)
        {
            OnPublicKeyFileDropped(path, fileType);
            return;
        }

        var isDirectory = Directory.Exists(path);
        var displayName = isDirectory
            ? new DirectoryInfo(path).Name
            : Path.GetFileName(path);

        switch (fileType)
        {
            case 1: // ATCファイル
            case 2: // EXE(ATC)ファイル
                // 5.5 の RSA 鍵ファイル方式（v150 公開鍵方式・宛先は RSA 単体）は先頭 20 バイトが通常の
                // ATC と同じなので種別 1/2 になる。パスワード画面へ送っても入れるパスワードが無いため、
                // 秘密鍵（.atcpvt）待ちの公開鍵暗号画面へ回す（種別 4 = RSA 暗号化データとして扱う）
                if (DecryptionService.GetPublicKeyEncryptionKind(path) == PublicKeyEncryptionKind.RsaKeyFile)
                    ShowPublicKeyViewWithFile(path, 4);
                else
                    ShowDecryptionView(path);
                break;

            case 3: // ZIPファイル → ZIP復号（パスワード解凍）
                CurrentView = new ZipPasswordDecryptionViewModel(this, path);
                break;

            case 4: // RSA暗号化データ
                // RSA暗号化ファイルがドロップされた → PublicKeyViewに遷移
                ShowPublicKeyViewWithFile(path, fileType);
                break;

            case 5: // RSA公開鍵
            case 6: // RSA秘密鍵
                // RSA鍵がドロップされた → PublicKeyViewに遷移
                ShowPublicKeyViewWithFile(path, fileType);
                break;

            case 7: // ZIPファイル
                ShowZipDecryptionView(path);
                break;

            case 0: // その他のファイルまたはフォルダー
            default:
                if (SelectedMenuItem?.Type == MenuItemType.PasswordZip)
                    ShowZipPasswordEncryptionView(allPaths?.ToArray() ?? [path]);
                else if (SelectedMenuItem?.Type == MenuItemType.PostPpap)
                    // Post-PPAP モードでファイルがドロップされた
                    ShowPostPpapViewWithFile(path, fileType);
                else if (SelectedMenuItem?.Type == MenuItemType.PublicKey)
                    // RSA 公開鍵暗号モードでファイルがドロップされた
                    OnPublicKeyFileDropped(path, fileType);
                else
                    ShowEncryptionView(allPaths?.ToArray() ?? [path]);

                break;
        }
    }

    /// <summary>
    ///   公開鍵暗号画面を表示（ファイル付き）
    /// </summary>
    private void ShowPublicKeyViewWithFile(string filePath, int fileType)
    {
        // 左メニューの公開鍵暗号を選択状態にする
        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Type == MenuItemType.PublicKey);

        // PublicKeyViewModelを作成して表示
        var viewModel = new PublicKeyViewModel(this);
        CurrentView = viewModel;

        // ドロップされたファイルを処理
        viewModel.OnFileDropped(filePath, fileType);
    }

    private void ShowZipPasswordEncryptionView(string[] targetPaths)
    {
        // settings.json の ZipAlgorithm が範囲外だと ViewModel の生成が
        // 例外で落ちる。画面を作る前に確かめ、不正なら案内してメイン画面へ戻す
        var algo = SettingsService.Instance.Current.ZipAlgorithm;
        if (!ZipPasswordEncryptionViewModel.IsSupportedAlgorithm(algo))
        {
            _ = DialogHelper.NotifyAsync(
                false,
                _localize["Dialog_ErrorTitle"],
                _localize.GetString("Error_InvalidZipAlgorithm", algo));
            BackToMain();
            return;
        }

        CurrentView = new ZipPasswordEncryptionViewModel(this, targetPaths);
    }

    private void ShowZipDecryptionView(string targetPath)
    {
        // まずパスワードなしで試行
        ShowDecryptionView(targetPath, true); // ZIP解凍モード
    }

    private void ShowEncryptionView(string[] targetPaths, int? initialFileType = null)
    {
        CurrentView = new EncryptionViewModel(this, targetPaths, _encryptionService, initialFileType);
    }

    private void ShowDecryptionView(string targetPath, bool isZipMode = false)
    {
        var service = isZipMode
            ? (IDecryptionService)new ZipPasswordDecryptionService()
            : new DecryptionService();

        CurrentView = new DecryptionViewModel(this, targetPath, service);
    }

    public void BackToMain()
    {
      // /exit=1: コマンドラインからの処理が一区切りしたので終了する
      if (_exitAfterProcess)
      {
        FinishCommandLineRun();
        return;
      }

      CurrentView = this;
      DroppedPath = string.Empty;
      DetectedFileType = -1;
      DropMessage = SelectedMenuItem?.Type switch
      {
        MenuItemType.Encrypt => _localize["Main_DropFilesToEncrypt"],
        MenuItemType.Decrypt => _localize["Main_DropFilesToDecrypt"],
        MenuItemType.PostPpap => _localize["PublicKey_DropFilesToEncrypt"],
        MenuItemType.PublicKey => _localize["PublicKey_DropFilesToDecrypt"],
        MenuItemType.PasswordZip => _localize["ZipPassword_DropFiles"],
        _ => _localize["Main_DropMessage"]
      };
    }

    public bool CanAcceptDrop(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            // ディレクトリは常に受け入れ可能
            if (Directory.Exists(path)) return true;

            // ファイルの場合はヘッダー情報を判定
            if (!File.Exists(path)) continue;

            var fileType = _fileTypeDetector.CheckFileType(path);
            // 有効なファイルタイプ
            // 0: 通常ファイル/フォルダー
            // 1: ATCファイル
            // 2: EXE(ATC)ファイル
            // 3: ZIPファイル
            // 4: RSA暗号化データ
            // 5: RSA公開鍵（XML）
            // 6: RSA秘密鍵（XML）
            if (fileType is 0 or 1 or 2 or 3 or 4 or 5 or 6 or 7)
                return true;
        }

        return false;
    }

    /// <summary>
    ///   公開鍵暗号画面でファイルがドロップされた時の処理
    /// </summary>
    private void OnPublicKeyFileDropped(string filePath, int fileType)
    {
        if (CurrentView is PublicKeyViewModel viewModel) viewModel.OnFileDropped(filePath, fileType);
    }

    /// <summary>
    ///   ファイル保存ダイアログを表示
    /// </summary>
    public async Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName,
        FilePickerFileType[] fileTypes)
    {
        var window = Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return null;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            FileTypeChoices = fileTypes
        });

        return file?.Path.LocalPath;
    }

    /// <summary>
    ///   RSA 公開鍵暗号画面を表示
    /// </summary>
    private void ShowPublicKeyView()
    {
        CurrentView = new PublicKeyViewModel(this);
    }

    /// <summary>
    ///   Esc キーの振り分け。処理中（進捗バーが動いているとき）なら「中止」を実行して true を返す。
    ///   false のときは呼び出し側（MainWindow）がアプリを終了する（旧版 5.0 以前と同じ:
    ///   Esc = 終了、処理中だけは Esc = 中止。利用者指示 2026-10-25）。
    ///   Post-PPAP の通信中は中止できないので何もせず true（終了もしない）
    /// </summary>
    public bool HandleEscapeKey()
    {
        switch (CurrentView)
        {
            case EncryptionViewModel { IsProcessing: true } e:
                e.CancelCommand.Execute(null);
                return true;
            case DecryptionViewModel { IsProcessingStep: true } d:
                d.CancelCommand.Execute(null);
                return true;
            case ZipPasswordEncryptionViewModel { IsProcessing: true } z:
                z.CancelCommand.Execute(null);
                return true;
            case ZipPasswordDecryptionViewModel { IsProcessing: true } zd:
                zd.CancelCommand.Execute(null);
                return true;
            case PostPpapViewModel { IsBusy: true }:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    ///   設定画面を表示
    /// </summary>
    private void ShowSettingsView()
    {
        // 実処理（暗号化・復号・通信）の最中だけは開かない。進捗の裏で設定を
        // 書き換えられると分かりにくいため。それ以外はどの画面からでも開ける
        //（以前は「メイン画面のときだけ」で、Post-PPAP などの画面から
        //  設定へ飛べなくなっていた）
        var busy = CurrentView switch
        {
            EncryptionViewModel e => e.IsProcessing,
            DecryptionViewModel d => d.IsProcessingStep,
            ZipPasswordEncryptionViewModel z => z.IsProcessing,
            ZipPasswordDecryptionViewModel zd => zd.IsProcessing,
            PostPpapViewModel p => p.IsBusy,
            SettingsViewModel => true, // すでに設定画面
            _ => false,
        };
        if (busy) return;

        CurrentView = new SettingsViewModel(this);
    }

    /// <summary>
    ///   Post-PPAP 画面を表示
    /// </summary>
    private void ShowPostPpapView()
    {
        CurrentView = new PostPpapViewModel(this);
    }

    /// <summary>
    ///   Post-PPAP 画面を表示（ファイル付き）
    /// </summary>
    private void ShowPostPpapViewWithFile(string filePath, int fileType)
    {
        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Type == MenuItemType.PostPpap);

        var viewModel = new PostPpapViewModel(this);
        CurrentView = viewModel;
        viewModel.PrepareEncryptionTarget(filePath, fileType);
    }

    /// <summary>
    /// 鍵ファイル（.atcpub / .atcpvt）を読む。
    /// 無い・読めない・文字コードが壊れている場合は null を返し、利用者に伝える。
    /// 未処理例外にすると、CLI 経路では終了コードを返さないまま異常終了してしまう。
    /// </summary>
    private string? TryReadKeyFile(string path, bool unattended = false)
    {
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read the key file: {path}: {ex.Message}");
            // /exit=1 の無人実行ではモーダルダイアログを出さない。誰も閉じられず、
            // 直後の終了処理と競合して待機状態が残る（DialogHelper が unattended で
            // 標準エラー出力へ回す）
            _ = DialogHelper.NotifyAsync(
                unattended,
                _localize["Dialog_ErrorTitle"],
                _localize.GetString("PublicKey_KeyFileReadFailed", Path.GetFileName(path)));
            return null;
        }
    }

    /// <summary>
    /// RSA暗号化を開始
    /// </summary>
    public void StartRsaEncryption(string publicKeyPath, string targetFilePath)
    {
        // 公開鍵のXMLを読み込む
        var publicKeyXml = TryReadKeyFile(publicKeyPath);
        if (publicKeyXml == null)
        {
            BackToMain();
            return;
        }

        // RSAモードでEncryptionViewModelを作成
        CurrentView = new EncryptionViewModel(this, targetFilePath, _encryptionService, publicKeyXml);
    }

    /// <summary>
    /// RSA復号を開始
    /// </summary>
    public void StartRsaDecryption(string privateKeyPath, string encryptedFilePath)
    {
        // 秘密鍵のXMLを読み込む
        var privateKeyXml = TryReadKeyFile(privateKeyPath);
        if (privateKeyXml == null)
        {
            BackToMain();
            return;
        }

        // RSAモードでDecryptionViewModelを作成
        CurrentView = new DecryptionViewModel(this, encryptedFilePath, _decryptionService, privateKeyXml);
    }

    /// <summary>
    /// メニューから暗号化を開始（ファイル/フォルダー選択後）
    /// </summary>
    /// <param name="initialFileType">
    /// 暗号化画面のコンボボックスの初期選択（ATC / EXE）。パスワード ZIP 画面から切り替えて来たときに渡す。
    /// null なら保存されている種類
    /// </param>
    public void StartEncryption(string[] paths, int? initialFileType = null)
    {
        if (paths.Length == 0) return;

        // 左メニューの暗号化を選択状態にする
        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Type == MenuItemType.Encrypt);

        // 複数ファイル/フォルダーはそのまま渡す（設定「複数ファイルの場合」でまとめ方が決まる）
        ShowEncryptionView(paths, initialFileType);
    }

    /// <summary>
    /// メニューから復号を開始（ファイル選択後）
    /// </summary>
    public void StartDecryption(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        // 左メニューの復号を選択状態にする
        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Type == MenuItemType.Decrypt);

        ShowDecryptionView(path);
    }

    /// <summary>
    /// メニューからZIP暗号化を開始（ファイル/フォルダー選択後）
    /// </summary>
    public void StartZipEncryption(string[] paths)
    {
        if (paths.Length == 0) return;

        // 左メニューのパスワードZIPを選択状態にする
        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Type == MenuItemType.PasswordZip);

        ShowZipPasswordEncryptionView(paths);
    }

    /// <summary>
    /// メニューからZIP復号を開始（ファイル選択後）
    /// </summary>
    public void StartZipDecryption(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        // 左メニューのパスワードZIPを選択状態にする
        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Type == MenuItemType.PasswordZip);

        CurrentView = new ZipPasswordDecryptionViewModel(this, path);
    }

    /// <summary>
    /// メニューから鍵ペアを生成
    /// </summary>
    public async Task GenerateKeyPairFromMenuAsync(Avalonia.Controls.Window parentWindow)
    {
        // 左メニューの公開鍵暗号を選択状態にする
        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Type == MenuItemType.PublicKey);

        // PublicKeyViewModelを作成して表示
        var viewModel = new PublicKeyViewModel(this);
        CurrentView = viewModel;

        // 鍵生成を実行
        await viewModel.GenerateKeyPairAsync();
    }

    /// <summary>
    /// メニューから公開鍵を読み込み
    /// </summary>
    public void LoadPublicKey(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        // 左メニューの公開鍵暗号を選択状態にする
        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Type == MenuItemType.PublicKey);

        // PublicKeyViewModelを作成して表示
        var viewModel = new PublicKeyViewModel(this);
        CurrentView = viewModel;

        // 公開鍵ファイルを処理（fileType = 5）
        viewModel.OnFileDropped(path, 5);
    }

    /// <summary>
    /// メニューから秘密鍵を読み込み
    /// </summary>
    public void LoadPrivateKey(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        // 左メニューの公開鍵暗号を選択状態にする
        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Type == MenuItemType.PublicKey);

        // PublicKeyViewModelを作成して表示
        var viewModel = new PublicKeyViewModel(this);
        CurrentView = viewModel;

        // 秘密鍵ファイルを処理（fileType = 6）
        viewModel.OnFileDropped(path, 6);
    }

    #region コマンドライン

    /// <summary>
    ///   コマンドライン引数の指定を画面に反映し、必要なら処理を開始する。
    ///   ウィンドウが表示されたあとに一度だけ呼ぶ。
    /// </summary>
    public async Task ApplyCommandLineAsync(CliOptions cli)
    {
        // 保存されている設定の複製に、引数で指定された項目だけを上書きする。
        // 複製なので、この実行でどう変わっても settings.json には書き戻らない。
        var settings = cli.ApplyTo(SettingsService.Instance.Current.Clone());
        if (cli.HasSettingOverride) HasCommandLineSettingOverride = true;

        // /genrsa: 鍵ペアを生成して終わり
        if (!string.IsNullOrEmpty(cli.GenerateRsaKeyOutputDir))
        {
            _exitAfterProcess = settings.ExitAfterProcess;
            await GenerateRsaKeyPairFromCommandLineAsync(cli.GenerateRsaKeyOutputDir);
            FinishCommandLineRun();
            return;
        }

        var paths = cli.InputPaths
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .ToArray();

        // 対象が無い場合は、左メニューだけ合わせてドラッグ＆ドロップを待つ
        if (paths.Length == 0)
        {
            SelectMenu(cli.Action == CliAction.Decrypt ? MenuItemType.Decrypt : MenuItemType.Encrypt);
            BackToMain();
            return;
        }

        DroppedPath = paths[0];
        var fileType = _fileTypeDetector.CheckFileType(paths[0]);
        DetectedFileType = fileType;

        // 実際に処理を始めるときだけ /exit を有効にする
        // （対象が無くドラッグ＆ドロップを待つだけの起動では終了させない）
        _exitAfterProcess = settings.ExitAfterProcess;

        if (ResolveAction(cli, fileType) == CliAction.Decrypt)
        {
            await StartDecryptionFromCommandLineAsync(cli, BuildRunContext(cli, settings, true), paths, fileType);
        }
        else
        {
            await StartEncryptionFromCommandLineAsync(cli, BuildRunContext(cli, settings, false), paths);
        }
    }

    /// <summary>
    ///   コマンドライン引数から、この実行に使う条件を組み立てる。
    /// </summary>
    private static RunContext BuildRunContext(CliOptions cli, AppSettings settings, bool forDecryption)
    {
        // パスワードファイルはパスワード文字列より優先される（Windows 版 GUI と同じ）
        var passwordFile = forDecryption ? cli.DecryptPasswordFilePath : cli.EncryptPasswordFilePath;
        byte[]? passwordBinary = null;
        if (!string.IsNullOrEmpty(passwordFile) && File.Exists(passwordFile))
        {
            passwordBinary = PasswordFileHash.ComputeSha256(passwordFile);
        }

        return new RunContext
        {
            Settings = settings,
            IsCommandLineRun = true,
            Password = passwordBinary == null ? cli.Password : null,
            PasswordBinary = passwordBinary,
            OutputPath = cli.OutputPath,
        };
    }

    /// <summary>
    ///   /en も /de も指定されていない場合、先頭ファイルの内容から処理を決める。
    /// </summary>
    private static CliAction ResolveAction(CliOptions cli, int fileType)
    {
        if (cli.Action is CliAction.Encrypt or CliAction.Decrypt) return cli.Action;

        // 秘密鍵や登録済みアカウントの指定があれば復号の意図とみなす
        if (cli.RsaPrivateKeyFilePath != null || cli.UsePostPpapAccounts) return CliAction.Decrypt;
        if (cli.RsaPublicKeyFilePath != null || cli.PostPpapRecipientEmail != null) return CliAction.Encrypt;

        // 1: ATC, 2: EXE(ATC), 3: 暗号化 ZIP, 4: RSA 暗号化データ, 7: ZIP
        return fileType is 1 or 2 or 3 or 4 or 7 ? CliAction.Decrypt : CliAction.Encrypt;
    }

    private async Task StartDecryptionFromCommandLineAsync(
        CliOptions cli, RunContext context, string[] paths, int fileType)
    {
        // /de を指定されたのに暗号化ファイルではない場合、
        // パスワード違いとして扱うと紛らわしいので、形式が違うと伝えて終わる
        // 1: ATC, 2: EXE(ATC), 3: 暗号化 ZIP, 4: RSA 暗号化データ, 7: ZIP
        if (fileType is not (1 or 2 or 3 or 4 or 7) && cli.RsaPrivateKeyFilePath == null)
        {
            await DialogHelper.NotifyAsync(
                context.IsUnattended,
                _localize["Dialog_ErrorTitle"],
                _localize.GetString("Error_NotEncryptedFile", paths[0]));
            ReportCommandLineResult(AtcExitCode.UnsupportedFormat);
            BackToMain();
            return;
        }

        // RSA 秘密鍵（.atcpvt）が指定されている場合
        if (cli.RsaPrivateKeyFilePath != null)
        {
            SelectMenu(MenuItemType.PublicKey);
            var privateKeyXml = TryReadKeyFile(cli.RsaPrivateKeyFilePath, context.IsUnattended);
            if (privateKeyXml == null)
            {
                ReportCommandLineResult(AtcExitCode.UsageError);
                BackToMain();
                return;
            }
            CurrentView = new DecryptionViewModel(this, paths, new DecryptionService(), context, privateKeyXml);
            return;
        }

        // パスワード付き ZIP
        if (fileType is 3 or 7)
        {
            SelectMenu(MenuItemType.PasswordZip);
            CurrentView = new ZipPasswordDecryptionViewModel(this, paths[0], context);
            return;
        }

        SelectMenu(MenuItemType.Decrypt);
        CurrentView = new DecryptionViewModel(this, paths, new DecryptionService(), context);
    }

    private async Task StartEncryptionFromCommandLineAsync(CliOptions cli, RunContext context, string[] paths)
    {
        // Post-PPAP（宛先メールアドレス指定）
        if (!string.IsNullOrEmpty(cli.PostPpapRecipientEmail))
        {
            SelectMenu(MenuItemType.PostPpap);
            var postPpapViewModel = new PostPpapViewModel(this);
            CurrentView = postPpapViewModel;
            var succeeded = await postPpapViewModel.StartEncryptionFromCommandLineAsync(
                paths[0], cli.PostPpapRecipientEmail, context);
            if (!succeeded) ReportCommandLineResult(AtcExitCode.GeneralError);
            FinishCommandLineRun();
            return;
        }

        // RSA 公開鍵（.atcpub）が指定されている場合
        if (cli.RsaPublicKeyFilePath != null)
        {
            SelectMenu(MenuItemType.PublicKey);
            var publicKeyXml = TryReadKeyFile(cli.RsaPublicKeyFilePath, context.IsUnattended);
            if (publicKeyXml == null)
            {
                ReportCommandLineResult(AtcExitCode.UsageError);
                BackToMain();
                return;
            }
            CurrentView = new EncryptionViewModel(this, paths, _encryptionService, context, publicKeyXml);
            return;
        }

        // パスワード付き ZIP（設定「暗号化ファイルの種類」/ 引数 /entype=3）
        if (context.Settings.EncryptionFileType == EncryptionFileTypes.Zip)
        {
            if (!ZipPasswordEncryptionViewModel.IsSupportedAlgorithm(context.Settings.ZipAlgorithm))
            {
                Console.Error.WriteLine(
                    _localize.GetString("Error_InvalidZipAlgorithm", context.Settings.ZipAlgorithm));
                ReportCommandLineResult(AtcExitCode.UsageError);
                BackToMain();
                return;
            }
            SelectMenu(MenuItemType.PasswordZip);
            CurrentView = new ZipPasswordEncryptionViewModel(this, paths, context);
            return;
        }

        SelectMenu(MenuItemType.Encrypt);
        CurrentView = new EncryptionViewModel(this, paths, _encryptionService, context);
    }

    /// <summary>
    ///   /genrsa: RSA 鍵ペアを生成して指定フォルダーに保存する。
    /// </summary>
    private async Task GenerateRsaKeyPairFromCommandLineAsync(string outputDirectory)
    {
        // /exit=1 の無人実行ではダイアログを出さない（誰も閉じられないため）
        var unattended = _exitAfterProcess;

        SelectMenu(MenuItemType.PublicKey);
        CurrentView = new PublicKeyViewModel(this);

        try
        {
            Directory.CreateDirectory(outputDirectory);

            var generator = new RsaKeyGeneratorService();
            var (publicKeyPath, privateKeyPath) = generator.GenerateKeyPair(outputDirectory, "AttacheCase_RSA");

            Console.WriteLine("RSA key pair generated:");
            Console.WriteLine("  Public key:  " + publicKeyPath);
            Console.WriteLine("  Private key: " + privateKeyPath);

            await DialogHelper.NotifyAsync(
                unattended,
                _localize["AppName"],
                publicKeyPath + Environment.NewLine + privateKeyPath,
                DialogIcon.Info);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            ReportCommandLineResult(AtcExitCode.GeneralError);

            await DialogHelper.NotifyAsync(unattended, _localize["Dialog_ErrorTitle"], ex.Message);
        }
    }

    /// <summary>
    ///   処理が正常に終わったときに呼ぶ。
    ///   設定「処理後に終了する」が有効なら、そのままアプリを終了する。
    /// </summary>
    public void CompleteRun(RunContext context)
    {
        if (!context.IsCommandLineRun && context.Settings.ExitAfterProcess)
        {
            _exitAfterProcess = true;
        }

        BackToMain();
    }

    /// <summary>
    ///   コマンドラインからの処理の結果を記録する。/exit=1 で終了する際の終了コードになる。
    /// </summary>
    public void ReportCommandLineResult(int exitCode)
    {
        if (exitCode != AtcExitCode.Success)
        {
            _commandLineExitCode = exitCode;
        }
    }

    /// <summary>
    ///   /exit=1 が指定されていればアプリを終了する。
    ///   成否にかかわらず終了する（スクリプトから呼んだときに固まらないようにするため）。
    /// </summary>
    public void FinishCommandLineRun()
    {
        if (!_exitAfterProcess) return;

        _exitAfterProcess = false;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(_commandLineExitCode);
        }
    }

    private void SelectMenu(MenuItemType type)
    {
        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Type == type);
    }

    #endregion
}
