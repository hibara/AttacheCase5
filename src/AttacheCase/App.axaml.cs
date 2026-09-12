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
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AttacheCase.Cli;
using AttacheCase.Models;
using System.Linq;
using AttacheCase.Services;
using AttacheCase.ViewModels;
using AttacheCase.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AttacheCase;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainViewModel;

    /// <summary>
    /// コマンドライン引数つきで起動されたか。更新確認を行うかの判断に使う。
    /// CommandLineStartup.Take() は一度きりなので、消費される前にここで控える。
    /// </summary>
    private bool _startedFromCommandLine;

    /// <summary>
    /// 終了時に背後の更新確認を打ち切るための合図。待機中や通信中のまま
    /// アプリが閉じられても、そこで止まるようにする。
    ///
    /// Dispose はしない。プロセスと寿命を共にするうえ、打ち切られた側がまだ
    /// トークンを握っている最中に破棄すると ObjectDisposedException になりうる。
    /// </summary>
    private readonly System.Threading.CancellationTokenSource _shutdownCts = new();

    public override void Initialize()
    {
      AvaloniaXamlLoader.Load(this);

      // Core 層（PasswordFileLoader など）からの通知をダイアログ表示に接続する。
      // 既定（差し替え前）は標準エラー出力で、CLI 実行ではそのままが正しい。
      UserNotification.NotifyAsync =
          (unattended, title, message) => DialogHelper.NotifyAsync(unattended, title, message);

      // 1) App 名（バンドル外実行時に効く）をロケールから決定
      Name = GetLocalizedAppDisplayName();

      // 2) “About …” もロケールで決定（NativeMenu は XAML 直書きよりコードが安定）
      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        LocalizeMacOsAppMenu();

      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        Dispatcher.UIThread.Post(() =>
        {
          // About の差し替え（あなたが既にやっているやつ）
          LocalizeMacOsAppMenu();

          // Hide/Quit 等の既定項目の差し替え
          LocalizeMacOsDefaultApplicationMenuItems();
        });
      }

      // 環境設定の読み込み
      // var services = new ServiceCollection();
      // services.AddSingleton<IWindowLayoutService>(new WindowLayoutService("AttacheCase"));

    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 設定のテーマ（auto/light/dark）を最初のウィンドウを作る前に反映する
            _appliedTheme = SettingsService.Instance.Current.Theme;
            ApplyThemeSetting(_appliedTheme);

            // Windows のタイトルバー・タスクバー用アイコン（旧版の main_icon.ico）。
            // 個々の Window に書かず暗黙スタイルで配ることで、About やダイアログ等の
            // サブウィンドウにも同じアイコンが付く。macOS ではウィンドウにアイコンを
            // 出さない（Dock は .app バンドルの icns）ため何もしない
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var iconStream = Avalonia.Platform.AssetLoader.Open(
                        new Uri("avares://AttacheCase/Assets/main-icon.ico"));
                    var windowIcon = new Avalonia.Controls.WindowIcon(iconStream);
                    Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.Window>())
                    {
                        Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Window.IconProperty, windowIcon) },
                    });
                }
                catch (Exception ex)
                {
                    // アイコンが読めなくても起動は続ける（既定アイコンになるだけ）
                    Debug.WriteLine("ウィンドウアイコンの設定に失敗: " + ex.Message);
                }
            }

            // 旧 Windows 版レジストリの記憶パスワード（MyKey の DPAPI blob）を、
            // 一度だけ新しい保管庫（_AtcVault.json）へ移す。保管庫が既にあれば何もしない。
            // 設定本体の移行（RegistrySettingsMigrator）とは別経路なのは、
            // settings.json に秘密を入れない方針のため
            if (OperatingSystem.IsWindows())
            {
                try { RememberedPasswordService.Instance.TryMigrateFromLegacyRegistry(); }
                catch (Exception ex) { Debug.WriteLine("記憶パスワードの移行に失敗: " + ex.Message); }
            }

            _mainViewModel = new MainWindowViewModel();
            _mainWindow = new MainWindow
            {
                DataContext = _mainViewModel,
                // ウィンドウタイトル
                Title = LocalizationService.Instance["AppName"]
            };

            desktop.MainWindow = _mainWindow;

            // 設定「表示言語」が変わったら、その場で画面を作り直して反映する
            SettingsService.Instance.Saved += OnSettingsSaved;

            // 設定「ウィンドウ」（最前面・メニューバーアイコン）を反映する
            WindowBehaviorService.Instance.Attach(_mainWindow, SettingsService.Instance);

            // 「複数起動しない」設定のとき、2 つ目の起動で渡された引数を引き受ける
            SingleInstanceService.Current?.StartListening(OnArgumentsFromAnotherInstance);

            // Finder や Dock へのドロップ、関連付けからの起動で開かれたファイルを受け取る。
            //
            // IActivatableLifetime は ApplicationLifetime とは別物。
            // ClassicDesktopStyleApplicationLifetime はこれを実装していないので、
            // `ApplicationLifetime is IActivatableLifetime` は決して成立しない
            // （成立しないまま黙って通り過ぎるため、関連付けが効かないことに
            //  気づけなかった）。取り出しは TryGetFeature で行う
            // Application 自身が非ジェネリックの TryGetFeature(Type) を持っており、
            // そちらが先に選ばれてしまうので、型を渡して受け取る
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += OnLifetimeActivated;
            }
            else
            {
                Debug.WriteLine("IActivatableLifetime を取得できません。関連付けからの起動を受け取れません");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || OperatingSystem.IsWindows())
            {
                // macOS: NativeMenu / Windows: インウィンドウメニュー を組み立てる
                _mainWindow.Opened += OnMainWindowOpened;
            }

            // Take() で消費される前に控える。購読の順序に依存させないため
            _startedFromCommandLine = CommandLineStartup.Options != null;

            // コマンドライン引数の指定を、ウィンドウが開いてから反映する
            _mainWindow.Opened += OnMainWindowOpenedForCommandLine;

            // 起動より先に届いた「このファイルを開け」を、ここで拾う
            _mainWindow.Opened += OnMainWindowOpenedForActivation;

            // 前回の更新でやり残した一時ディレクトリを片付ける
            UpdateInstaller.CleanUpStaleWorkDirectories(TimeSpan.FromDays(1));

            // 起動時の更新確認
            _mainWindow.Opened += OnMainWindowOpenedForUpdateCheck;

            desktop.Exit += (_, _) => _shutdownCts.Cancel();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>いま適用しているテーマ（"auto"/"light"/"dark"）。変更検知用。</summary>
    private string _appliedTheme = "auto";

    /// <summary>
    /// 設定が保存されたとき、テーマを反映し、テーマまたは表示言語が
    /// 変わっていれば画面を作り直す。
    /// 色そのものは作り直さなくても効くが、サイドバーの SVG アイコンは
    /// 明暗別ファイルを生成時に選ぶため、作り直しが要る。
    /// </summary>
    private void OnSettingsSaved(object? sender, Models.AppSettings settings)
    {
        var themeChanged = settings.Theme != _appliedTheme;
        ApplyThemeSetting(settings.Theme);
        _appliedTheme = settings.Theme;

        var languageChanged = LocalizationService.Instance.ApplyLanguageSetting(settings.Language);

        if (themeChanged || languageChanged)
        {
            Dispatcher.UIThread.Post(RecreateMainWindow);
        }
    }

    /// <summary>
    /// 設定のテーマ（auto / light / dark）を Avalonia のテーマバリアントへ反映する。
    /// auto は OS の設定に追従する（ThemeVariant.Default）。
    /// </summary>
    private void ApplyThemeSetting(string theme)
    {
        RequestedThemeVariant = theme switch
        {
            "light" => Avalonia.Styling.ThemeVariant.Light,
            "dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }

    /// <summary>
    /// 表示言語の切り替えを反映するため、メインウィンドウを作り直す。
    /// 文字列は画面の生成時に読み込まれるため、作り直すのが確実。
    /// </summary>
    private void RecreateMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var previous = _mainWindow;

        _mainViewModel = new MainWindowViewModel();
        _mainWindow = new MainWindow
        {
            DataContext = _mainViewModel,
            Title = LocalizationService.Instance["AppName"],
        };

        desktop.MainWindow = _mainWindow;
        WindowBehaviorService.Instance.Attach(_mainWindow, SettingsService.Instance);

        // ファイル・編集・ウィンドウ・ヘルプのメニューは Opened で組み立てている。
        // 作り直したウィンドウにも登録し直さないと、言語を切り替えた後にメニューが消える
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || OperatingSystem.IsWindows())
        {
            _mainWindow.Opened += OnMainWindowOpened;
        }

        _mainWindow.Show();
        previous?.Close();

        // macOS のアプリケーションメニューも言語に合わせ直す
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Name = GetLocalizedAppDisplayName();
            LocalizeMacOsAppMenu();
            LocalizeMacOsDefaultApplicationMenuItems();
        }
    }

    /// <summary>
    /// 2 つ目のプロセスから渡されたコマンドライン引数を、こちらのウィンドウで処理する。
    /// 受け付けたら true を返す（呼び出し元は受付結果を相手のプロセスへ返す）。
    /// </summary>
    private bool OnArgumentsFromAnotherInstance(string[] args)
    {
        var options = CliParser.Parse(args);

        if (options.ParseError != null || !options.HasAnyInstruction) return false;
        if (_mainViewModel is not { CanAcceptCommandLine: true }) return false;

        // /exit=1 で頼まれても、こちらのウィンドウを閉じてしまわない。
        // 呼び出した側のプロセスは受け渡した時点ですでに終了しており、
        // 開いたままのウィンドウを勝手に閉じるのは意図と違うため。
        // （設定「処理後に終了する」を利用者自身が有効にしている場合はそちらに従う）
        options.ExitAfterProcess = null;

        Dispatcher.UIThread.Post(() =>
        {
            WindowBehaviorService.Instance.ActivateWindow();
            _ = _mainViewModel.ApplyCommandLineAsync(options);
        });

        return true;
    }

    /// <summary>
    /// 関連付けから起動されたとき、窓の用意ができるまで預かるファイル。
    ///
    /// 書類をダブルクリックして起動した場合、macOS からの知らせは
    /// 画面が立ち上がる前に届くことがある。そのまま捨てると
    /// 「アプリは開くのに、ファイルは何も起きない」になる。
    /// </summary>
    private List<string>? _pendingActivationFiles;

    /// <summary>
    /// Finder や Dock のアイコンへのドロップ、関連付けからの起動で開かれた
    /// ファイルを処理する。
    ///
    /// macOS では 2 つ目のプロセスは起動せず、動いているアプリへ通知が届く。
    /// </summary>
    private void OnLifetimeActivated(object? sender, ActivatedEventArgs e)
    {
        if (e is not FileActivatedEventArgs fileArgs) return;

        var paths = fileArgs.Files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();

        if (paths.Count == 0) return;

        Dispatcher.UIThread.Post(() => OpenActivatedFiles(paths));
    }

    /// <summary>
    /// 受け取ったファイルを画面へ渡す。まだ渡せる状態でなければ預かっておく。
    /// </summary>
    private void OpenActivatedFiles(List<string> paths)
    {
        // 画面の用意ができていない（起動直後）。窓が開いてから渡す
        if (_mainViewModel is null || _mainWindow is null)
        {
            _pendingActivationFiles = paths;
            return;
        }

        // 何かの処理中は割り込まない。ドロップと同じ扱い
        if (!_mainViewModel.CanAcceptCommandLine) return;

        WindowBehaviorService.Instance.ActivateWindow();
        _mainViewModel.OnFilesDropped(paths);
    }

    /// <summary>
    /// 窓が開いたところで、預かっていたファイルを渡す。
    /// </summary>
    private void OnMainWindowOpenedForActivation(object? sender, EventArgs e)
    {
        if (_mainWindow != null)
        {
            _mainWindow.Opened -= OnMainWindowOpenedForActivation;
        }

        var paths = _pendingActivationFiles;
        _pendingActivationFiles = null;

        if (paths is { Count: > 0 }) OpenActivatedFiles(paths);
    }

    private static string GetLocalizedAppDisplayName()
    {
      // あなたの既存ローカライズ資産を優先（キーが無ければフォールバック）
      var lang = LocalizationService.Instance.CurrentCulture.TwoLetterISOLanguageName;

      try
      {
        // 例：en="AttacheCase", ja="アタッシェケース" を入れておく
        var s = LocalizationService.Instance["AppName"];
        if (!string.IsNullOrWhiteSpace(s))
          return s;
      }
      catch
      {
        // ignore
      }

      return lang == "ja" ? "アタッシェケース" : "AttacheCase";
    }

    /// <summary>
    /// アプリ名メニュー（左端）の文言をロケールに合わせる。
    /// 「AttacheCase について」と「アップデートを確認…」の 2 つ。
    /// </summary>
    private void LocalizeMacOsAppMenu()
    {
      var menu = NativeMenu.GetMenu(this);
      if (menu?.Items.Count > 0 && menu.Items[0] is NativeMenuItem aboutItem)
      {
        var lang = LocalizationService.Instance.CurrentCulture.TwoLetterISOLanguageName;
        var fallbackFormat = lang == "ja" ? "{0}について" : "About {0}";

        string format;
        try
        {
          // 例：en="About {0}", ja="{0}について…"
          format = LocalizationService.Instance["Menu_About"];
        }
        catch
        {
          format = fallbackFormat;
        }

        aboutItem.Header = string.Format(format, Name);
      }

      // 「アップデートを確認…」（About の下、区切りを挟んだ 3 番目）。
      // 文言が引けないときは App.axaml の既定（英語）のままにする
      if (menu?.Items.Count > 2 && menu.Items[2] is NativeMenuItem updateItem)
      {
        try
        {
          updateItem.Header = LocalizationService.Instance["Menu_CheckForUpdates"];
        }
        catch
        {
          // そのまま
        }
      }
    }

    /// <summary>
    /// 起動時のコマンドライン引数を画面へ反映する。
    /// ダイアログを出せるよう、ウィンドウが表示されてから実行する。
    /// </summary>
    private void OnMainWindowOpenedForCommandLine(object? sender, EventArgs e)
    {
        if (_mainWindow != null)
        {
            _mainWindow.Opened -= OnMainWindowOpenedForCommandLine;
        }

        var options = CommandLineStartup.Take();
        if (options == null || _mainViewModel == null) return;

        Dispatcher.UIThread.Post(() => _ = _mainViewModel.ApplyCommandLineAsync(options));
    }

    /// <summary>
    /// 起動時の更新確認。
    ///
    /// 利用者の作業を邪魔しないよう、少し待ってから背後で確認し、更新があるときだけ知らせる。
    /// 通信できない・署名が合わないなどの失敗は黙って諦める（起動を妨げない）。
    ///
    /// コマンドライン起動のときは行わない。自動処理の途中でダイアログを出すと、
    /// 無人で回している処理が止まってしまうため。
    /// </summary>
    private void OnMainWindowOpenedForUpdateCheck(object? sender, EventArgs e)
    {
        if (_mainWindow != null)
        {
            _mainWindow.Opened -= OnMainWindowOpenedForUpdateCheck;
        }

        if (_startedFromCommandLine) return;
        if (!SettingsService.Instance.Current.CheckForUpdatesOnStartup) return;

        // 起動のたびに問い合わせると、配信側にも利用者の回線にも無用な負担がかかる。
        // 1 日に 1 回までとする
        var store = new UpdateCheckStateStore();
        if (!store.ShouldCheck(DateTimeOffset.Now)) return;

        _ = CheckForUpdatesAsync(store, _shutdownCts.Token);
    }

    /// <summary>
    /// 起動時の確認。少し待ってから背後で問い合わせ、更新があるときだけ知らせる。
    /// </summary>
    private static async System.Threading.Tasks.Task CheckForUpdatesAsync(
        UpdateCheckStateStore store,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            // 起動直後は画面の準備で忙しいので、少し置いてから通信する
            await System.Threading.Tasks.Task
                .Delay(TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);

            IUpdateService updateService = new UpdateService();
            var result = await updateService.CheckAsync(cancellationToken).ConfigureAwait(false);

            // 到達できた時点で記録する。落ちている配信先に毎回起動のたびに
            // 問い合わせ続けないようにするため、失敗でも間隔は空ける
            store.MarkChecked(DateTimeOffset.Now);

            if (result.Status != UpdateCheckStatus.UpdateAvailable ||
                result.Manifest is null || result.Build is null)
            {
                return;
            }

            // 利用者が飛ばした版なら黙っている。より新しい版が出れば、
            // バージョンが違うのでまた知らせることになる
            if (store.IsSkipped(result.Manifest.Version)) return;

            await Dispatcher.UIThread.InvokeAsync(
                () => OfferUpdateAsync(result.Manifest, result.Build, store));
        }
        catch (OperationCanceledException)
        {
            // 終了中。何もしない
        }
        catch (Exception ex)
        {
            // 更新確認の失敗で利用者の作業を止めない
            Debug.WriteLine($"Update check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// メニューの「アップデートを確認…」。
    ///
    /// 1 日 1 回の間隔は無視する。利用者が自分で確かめたいときに待たされては
    /// 意味がない。最新なら、その旨をはっきり伝える（自動確認と違って
    /// 黙って終わると、動いたのかどうか分からない）。
    /// </summary>
    private async void OnCheckForUpdatesMenuClick(object? sender, EventArgs e)
    {
        var localize = LocalizationService.Instance;
        var store = new UpdateCheckStateStore();

        try
        {
            IUpdateService updateService = new UpdateService();
            var result = await updateService.CheckAsync(_shutdownCts.Token).ConfigureAwait(true);

            store.MarkChecked(DateTimeOffset.Now);

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable
                    when result.Manifest is not null && result.Build is not null:
                    // 手動で確かめたのだから、飛ばした版でも見せる
                    await OfferUpdateAsync(result.Manifest, result.Build, store);
                    break;

                case UpdateCheckStatus.UpToDate:
                    await DialogHelper.NotifyAsync(
                        unattended: false,
                        localize["Update_Title"],
                        localize.GetString("Update_UpToDate", AppVersion.Display),
                        DialogIcon.Info);
                    break;

                default:
                    await DialogHelper.NotifyAsync(
                        unattended: false,
                        localize["Update_Title"],
                        localize["Update_CheckFailed"] +
                            (string.IsNullOrEmpty(result.Error) ? "" : "\n\n" + result.Error),
                        DialogIcon.Warning);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // 終了中
        }
        catch (Exception ex)
        {
            await DialogHelper.NotifyAsync(
                unattended: false,
                localize["Update_Title"],
                localize["Update_CheckFailed"] + "\n\n" + ex.Message,
                DialogIcon.Error);
        }
    }

    /// <summary>
    /// 更新の内容を見せて、選んでもらう。
    /// </summary>
    /// <summary>
    /// Windows での更新適用。
    ///   インストーラ版 … ダウンロード → マニフェスト照合（掴んだまま）→
    ///                    Authenticode 検証 → インストーラ起動（UAC）→ 自分は終了
    ///   ZIP 版         … 何も落とさず配布ページをブラウザで開く
    /// </summary>
    private static async System.Threading.Tasks.Task ApplyUpdateOnWindowsAsync(Models.UpdateBuild build)
    {
        if (!OperatingSystem.IsWindows()) return;

        var localize = LocalizationService.Instance;

        if (Platform.Windows.WindowsInstallKind.Detect() == Platform.Windows.InstallKind.Portable)
        {
            await DialogHelper.NotifyAsync(
                unattended: false, localize["Update_Title"],
                localize["Update_OpenDownloadPage"], DialogIcon.Info);
            Platform.Windows.WindowsUpdateFlow.OpenDownloadPage(UpdateService.DefaultDownloadPageUrl);
            return;
        }

        var progressWindow = new UpdateProgressWindow();
        progressWindow.SetStatus(localize["Update_Downloading"]);
        progressWindow.SetProgress(0);
        var owner = DialogHelper.GetMainWindow();
        if (owner is null) progressWindow.Show(); else progressWindow.Show(owner);

        Platform.Windows.WindowsUpdatePreparation? preparation = null;
        try
        {
            preparation = await new Platform.Windows.WindowsUpdateFlow().PrepareAsync(
                build,
                new Progress<UpdateStage>(stage =>
                {
                    progressWindow.SetStatus(localize[
                        stage == UpdateStage.Downloading ? "Update_Downloading" : "Update_Verifying"]);
                    if (stage != UpdateStage.Downloading) progressWindow.SetProgress(null);
                }),
                new Progress<DownloadProgress>(p => progressWindow.SetProgress(
                    p.Ratio * 100,
                    localize.GetString(
                        "Update_DownloadSize",
                        FormatBytes(p.BytesReceived),
                        FormatBytes(p.TotalBytes)))),
                progressWindow.CancellationToken);

            progressWindow.Finish();

            // ここから先は自分を終了させる。中止できるのはこの手前まで
            await DialogHelper.NotifyAsync(
                unattended: false,
                localize["Update_Title"],
                localize["Update_ReadyToRestart"],
                DialogIcon.Info);

            Platform.Windows.WindowsUpdateFlow.LaunchInstaller(preparation);

            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (OperationCanceledException)
        {
            // 利用者が中止した。何も言わずに戻る
            progressWindow.Finish();
        }
        catch (UpdateContentMismatchException ex)
        {
            progressWindow.Finish();

            // 配信側のファイルが更新情報と食い違っている。利用者のパソコンには
            // 何も起きていないので、そこを先に伝える
            await DialogHelper.NotifyAsync(
                unattended: false,
                localize["Update_Title"],
                localize["Update_ContentMismatch"] + "\n\n" +
                localize["Update_ContentMismatchAdvice"] + "\n\n" + ex.Message,
                DialogIcon.Warning);
        }
        catch (Exception ex)
        {
            progressWindow.Finish();

            await DialogHelper.NotifyAsync(
                unattended: false,
                localize["Update_Title"],
                localize["Update_Failed"] + "\n\n" + ex.Message,
                DialogIcon.Error);
        }
        finally
        {
            // 掴んでいたファイルを放す（LaunchInstaller が戻ったあとなら差し替えの隙は無い）
            preparation?.Dispose();
        }
    }

    private static async System.Threading.Tasks.Task OfferUpdateAsync(
        Models.UpdateManifest manifest,
        Models.UpdateBuild build,
        UpdateCheckStateStore store)
    {
        var choice = await UpdateAvailableWindow.ShowAsync(
            DialogHelper.GetMainWindow(), manifest, AppVersion.Current);

        switch (choice)
        {
            case UpdateChoice.Install:
                await ApplyUpdateAsync(build);
                break;

            case UpdateChoice.Skip:
                store.MarkSkipped(manifest.Version);
                break;

            case UpdateChoice.Later:
            default:
                // 何もしない。次の確認でまた知らせる
                break;
        }
    }

    /// <summary>
    /// 更新を適用する。ダウンロードと検証を終えてから差し替えを起こし、
    /// 自分は速やかに終了する。
    ///
    /// 差し替えられない場所で動いている場合は、何もせずに事情を伝える。
    /// 権限昇格は求めない（昇格を伴う自動更新は、それ自体が攻撃経路になる）。
    /// </summary>
    private static async System.Threading.Tasks.Task ApplyUpdateAsync(Models.UpdateBuild build)
    {
        var localize = LocalizationService.Instance;

        if (OperatingSystem.IsWindows())
        {
            await ApplyUpdateOnWindowsAsync(build);
            return;
        }

        var location = AppBundleLocator.Locate();
        if (!location.CanReplace)
        {
            var reason = location.Status switch
            {
                InstallLocationStatus.Translocated => localize["Update_Cannot_Translocated"],
                InstallLocationStatus.ReadOnlyLocation => localize["Update_Cannot_ReadOnly"],
                InstallLocationStatus.NotWritable => localize["Update_Cannot_NotWritable"],
                _ => localize["Update_Cannot_NotInAppBundle"],
            };

            await DialogHelper.NotifyAsync(
                unattended: false, localize["Update_Title"], reason, DialogIcon.Warning);
            return;
        }

        var progressWindow = new UpdateProgressWindow();
        progressWindow.SetStatus(localize["Update_Downloading"]);
        progressWindow.SetProgress(0);
        // 親が取れないことがある（メインウィンドウを作り直している最中など）。
        // その場合は親なしで出す。出ないより出たほうがよい
        var owner = DialogHelper.GetMainWindow();
        if (owner is null)
        {
            progressWindow.Show();
        }
        else
        {
            progressWindow.Show(owner);
        }

        try
        {
            var preparation = await new UpdateFlow().PrepareAsync(
                build,
                location.BundlePath!,
                stage =>
                {
                    progressWindow.SetStatus(localize[stage switch
                    {
                        UpdateStage.Downloading => "Update_Downloading",
                        UpdateStage.Extracting => "Update_Extracting",
                        _ => "Update_Verifying",
                    }]);

                    // 展開と検証は進み具合が分からない。ダウンロード完了時の
                    // 100% を出したままにすると、止まって見える
                    if (stage != UpdateStage.Downloading)
                    {
                        progressWindow.SetProgress(null);
                    }
                },
                new Progress<DownloadProgress>(p => progressWindow.SetProgress(
                    p.Ratio * 100,
                    localize.GetString(
                        "Update_DownloadSize",
                        FormatBytes(p.BytesReceived),
                        FormatBytes(p.TotalBytes)))),
                progressWindow.CancellationToken);

            progressWindow.Finish();

            // ここから先は自分を終了させる。中止できるのはこの手前まで
            await DialogHelper.NotifyAsync(
                unattended: false,
                localize["Update_Title"],
                localize["Update_ReadyToRestart"],
                DialogIcon.Info);

            new UpdateInstaller().Start(
                preparation.VerifiedAppPath, location.BundlePath!, preparation.WorkDirectory);

            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (OperationCanceledException)
        {
            // 利用者が中止した。何も言わずに戻る
            progressWindow.Finish();
        }
        catch (UpdateContentMismatchException ex)
        {
            progressWindow.Finish();

            // 配信側のファイルが更新情報と食い違っている。他の失敗と違って、
            // 利用者のパソコンには何も起きていないので、そこを先に伝える。
            // 期待値と実際の値は、報告してもらうときに要るので最後に残す
            await DialogHelper.NotifyAsync(
                unattended: false,
                localize["Update_Title"],
                localize["Update_ContentMismatch"] + "\n\n" +
                localize["Update_ContentMismatchAdvice"] + "\n\n" + ex.Message,
                DialogIcon.Warning);
        }
        catch (Exception ex)
        {
            progressWindow.Finish();

            await DialogHelper.NotifyAsync(
                unattended: false,
                localize["Update_Title"],
                localize["Update_Failed"] + "\n\n" + ex.Message,
                DialogIcon.Error);
        }
    }

    /// <summary>バイト数を見やすい単位にする。</summary>
    internal static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024 / 1024:0.0} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / 1024.0 / 1024:0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes} B";
    }


    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (_mainWindow != null)
        {
            _mainWindow.Opened -= OnMainWindowOpened;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) SetupMacOsWindowMenu();
            SetupWindowsMenu();
            _ = ConfirmPendingPortableSettingsAsync();
        }
    }

    /// <summary>未承認のポータブル設定が見つかっていれば、信頼するか確認する。</summary>
    private static bool s_portableConfirmAsked;

    private async System.Threading.Tasks.Task ConfirmPendingPortableSettingsAsync()
    {
        var pending = SettingsService.Instance.PendingPortableFilePath;
        if (pending == null || s_portableConfirmAsked) return;
        s_portableConfirmAsked = true; // 言語変更でウィンドウを作り直しても二度は聞かない

        var localize = LocalizationService.Instance;
        var trusted = await DialogHelper.ConfirmAsync(
            unattended: false,
            localize["Portable_ConfirmTitle"],
            localize.GetString("Portable_ConfirmMessage", pending));

        if (!trusted) return;

        new AttacheCase.Settings.PortableSettingsGuard().Approve(pending);
        await DialogHelper.NotifyAsync(
            unattended: false,
            localize["Portable_ConfirmTitle"],
            localize["Portable_EnabledNextLaunch"],
            DialogIcon.Info);
    }

    /// <summary>
    /// Windows 用のインウィンドウメニューを組み立てる。
    ///
    /// 構成は旧 Windows 版（Form1 のメニュー）を踏襲した 3 本立て:
    ///   ファイル（暗号化する / 復号する のサブメニュー + 公開鍵暗号 + Post-PPAP + 終了）
    ///   オプション（動作設定）
    ///   ヘルプ（オンラインヘルプ F1 / 更新の確認 / バージョン情報 / ライセンス）
    /// 文言は WinMenu_* リソース（Alt ニーモニック "_" 入り。macOS の NativeMenu は
    /// ニーモニックを使わないため、Menu_* とはキーを分けている）。
    /// </summary>
    private void SetupWindowsMenu()
    {
        if (!OperatingSystem.IsWindows() || _mainWindow == null) return;

        var menuBar = _mainWindow.FindControl<Avalonia.Controls.Menu>("WindowsMenuBar");
        if (menuBar == null) return;

        var localize = LocalizationService.Instance;

        // ヘッダーは AccessText で包む。テーマによっては MenuItem テンプレートが
        // アクセスキー（"_F"）を解釈せず、アンダースコアが素通しで表示されるため
        //（ShadUI で実測）。AccessText なら常に「F に下線」として扱われる
        Avalonia.Controls.Primitives.AccessText Header(string key)
            => new() { Text = localize[key], VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

        Avalonia.Controls.MenuItem Item(string key, Action action, Avalonia.Input.KeyGesture? gesture = null)
        {
            var item = new Avalonia.Controls.MenuItem { Header = Header(key) };
            if (gesture != null)
            {
                item.InputGesture = gesture;
                // InputGesture は表示だけなので、実際のショートカットはウィンドウ側に登録する
                _mainWindow.KeyBindings.Add(new Avalonia.Input.KeyBinding
                {
                    Gesture = gesture,
                    Command = new CommunityToolkit.Mvvm.Input.RelayCommand(action),
                });
            }

            item.Click += (_, _) => action();
            return item;
        }

        // サイドバーの項目を選んだのと同じ動きをさせる（選択表示も追従する）
        void SelectSideMenu(Models.MenuItemType type)
        {
            var model = _mainViewModel?.MenuItems.FirstOrDefault(m => m.Type == type);
            if (model != null) _mainViewModel!.MenuItemClickCommand.Execute(model);
        }

        // ファイル(F)
        var encryptMenu = new Avalonia.Controls.MenuItem { Header = Header("WinMenu_Encrypt") };
        encryptMenu.Items.Add(Item("WinMenu_EncryptOpenFiles", () => RunMenuAction(OpenFileToEncryptAsync),
            new Avalonia.Input.KeyGesture(Avalonia.Input.Key.O, Avalonia.Input.KeyModifiers.Control)));
        encryptMenu.Items.Add(Item("WinMenu_EncryptOpenFolder", () => RunMenuAction(OpenFolderToEncryptAsync),
            new Avalonia.Input.KeyGesture(Avalonia.Input.Key.D, Avalonia.Input.KeyModifiers.Control)));
        encryptMenu.Items.Add(new Avalonia.Controls.Separator());
        encryptMenu.Items.Add(Item("WinMenu_ZipFromFiles", () => RunMenuAction(OpenFileForZipAsync)));
        encryptMenu.Items.Add(Item("WinMenu_ZipFromFolder", () => RunMenuAction(OpenFolderForZipAsync)));

        var decryptMenu = new Avalonia.Controls.MenuItem { Header = Header("WinMenu_Decrypt") };
        decryptMenu.Items.Add(Item("WinMenu_DecryptOpenFile", () => RunMenuAction(OpenEncryptedFileAsync)));
        decryptMenu.Items.Add(new Avalonia.Controls.Separator());
        decryptMenu.Items.Add(Item("WinMenu_DecryptOpenZip", () => RunMenuAction(OpenZipFileAsync)));

        var fileMenu = new Avalonia.Controls.MenuItem { Header = Header("WinMenu_File") };
        fileMenu.Items.Add(encryptMenu);
        fileMenu.Items.Add(decryptMenu);
        fileMenu.Items.Add(new Avalonia.Controls.Separator());
        fileMenu.Items.Add(Item("WinMenu_PublicKey", () => SelectSideMenu(Models.MenuItemType.PublicKey)));
        fileMenu.Items.Add(Item("WinMenu_PostPpap", () => SelectSideMenu(Models.MenuItemType.PostPpap)));
        fileMenu.Items.Add(new Avalonia.Controls.Separator());
        fileMenu.Items.Add(Item("WinMenu_Exit", () => _mainWindow.Close()));

        // オプション(O)
        var optionsMenu = new Avalonia.Controls.MenuItem { Header = Header("WinMenu_Options") };
        optionsMenu.Items.Add(Item("WinMenu_Setting", () => _mainViewModel?.OpenSettingsCommand.Execute(null)));

        // ヘルプ(H)
        var helpMenu = new Avalonia.Controls.MenuItem { Header = Header("WinMenu_Help") };
        helpMenu.Items.Add(Item("WinMenu_OnlineHelp", () => OnWebsiteMenuClick(this, EventArgs.Empty),
            new Avalonia.Input.KeyGesture(Avalonia.Input.Key.F1)));
        helpMenu.Items.Add(new Avalonia.Controls.Separator());
        helpMenu.Items.Add(Item("WinMenu_CheckForUpdates", () => OnCheckForUpdatesMenuClick(this, EventArgs.Empty)));
        helpMenu.Items.Add(Item("WinMenu_License", () => OnLicenseMenuClick(this, EventArgs.Empty)));
        helpMenu.Items.Add(new Avalonia.Controls.Separator());
        // 「バージョン情報」は昔からの慣習で必ず一番下に置く（ライセンスと取り違え防止）
        helpMenu.Items.Add(Item("WinMenu_About", () => OnAboutMenuClick(this, EventArgs.Empty)));

        menuBar.Items.Clear();
        menuBar.Items.Add(fileMenu);
        menuBar.Items.Add(optionsMenu);
        menuBar.Items.Add(helpMenu);
        menuBar.IsVisible = true;
    }

    /// <summary>
    /// macOS用のネイティブメニュー（File/Edit/Window/Help）を MainWindow に設定
    /// </summary>
    /// <summary>
    /// メニュー項目の非同期処理を実行する。
    /// Click に async ラムダを直接渡すと async void になり、例外が拾われずに
    /// アプリが落ちる。ここで受け止めて利用者に伝える。
    /// </summary>
    private static async void RunMenuAction(Func<System.Threading.Tasks.Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Menu action failed: " + ex);
            await DialogHelper.NotifyAsync(
                false,
                LocalizationService.Instance["Dialog_ErrorTitle"],
                ex.Message);
        }
    }

    /// <summary>
    /// 既定メニュー項目の「最初に見たときのヘッダー」を覚えておく置き場。
    /// 言語を何度切り替えても同じ判定ができるようにするため。
    /// </summary>
    private static readonly ConditionalWeakTable<NativeMenuItem, string> s_originalMenuHeaders = new();

    private void SetupMacOsWindowMenu()
    {
        if (_mainWindow == null)
            return;

        var localize = LocalizationService.Instance;

        // ========================================
        // ファイルメニュー
        // ========================================

        var openFileToEncrypt = new NativeMenuItem(localize["Menu_OpenFileToEncrypt"])
        {
            Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.O, Avalonia.Input.KeyModifiers.Meta)
        };
        openFileToEncrypt.Click += (_, _) => RunMenuAction(OpenFileToEncryptAsync);

        var openFolderToEncrypt = new NativeMenuItem(localize["Menu_OpenFolderToEncrypt"])
        {
            Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.O,
                Avalonia.Input.KeyModifiers.Meta | Avalonia.Input.KeyModifiers.Shift)
        };
        openFolderToEncrypt.Click += (_, _) => RunMenuAction(OpenFolderToEncryptAsync);

        var openEncryptedFile = new NativeMenuItem(localize["Menu_OpenEncryptedFile"])
        {
            Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.D, Avalonia.Input.KeyModifiers.Meta)
        };
        openEncryptedFile.Click += (_, _) => RunMenuAction(OpenEncryptedFileAsync);

        var openFileForZip = new NativeMenuItem(localize["Menu_OpenFileForZip"]);
        openFileForZip.Click += (_, _) => RunMenuAction(OpenFileForZipAsync);

        var openFolderForZip = new NativeMenuItem(localize["Menu_OpenFolderForZip"]);
        openFolderForZip.Click += (_, _) => RunMenuAction(OpenFolderForZipAsync);

        var openZipFile = new NativeMenuItem(localize["Menu_OpenZipFile"]);
        openZipFile.Click += (_, _) => RunMenuAction(OpenZipFileAsync);

        var generateKeyPair = new NativeMenuItem(localize["Menu_GenerateKeyPair"]);
        generateKeyPair.Click += (_, _) => RunMenuAction(GenerateKeyPairAsync);

        var loadPublicKey = new NativeMenuItem(localize["Menu_LoadPublicKey"]);
        loadPublicKey.Click += (_, _) => RunMenuAction(LoadPublicKeyAsync);

        var loadPrivateKey = new NativeMenuItem(localize["Menu_LoadPrivateKey"]);
        loadPrivateKey.Click += (_, _) => RunMenuAction(LoadPrivateKeyAsync);

        var closeMenuItem = new NativeMenuItem(localize["Menu_Close"])
        {
            Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.W, Avalonia.Input.KeyModifiers.Meta)
        };
        closeMenuItem.Click += (_, _) => _mainWindow.Close();

        var fileMenu = new NativeMenuItem(localize["MacMenu_File"])
        {
            Menu = new NativeMenu
            {
                openFileToEncrypt,
                openFolderToEncrypt,
                openEncryptedFile,
                new NativeMenuItemSeparator(),
                openFileForZip,
                openFolderForZip,
                openZipFile,
                new NativeMenuItemSeparator(),
                generateKeyPair,
                loadPublicKey,
                loadPrivateKey,
                new NativeMenuItemSeparator(),
                closeMenuItem
            }
        };

        // ========================================
        // 編集メニュー（必要最低限：ショートカットだけ）
        // ========================================
        var editMenu = new NativeMenuItem(localize["MacMenu_Edit"])
        {
            Menu = new NativeMenu
            {
                new NativeMenuItem("Undo")
                {
                    Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Z, Avalonia.Input.KeyModifiers.Meta)
                },
                new NativeMenuItem("Redo")
                {
                    Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Z,
                        Avalonia.Input.KeyModifiers.Meta | Avalonia.Input.KeyModifiers.Shift)
                },
                new NativeMenuItemSeparator(),
                new NativeMenuItem("Cut")
                {
                    Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.X, Avalonia.Input.KeyModifiers.Meta)
                },
                new NativeMenuItem("Copy")
                {
                    Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.C, Avalonia.Input.KeyModifiers.Meta)
                },
                new NativeMenuItem("Paste")
                {
                    Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.V, Avalonia.Input.KeyModifiers.Meta)
                },
                new NativeMenuItem("Select All")
                {
                    Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.A, Avalonia.Input.KeyModifiers.Meta)
                }
            }
        };

        // ========================================
        // ウィンドウメニュー
        // ========================================
        var minimizeItem = new NativeMenuItem(localize["Menu_Minimize"])
        {
            Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.M, Avalonia.Input.KeyModifiers.Meta)
        };
        minimizeItem.Click += (_, _) => _mainWindow.WindowState = WindowState.Minimized;

        var zoomItem = new NativeMenuItem(localize["Menu_Zoom"]);
        zoomItem.Click += (_, _) =>
        {
            _mainWindow.WindowState = _mainWindow.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        };

        var bringAllToFrontItem = new NativeMenuItem(localize["Menu_BringAllToFront"]);
        bringAllToFrontItem.Click += (_, _) => _mainWindow.Activate();

        var windowMenu = new NativeMenuItem(localize["MacMenu_Window"])
        {
            Menu = new NativeMenu
            {
                minimizeItem,
                zoomItem,
                new NativeMenuItemSeparator(),
                bringAllToFrontItem
            }
        };

        // ========================================
        // ヘルプメニュー
        // ========================================
        var websiteMenuItem = new NativeMenuItem(localize["Menu_Website"]);
        websiteMenuItem.Click += OnWebsiteMenuClick;

        // バージョン情報からも開けるが、探しに行く先としてはヘルプが自然
        var licenseMenuItem = new NativeMenuItem(localize["Menu_License"]);
        licenseMenuItem.Click += OnLicenseMenuClick;

        var helpMenu = new NativeMenuItem(localize["MacMenu_Help"])
        {
            Menu = new NativeMenu
            {
                websiteMenuItem,
                new NativeMenuItemSeparator(),
                licenseMenuItem
            }
        };

        // ========================================
        // MainWindow に設定（←ここが本体）
        // ========================================
        var nativeMenu = new NativeMenu
        {
            fileMenu,
            editMenu,
            windowMenu,
            helpMenu
        };

        NativeMenu.SetMenu(_mainWindow, nativeMenu);
    }

    // ========================================
    // メニューハンドラー
    // ========================================

    private async void OnAboutMenuClick(object? sender, EventArgs e)
    {
      try
      {
        var aboutWindow = new AboutWindow();

        if (_mainWindow != null)
        {
          // 入れ子モーダルは Windows で操作不能になるため、バージョン情報のボタンは
          // 自身を閉じて「次に開く画面」を返す。ここでメインウィンドウ所有として開き直す。
          // ライセンス/登録画面は「バージョン情報の上に載っている」体なので、
          // 閉じたらバージョン情報へ戻る（None で閉じるまでループ）。
          var next = await aboutWindow.ShowDialog<AboutWindow.AboutAction>(_mainWindow);
          while (next != AboutWindow.AboutAction.None)
          {
            switch (next)
            {
              case AboutWindow.AboutAction.ShowLicense:
                await new LicenseWindow().ShowDialog(_mainWindow);
                break;
              case AboutWindow.AboutAction.ShowRegistration:
                await new LicenseRegistrationWindow().ShowDialog(_mainWindow);
                break;
              case AboutWindow.AboutAction.ConfirmDeleteLicense:
                // 隠し機能: ライセンスアイコン右クリック → 削除。確認の既定は「いいえ」。
                // 「はい」なら削除し、開き直したバージョン情報は未登録（登録ボタン）に戻る
                if (await MessageDialogWindow.ShowConfirmAsync(_mainWindow,
                      LocalizationService.Instance["About_DeleteLicenseTitle"],
                      LocalizationService.Instance["About_DeleteLicenseConfirm"]))
                {
                  new LicenseRegister().DeleteLicense();
                }
                break;
            }
            next = await new AboutWindow().ShowDialog<AboutWindow.AboutAction>(_mainWindow);
          }
        }
        else
        {
          aboutWindow.Show();
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"AboutWindow error: {ex.Message}");
      }
    }

    private async void OnLicenseMenuClick(object? sender, EventArgs e)
    {
        try
        {
            var licenseWindow = new LicenseWindow();

            if (_mainWindow != null)
            {
                await licenseWindow.ShowDialog(_mainWindow);
            }
            else
            {
                licenseWindow.Show();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LicenseWindow error: {ex.Message}");
        }
    }

    private void OnWebsiteMenuClick(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://hibara.jp/software/attachecase/",
                UseShellExecute = true
            });
        }
        catch
        {
            // 失敗しても続行
        }
    }

    private async System.Threading.Tasks.Task OpenFileToEncryptAsync()
    {
        if (_mainWindow == null || _mainViewModel == null) return;
        var localize = LocalizationService.Instance;

        var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localize["Dialog_SelectFileToEncrypt"],
            AllowMultiple = true
        });

        if (files.Count > 0)
        {
            var paths = new System.Collections.Generic.List<string>();
            foreach (var file in files)
            {
                if (file.TryGetLocalPath() is { } path)
                    paths.Add(path);
            }

            if (paths.Count > 0)
                _mainViewModel.StartEncryption(paths.ToArray());
        }
    }

    private async System.Threading.Tasks.Task OpenFolderToEncryptAsync()
    {
        if (_mainWindow == null || _mainViewModel == null) return;
        var localize = LocalizationService.Instance;

        var folders =
          await _mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = localize["Dialog_SelectFolderToEncrypt"],
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            _mainViewModel.StartEncryption([path]);
    }

    private async System.Threading.Tasks.Task OpenEncryptedFileAsync()
    {
        if (_mainWindow == null || _mainViewModel == null) return;
        var localize = LocalizationService.Instance;

        var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localize["Dialog_SelectEncryptedFile"],
            AllowMultiple = false,
            FileTypeFilter =
            [
              new FilePickerFileType("AttacheCase Files") { Patterns = new[] { "*.atc" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            ]
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            _mainViewModel.StartDecryption(path);
    }

    private async System.Threading.Tasks.Task OpenFileForZipAsync()
    {
        if (_mainWindow == null || _mainViewModel == null) return;
        var localize = LocalizationService.Instance;

        var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localize["Dialog_SelectFileForZip"],
            AllowMultiple = true
        });

        if (files.Count > 0)
        {
            var paths = new System.Collections.Generic.List<string>();
            foreach (var file in files)
            {
                if (file.TryGetLocalPath() is { } path)
                    paths.Add(path);
            }

            if (paths.Count > 0)
                _mainViewModel.StartZipEncryption(paths.ToArray());
        }
    }

    private async System.Threading.Tasks.Task OpenFolderForZipAsync()
    {
        if (_mainWindow == null || _mainViewModel == null) return;
        var localize = LocalizationService.Instance;

        var folders = await _mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = localize["Dialog_SelectFolderForZip"],
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            _mainViewModel.StartZipEncryption(new[] { path });
    }

    private async System.Threading.Tasks.Task OpenZipFileAsync()
    {
        if (_mainWindow == null || _mainViewModel == null) return;
        var localize = LocalizationService.Instance;

        var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localize["Dialog_SelectZipFile"],
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ZIP Files") { Patterns = new[] { "*.zip" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            _mainViewModel.StartZipDecryption(path);
    }

    private async System.Threading.Tasks.Task GenerateKeyPairAsync()
    {
        if (_mainWindow == null || _mainViewModel == null) return;
        await _mainViewModel.GenerateKeyPairFromMenuAsync(_mainWindow);
    }

    private async System.Threading.Tasks.Task LoadPublicKeyAsync()
    {
        if (_mainWindow == null || _mainViewModel == null) return;
        var localize = LocalizationService.Instance;

        var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localize["Dialog_SelectPublicKey"],
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Public Key Files") { Patterns = new[] { "*.atcpub" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            _mainViewModel.LoadPublicKey(path);
    }

    private async System.Threading.Tasks.Task LoadPrivateKeyAsync()
    {
        if (_mainWindow == null || _mainViewModel == null) return;
        var localize = LocalizationService.Instance;

        var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localize["Dialog_SelectPrivateKey"],
            AllowMultiple = false,
            FileTypeFilter =
            [
              new FilePickerFileType("Private Key Files") { Patterns = ["*.atcpvt"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            _mainViewModel.LoadPrivateKey(path);
    }

    private void LocalizeMacOsDefaultApplicationMenuItems()
    {
      // Application に付いている“アプリ名メニュー（左端）”の中身
      var menu = NativeMenu.GetMenu(this);
      if (menu == null) return;

      var appName = Name ?? "AttacheCase";

      string Get(string key, string fallback)
      {
        try
        {
          return LocalizationService.Instance[key];
        }
        catch
        {
          return fallback;
        }
      }

      // 推奨：フォーマットキーを用意（例）
      // MacMenu_HideFormat: ja="{0}を隠す", en="Hide {0}"
      // MacMenu_QuitFormat: ja="{0}を終了",   en="Quit {0}"
      var hideFmt = Get("MacMenu_HideFormat",
        LocalizationService.Instance.IsJapanese ? "{0}を隠す" : "Hide {0}");
      var quitFmt = Get("MacMenu_QuitFormat",
        LocalizationService.Instance.IsJapanese ? "{0}を終了" : "Quit {0}");

      foreach (var item in menu.Items)
      {
        if (item is not NativeMenuItem nmi) continue;
        if (nmi.Header is not { } header) continue;

        // 判定は「最初に見たときのヘッダー」で行う。
        // 表示されている文字列で判定すると、一度日本語に置き換えた後は
        // "Hide" で始まらなくなり、2 回目以降の言語切り替えが効かなくなる
        if (!s_originalMenuHeaders.TryGetValue(nmi, out var original))
        {
          original = header;
          s_originalMenuHeaders.Add(nmi, original);
        }

        // 既定項目を英語テキストで判定して差し替え（Avalonia既定が英語になりがちなため）
        if (original.StartsWith("Hide", StringComparison.OrdinalIgnoreCase))
        {
          if (string.Equals(original, "Hide Others", StringComparison.OrdinalIgnoreCase))
          {
            nmi.Header = Get("MacMenu_HideOthers", "他を非表示");
          }
          else
          {
            nmi.Header = string.Format(hideFmt, appName);
          }
        }
        else if (string.Equals(original, "Show All", StringComparison.OrdinalIgnoreCase))
        {
          nmi.Header = Get("MacMenu_ShowAll", "すべて表示");
        }
        else if (string.Equals(original, "Services", StringComparison.OrdinalIgnoreCase))
        {
          nmi.Header = Get("MacMenu_Services", "サービス");
        }
        else if (original.StartsWith("Quit", StringComparison.OrdinalIgnoreCase))
        {
          nmi.Header = string.Format(quitFmt, appName);
        }
      }
    }
}