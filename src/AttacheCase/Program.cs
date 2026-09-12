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
// エントリーポイント。
//
// コマンドライン引数はここで解析し、GUI へ引き渡す。
// Windows 版のようにコマンドライン専用の実行ファイル（AttacheCase.Cli.exe）は
// 設けず、引数を付けて起動した場合も本体のウィンドウを表示して処理する。
// コンソールだけで完結するのは --help / --version と、引数の指定が誤っていた
// ときのエラー表示のみ。
//----------------------------------------------------------------------

using System;
using System.IO;
using AttacheCase.Cli;
using AttacheCase.Services;
using Avalonia;

namespace AttacheCase;

class Program
{
    /// <summary>複数起動を防ぐためのロックと、引数の受け渡し口</summary>
    private static SingleInstanceService? _singleInstance;

    /// <summary>
    /// Windows 用の名前付き Mutex。インストーラ（Inno Setup の AppMutex=AttacheCase5）が
    /// 「本体が起動中か」を検出するために見る。アプリ内の単一インスタンス制御は
    /// SingleInstanceService（ソケット + ロックファイル）が担い、これは検出専用。
    /// 名前は installer\AttacheCase.iss の AppMutex と完全に一致させること。
    /// Debug ビルドは別名にして、開発中の起動がリリースのインストールを妨げないようにする。
    /// </summary>
#if DEBUG
    private const string WindowsAppMutexName = "AttacheCase5-Test";
#else
    private const string WindowsAppMutexName = "AttacheCase5";
#endif
    private static System.Threading.Mutex? _windowsAppMutex;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // DLL プリロード攻撃対策（Windows のみ）。他の何よりも先に呼ぶ。
        // 旧 Windows 版 5.0 が Main の冒頭でやっていたもの。詳細は DllSearchPathGuard
        AttacheCase.Platform.DllSearchPathGuard.Apply();

        // インストーラの起動中検出用（掴んだまま。プロセス終了で自然に解放される）
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _windowsAppMutex = new System.Threading.Mutex(false, WindowsAppMutexName);
            }
            catch (Exception)
            {
                // 検出用なので、作れなくても起動は続ける
            }
        }

        // 設定「表示言語」を最初に適用する（ヘルプの言語もこれに従う）
        LocalizationService.Instance.ApplyLanguageSetting(SettingsService.Instance.Current.Language);

        var cli = CliParser.Parse(args);

        switch (cli.Action)
        {
            case CliAction.ShowHelp:
                CliHelpText.WriteHelp();
                return AtcExitCode.Success;

            case CliAction.ShowVersion:
                CliHelpText.WriteVersion();
                return AtcExitCode.Success;
        }

        if (cli.ParseError != null)
        {
            Console.Error.WriteLine(cli.ParseError);
            Console.Error.WriteLine("--help で使い方を表示します。 / Run with --help to see the usage.");
            return cli.ParseErrorCode;
        }

        if (!cli.Quiet && cli.IgnoredOptions.Count > 0)
        {
            Console.Error.WriteLine(
                "次のオプションは macOS 版では効果がありません（受理のみ）: / " +
                "The following options have no effect on macOS: " +
                string.Join(", ", cli.IgnoredOptions));
        }

        // 設定「アタッシェケースを複数起動しない」
        var startupSettings = cli.ApplyTo(SettingsService.Instance.Current.Clone());
        if (startupSettings.NoMultipleInstance)
        {
            var handled = HandleSingleInstance(args, cli);
            if (handled.HasValue) return handled.Value;
        }

        // 何か指定があるときだけ GUI へ引き渡す
        CommandLineStartup.Options = cli.HasAnyInstruction ? cli : null;

        try
        {
            // Avalonia 自身の引数解析に「/p=...」などを渡さないよう、空の配列を与える
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        finally
        {
            // ロックを解放し、ソケットファイルを片付ける
            _singleInstance?.Dispose();
        }
    }

    /// <summary>
    /// 複数起動しない設定のときの入口。
    /// 1 つ目のプロセスならロックを取って起動を続け（戻り値 null）、
    /// 2 つ目以降なら引数を 1 つ目へ渡して終了する（戻り値が終了コード）。
    /// </summary>
    private static int? HandleSingleInstance(string[] args, CliOptions cli)
    {
        var directory = Path.GetDirectoryName(SettingsService.Instance.FilePath);
        if (string.IsNullOrEmpty(directory)) return null;

        var service = new SingleInstanceService(directory);

        if (service.TryAcquire())
        {
            // 1 つ目のプロセス。ウィンドウが出てから受け付けを始める（App 側で StartListening）
            _singleInstance = service;
            return null;
        }

        // すでに起動している。処理してほしいものがあれば、そちらへ渡す
        if (!cli.HasAnyInstruction)
        {
            Console.Error.WriteLine(
                "アタッシェケースはすでに起動しています。 / AttacheCase is already running.");
            return AtcExitCode.GeneralError;
        }

        if (service.TrySendArguments(args, TimeSpan.FromSeconds(5)))
        {
            if (!cli.Quiet)
            {
                Console.Error.WriteLine(
                    "すでに起動しているアタッシェケースで処理します。 / " +
                    "Handed over to the running AttacheCase.");
            }
            return AtcExitCode.Success;
        }

        Console.Error.WriteLine(
            "すでに起動しているアタッシェケースが受け付けられませんでした（処理中の可能性があります）。 / " +
            "The running AttacheCase did not accept the request (it may be busy).");
        return AtcExitCode.GeneralError;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
