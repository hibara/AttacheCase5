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
// コマンドライン専用の実行ファイル（GUI・Avalonia を一切読み込まない）。
//
// Windows 版 AttacheCase.Cli\Program.cs の移植。標準出力には処理結果
// （出力パス）だけを、進捗と注意書きは標準エラー出力へ書く。
// 終了コードは AtcExitCode の凍結契約に従う。
// 暗号化・復号の実装は Core の HeadlessRunner にあり、このアプリは
// 引数解析と入出力の面倒を見るだけに留める。
//
// 設定の扱い（AttacheCase4 以来の規則。GUI 本体に引数を渡したときと同じ）:
//   保存されている設定（承認済みの持ち運び設定 _AtcCase.json があればそちら、
//   無ければ settings.json）を読み、その【複製】に引数で指定された項目だけを
//   上書きして使う。上書きはこの起動のあいだだけで、設定ファイルには書き戻さない。
//   引数を省略した項目は設定画面の内容がそのまま既定になる。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AttacheCase.Cli;
using AttacheCase.Headless;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.CliApp;

internal static class Program
{
    private static bool _quiet;
    private static HeadlessRunner? _runner;
    private static bool _canceled;

    /// <summary>この起動で使う設定（保存されている設定の複製に引数を上書きしたもの）</summary>
    private static AppSettings _settings = new();

    private static int Main(string[] args)
    {
        // DLL プリロード攻撃対策（Windows のみ）。他の何よりも先に呼ぶ。
        // 旧 Windows 版 5.0 が Main の冒頭でやっていたものに、.NET (Core) 向けの
        // P/Invoke リゾルバーと起動時チェックを足したもの。詳細は DllSearchPathGuard。
        // 単一ファイル発行で exe の隣に DLL があれば、標準エラーに理由を出して起動しない
        // （設定・リソースなど、他の何かを読む前に判定する）。
        // 検索順序を限定できなかったときも起動しない（黙って続けると対策前と同じ状態になる）
        var guardInstalled = AttacheCase.Platform.DllSearchPathGuard.Install();

        // 日本語 Windows の端末は既定が CP932。ヘルプなどの UTF-8 出力が
        // 化けないよう、コンソール出力を UTF-8 に切り替える（起動時チェックの
        // メッセージもこの文字コードで出したいので、チェックより前に行う。
        // kernel32 の SetConsoleOutputCP だけで、文字列ハッシュは使わない）
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception)
        {
            // リダイレクト先やコンソール無し環境では設定できないことがある。出力自体は可能
        }

        if (!guardInstalled)
        {
            Console.Error.WriteLine(AttacheCase.Platform.DllSearchPathGuard.BuildInstallFailureMessage());
            return AtcExitCode.GeneralError;
        }

        var foreignDlls = AttacheCase.Platform.DllSearchPathGuard.FindForeignDlls();
        if (foreignDlls.Count > 0)
        {
            Console.Error.WriteLine(AttacheCase.Platform.DllSearchPathGuard.BuildRefusalMessage(foreignDlls));
            return AtcExitCode.GeneralError;
        }

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            WriteError("Unexpected error: " + ex.Message);
            return AtcExitCode.InternalError;
        }
    }

    private static int Run(string[] args)
    {
        // 設定「表示言語」を最初に適用する（ヘルプの言語もこれに従う）
        LocalizationService.Instance.ApplyLanguageSetting(SettingsService.Instance.Current.Language);

        var options = CliParser.Parse(args);

        if (options.ParseError != null)
        {
            WriteError(options.ParseError);
            WriteError("Try \"AttacheCase.Cli --help\" for usage.");
            return options.ParseErrorCode;
        }

        switch (options.Action)
        {
            case CliAction.ShowHelp:
                CliHelpText.WriteHelp();
                return AtcExitCode.Success;

            case CliAction.ShowVersion:
                CliHelpText.WriteVersion();
                return AtcExitCode.Success;
        }

        _quiet = options.Quiet;

        foreach (var ignored in options.IgnoredOptions)
        {
            WriteVerbose("Note: \"" + ignored + "\" is a GUI option and has no effect here.");
        }

        foreach (var name in options.GuiOnlyOptionsGiven())
        {
            WriteVerbose("Note: \"" + name + "\" only affects the app window and has no effect here.");
        }

        //-----------------------------------
        // 保存されている設定の複製に、引数で指定された項目だけを上書きする。
        // 複製なので settings.json / _AtcCase.json には書き戻らない
        //-----------------------------------
        var store = SettingsService.Instance;
        _settings = options.ApplyTo(store.Current.Clone());

        if (store.PendingPortableFilePath != null)
        {
            // 未承認の持ち運び設定は使わない（承認はアプリの画面でしか行えない）
            WriteVerbose("Note: a portable settings file was found but has not been approved in the app, so it is ignored: "
                         + store.PendingPortableFilePath);
        }

        //-----------------------------------
        // 商用ライセンス操作（/lc）。処理対象パスは不要な単独コマンド
        //-----------------------------------
        if (options.LicenseFile != null)
        {
            return RunLicenseCommand(options.LicenseFile);
        }

        if (options.InputPaths.Count == 0)
        {
            WriteError("No input file or folder is specified.");
            return AtcExitCode.UsageError;
        }

        //-----------------------------------
        // 暗号化か復号かを決める
        //-----------------------------------
        var action = options.Action;
        if (action == CliAction.Auto)
        {
            action = LooksLikeEncryptedFile(options.InputPaths[0])
                ? CliAction.Decrypt
                : CliAction.Encrypt;
        }

        //-----------------------------------
        // Ctrl+C で処理を中断できるようにする
        //-----------------------------------
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // プロセスを即死させず、エンジンに後始末をさせる
            _canceled = true;
            _runner?.Cancel();
            WriteError("Canceling...");
        };

        _runner = new HeadlessRunner(ReportProgress);

        var exitCode = action == CliAction.Encrypt
            ? RunEncrypt(options)
            : RunDecrypt(options);

        if (_canceled && exitCode == AtcExitCode.Success)
        {
            return AtcExitCode.Canceled;
        }

        return exitCode;
    }

    //======================================================================
    // 商用ライセンス（/lc）
    //======================================================================

    /// <summary>
    /// /lc=ファイル でレジストレーションコードを登録、/lc=（空）で削除する。
    /// Windows 版 AppSettings の /lc 処理と同じ解釈:
    /// ファイルから 16 進文字だけを最大 512 文字集めてコードとして検証する。
    /// </summary>
    private static int RunLicenseCommand(string licenseFile)
    {
        if (licenseFile.Length == 0)
        {
            if (new LicenseRegister().DeleteLicense())
            {
                WriteVerbose("The commercial license has been removed.");
                return AtcExitCode.Success;
            }

            WriteError("Could not delete the registration code.");
            return AtcExitCode.GeneralError;
        }

        if (!File.Exists(licenseFile))
        {
            WriteError("License file not found: " + licenseFile);
            return AtcExitCode.FileNotFound;
        }

        // レジストレーションコードは 16 進 512 文字。ファイル中の 16 進文字だけを
        // 集め、そこまで達したら打ち切る（余分な文字を足すと検証に失敗するため）
        var code = new StringBuilder(512);
        foreach (var line in File.ReadLines(licenseFile))
        {
            foreach (var c in line)
            {
                if (Uri.IsHexDigit(c)) code.Append(c);
                if (code.Length >= 512) break;
            }

            if (code.Length >= 512) break;
        }

        var license = new LicenseRegister(code.ToString());
        if (license.Decrypt(fRegister: true))
        {
            WriteVerbose("The commercial license has been registered: " +
                         license.UserNameString + " <" + license.EmailAddressString + ">");
            return AtcExitCode.Success;
        }

        WriteError("The registration code is not valid.");
        return AtcExitCode.GeneralError;
    }

    //======================================================================
    // 暗号化
    //======================================================================

    private static int RunEncrypt(CliOptions options)
    {
        //-----------------------------------
        // 出力形式（設定「暗号化ファイルの種類」／引数 /entype, /exeout）
        //   自己実行形式は Windows 専用。設定に EXE が残っていても他の OS では
        //   .atc にする（GUI の EncryptionViewModel と同じ丸め方）
        //-----------------------------------
        var isZip = _settings.EncryptionFileType == EncryptionFileTypes.Zip;
        var selfExecutable = _settings.EncryptionFileType == EncryptionFileTypes.Exe
                             && OperatingSystem.IsWindows();

        // ZIP の暗号方式 3（暗号化なし）は設定画面だけの選択肢で、CLI の契約は 0〜2
        if (isZip && _settings.ZipAlgorithm is < 0 or > 2)
        {
            WriteError("The ZIP cipher saved in the settings (" + _settings.ZipAlgorithm +
                       ") cannot be used from the command line. Specify /zipalgo=0|1|2.");
            return AtcExitCode.UsageError;
        }

        //-----------------------------------
        // パスワード
        //   /pfile が無ければ設定「暗号化時にパスワードファイルを自動チェックする」に従う
        //-----------------------------------
        byte[]? passwordBinary = null;
        var passwordFile = options.EncryptPasswordFilePath;
        if (string.IsNullOrEmpty(passwordFile))
        {
            passwordFile = ResolveConfiguredPasswordFile(
                _settings.CheckPasswordFileOnEncrypt, _settings.EncryptPasswordFilePath);
        }

        if (!string.IsNullOrEmpty(passwordFile))
        {
            if (!File.Exists(passwordFile))
            {
                WriteError("Password file not found: " + passwordFile);
                return AtcExitCode.FileNotFound;
            }

            passwordBinary = PasswordFileHash.ComputeSha256(passwordFile);
        }

        var usePublicKey = !string.IsNullOrEmpty(options.RsaPublicKeyFilePath)
                           || !string.IsNullOrEmpty(options.PostPpapRecipientEmail);

        if (!usePublicKey && string.IsNullOrEmpty(options.Password) && passwordBinary == null)
        {
            WriteError("No password is specified. Use /p=PASSWORD, /pfile=FILE, /pubkey=FILE or /p-ppap=EMAIL.");
            return AtcExitCode.UsageError;
        }

        //-----------------------------------
        // 処理単位の組み立て
        //   既定           : 指定されたすべてを 1 つの暗号化ファイルにまとめる
        //   /oneby 指定時  : フォルダーを再帰展開し、ファイル 1 つにつき 1 つの暗号化ファイルを作る
        //-----------------------------------
        List<EncryptJob> jobs;
        try
        {
            jobs = BuildEncryptJobs(options, isZip, selfExecutable);
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return AtcExitCode.UsageError;
        }

        if (jobs.Count == 0)
        {
            WriteError("There is no file to encrypt.");
            return AtcExitCode.FileNotFound;
        }

        //-----------------------------------
        // 実行
        //   途中で失敗したらそこで中断する。
        //   無人バッチで一部の失敗を見逃すほうが危険なため。
        //-----------------------------------
        foreach (var job in jobs)
        {
            if (_canceled) return AtcExitCode.Canceled;

            var outDir = Path.GetDirectoryName(job.OutputFilePath);
            if (string.IsNullOrEmpty(outDir) || !Directory.Exists(outDir))
            {
                WriteError("Output directory not found: " + outDir);
                return AtcExitCode.FileNotFound;
            }

            // 値はすべて「設定に引数を上書きした結果」から取る（引数省略時は設定の内容）
            var request = new HeadlessEncryptRequest
            {
                InputPaths = job.InputPaths,
                OutputFilePath = job.OutputFilePath,
                Password = options.Password,
                PasswordBinary = passwordBinary,
                SelfExecutable = selfExecutable,
                ZipEncryption = isZip,
                // AppSettings.ZipAlgorithm と HeadlessEncryptRequest.ZipAlgorithm は
                // どちらも公開契約の 0/1/2（enum 値 0/128/256 ではない）
                ZipAlgorithm = _settings.ZipAlgorithm,
                KeepTimeStamp = _settings.KeepTimeStamp,
                Compression = _settings.CompressionLevelValue,
                MissTypeLimits = _settings.MissTypeLimits,
                BreakFileWhenLimitExceeded = _settings.BreakFileWhenLimitExceeded,
                // 端末では上書きの確認を出せないので、「確認する」＝上書きしない（終了コード 5）
                Overwrite = !_settings.ConfirmOverwrite,
                RsaPublicKeyFilePath = options.RsaPublicKeyFilePath,
                PostPpapRecipientEmail = options.PostPpapRecipientEmail,
            };

            var result = _runner!.Encrypt(request);
            ClearProgressLine();

            if (!result.Succeeded)
            {
                WriteError(FormatError(result));
                return result.ExitCode;
            }

            foreach (var path in result.OutputPaths)
            {
                Console.Out.WriteLine(path);
            }

            //-----------------------------------
            // 元ファイルの削除
            //   その処理単位が成功した直後に削除する（まとめて最後に消さない）
            //-----------------------------------
            if (_settings.DeleteMode > 0)
            {
                var deleteExitCode = DeletePaths(job.InputPaths, _settings.DeleteMode);
                if (deleteExitCode != AtcExitCode.Success)
                {
                    return deleteExitCode;
                }
            }
        }

        return AtcExitCode.Success;
    }

    /// <summary>
    /// 設定「パスワードファイルを自動チェックする」に従い、使うパスワードファイルを決める。
    /// 無ければ「無くてもエラーを出さない」の設定に従って知らせ、null（パスワードファイル無し）を返す。
    /// GUI の PasswordFileLoader.TryLoadConfiguredAsync と同じ振る舞い。
    /// </summary>
    private static string? ResolveConfiguredPasswordFile(bool enabled, string configuredPath)
    {
        if (!enabled) return null;

        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        if (!_settings.NoErrorWhenPasswordFileMissing)
        {
            // 設定の不備を知らせる警告なので /q でも抑止しない
            WriteError("Warning: the password file set in the settings was not found: " + configuredPath);
        }

        return null;
    }

    /// <summary>
    /// 1 回分の暗号化処理。
    /// </summary>
    private sealed class EncryptJob
    {
        public required IList<string> InputPaths { get; init; }
        public required string OutputFilePath { get; init; }
    }

    /// <summary>
    /// 入力から暗号化の処理単位を組み立てる。
    /// </summary>
    private static List<EncryptJob> BuildEncryptJobs(CliOptions options, bool isZip, bool selfExecutable)
    {
        var jobs = new List<EncryptJob>();

        //-----------------------------------
        // 既定: すべてを 1 つの暗号化ファイルにまとめる
        //-----------------------------------
        if (!_settings.EncryptOneByOne)
        {
            jobs.Add(new EncryptJob
            {
                InputPaths = options.InputPaths,
                OutputFilePath = BuildEncryptOutputPath(options, options.InputPaths.ToArray(), isZip, selfExecutable),
            });
            return jobs;
        }

        //-----------------------------------
        // /oneby: フォルダーを再帰展開して 1 ファイルずつ処理する
        //-----------------------------------
        // /out= にファイル名まで指定されていると、出力先が 1 つに定まってしまい矛盾する
        // （CliOutputPath も同じ検査をするが、こちらは英語の定型文で早く止める）
        if (!string.IsNullOrEmpty(options.OutputPath) &&
            !Directory.Exists(Path.GetFullPath(options.OutputPath)))
        {
            throw new InvalidOperationException(
                "/oneby cannot be combined with a single output file. Specify a folder with /out= or /saveto=.");
        }

        // 出力名は入力のベース名から作るため、/saveto や /out で 1 つの
        // フォルダーへ集めると、別サブフォルダーの同名ファイル
        // （dir\a\report.xlsx と dir\b\report.xlsx）が同じ出力パスに写像される。
        // そのままだと /y 併用で先に作った暗号化ファイルが黙って上書きされる
        // （不許可なら途中で終了コード 5）。連番を付けて一意化する
        var usedOutputPaths = new HashSet<string>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

        // まず処理対象のファイルを確定させる。
        // 本体 GUI と同じく、フォルダーはサブフォルダー以下のすべてのファイルを
        // 対象にする（拡張子による除外は行わない）
        var targetFiles = new List<string>();
        foreach (var inputPath in options.InputPaths)
        {
            if (Directory.Exists(inputPath))
            {
                targetFiles.AddRange(EnumerateFiles(inputPath));
            }
            else
            {
                targetFiles.Add(inputPath);
            }
        }

        // 入力ファイルそのものを出力候補から除外するため、先に全部予約する。
        // 予約しないと、先行ジョブの出力先に後続ジョブの入力
        // （例: 対象フォルダーに同梱されていた foo.atc）が選ばれ、
        // /y 併用で処理前の入力を暗号化データで上書きしてしまう
        foreach (var filePath in targetFiles)
        {
            usedOutputPaths.Add(Path.GetFullPath(filePath));
        }

        foreach (var filePath in targetFiles)
        {
            jobs.Add(new EncryptJob
            {
                InputPaths = new List<string> { filePath },
                OutputFilePath = MakeUniqueOutputPath(
                    BuildEncryptOutputPath(options, [filePath], isZip, selfExecutable), usedOutputPaths),
            });
        }

        return jobs;
    }

    /// <summary>
    /// 同じ実行内で出力パスが重複したら、拡張子の前に _2, _3, ... を付けて
    /// 一意にする（ディスク上の既存ファイルとの衝突は /ow の管轄で、ここでは見ない）。
    /// </summary>
    private static string MakeUniqueOutputPath(string path, HashSet<string> used)
    {
        if (used.Add(path)) return path;

        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, name + "_" + i + ext);
            if (used.Add(candidate)) return candidate;
        }
    }

    /// <summary>
    /// フォルダー配下のファイルを再帰的に列挙する。
    /// </summary>
    private static IEnumerable<string> EnumerateFiles(string rootFolderPath)
    {
        var pending = new Queue<string>();
        pending.Enqueue(rootFolderPath);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            foreach (var filePath in Directory.GetFiles(current, "*"))
            {
                yield return filePath;
            }

            foreach (var directoryPath in Directory.GetDirectories(current))
            {
                // 再解析ポイント（ジャンクション、シンボリックリンク）は無限再帰を避けるため辿らない
                var attributes = new DirectoryInfo(directoryPath).Attributes;
                if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;

                pending.Enqueue(directoryPath);
            }
        }
    }

    /// <summary>
    /// 暗号化ファイルの出力パスを決める。
    /// 規則（/out → 保存先の設定 /saveto・/zipsaveto → 入力と同じフォルダー、
    /// 拡張子の偽装、元拡張子の保持、自動命名）は GUI と共通の
    /// <see cref="CliOutputPath"/> にあり、この起動の設定に従う。
    /// </summary>
    private static string BuildEncryptOutputPath(
        CliOptions options, string[] inputPaths, bool isZip, bool selfExecutable)
    {
        var context = new RunContext
        {
            Settings = _settings,
            IsCommandLineRun = true,
            OutputPath = options.OutputPath,
        };

        return isZip
            ? CliOutputPath.ForZip(inputPaths, context)
            : CliOutputPath.ForEncryption(inputPaths, context, selfExecutable ? ".exe" : ".atc");
    }

    //======================================================================
    // 復号
    //======================================================================

    private static int RunDecrypt(CliOptions options)
    {
        foreach (var inputPath in options.InputPaths)
        {
            if (_canceled) return AtcExitCode.Canceled;

            if (!File.Exists(inputPath))
            {
                WriteError("Encrypted file not found: " + inputPath);
                return AtcExitCode.FileNotFound;
            }

            //-----------------------------------
            // パスワード
            //   パスワードファイルの鍵素材はデータバージョンによって算出方法が変わるため、
            //   実際のバージョンを見てから求める。
            //-----------------------------------
            // /dpfile が無ければ設定「復号時にパスワードファイルを自動チェックする」に従う
            byte[]? passwordBinary = null;
            var passwordFile = options.DecryptPasswordFilePath;
            if (string.IsNullOrEmpty(passwordFile))
            {
                passwordFile = ResolveConfiguredPasswordFile(
                    _settings.CheckPasswordFileOnDecrypt, _settings.DecryptPasswordFilePath);
            }

            if (!string.IsNullOrEmpty(passwordFile))
            {
                if (!File.Exists(passwordFile))
                {
                    WriteError("Password file not found: " + passwordFile);
                    return AtcExitCode.FileNotFound;
                }

                // 版数が読めないまま ComputeForDataFileVersion(…, 0) に流すと
                // 旧方式ハッシュに倒れ、あとで版数検出に成功した場合に間違った
                // 鍵素材のまま「パスワードが違う」(4) になって紛らわしい。
                // 読めない理由を実際に開いて確かめ、正しい終了コードで止める
                var dataFileVersion = HeadlessProbe.GetDataFileVersion(inputPath);
                if (dataFileVersion <= 0)
                {
                    try
                    {
                        using var probeFs = new FileStream(
                            inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        // 読めるのに版数が取れない = AttacheCase のファイルではない
                        WriteError("Not a supported AttacheCase file: " + inputPath);
                        return AtcExitCode.UnsupportedFormat;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        WriteError("Access denied: " + inputPath);
                        return AtcExitCode.AccessDenied;
                    }
                    catch (IOException ex)
                    {
                        WriteError("Could not read the file: " + inputPath + " (" + ex.Message + ")");
                        return AtcExitCode.GeneralError;
                    }
                }

                passwordBinary = PasswordFileHash.ComputeForDataFileVersion(passwordFile, dataFileVersion);
            }

            var hasPrivateKeySource = !string.IsNullOrEmpty(options.RsaPrivateKeyFilePath)
                                      || options.UsePostPpapAccounts;

            if (!hasPrivateKeySource && string.IsNullOrEmpty(options.Password) && passwordBinary == null)
            {
                WriteError("No password is specified. Use /p=PASSWORD, /dpfile=FILE, /privkey=FILE or /p-ppap.");
                return AtcExitCode.UsageError;
            }

            //-----------------------------------
            // 展開先（/out → 保存先の設定 /dsaveto → 暗号化ファイルと同じフォルダー。GUI と共通）
            //-----------------------------------
            var outDir = CliOutputPath.ForDecryption(inputPath, new RunContext
            {
                Settings = _settings,
                IsCommandLineRun = true,
                OutputPath = options.OutputPath,
            });

            if (string.IsNullOrEmpty(outDir))
            {
                WriteError("Could not determine the output directory. Use /out= or /dsaveto=.");
                return AtcExitCode.UsageError;
            }

            if (!Directory.Exists(outDir))
            {
                try
                {
                    Directory.CreateDirectory(outDir);
                }
                catch (Exception ex)
                {
                    WriteError("Could not create the output directory: " + outDir + " (" + ex.Message + ")");
                    return AtcExitCode.AccessDenied;
                }
            }

            //-----------------------------------
            // 実行
            //-----------------------------------
            var request = new HeadlessDecryptRequest
            {
                InputFilePath = inputPath,
                OutputDirectory = outDir,
                Password = options.Password,
                PasswordBinary = passwordBinary,
                // 端末では上書きの確認を出せないので、「確認する」＝既存ファイルは残す（スキップ）
                Overwrite = !_settings.ConfirmOverwrite,
                NoParentFolder = _settings.NoParentFolder,
                SetTimeStampToNow = _settings.SetTimeStampToNow,
                ContinueOnHashMismatch = _settings.ContinueOnHashMismatch,
                RsaPrivateKeyFilePath = options.RsaPrivateKeyFilePath,
                UsePostPpapAccounts = options.UsePostPpapAccounts,
            };

            var result = _runner!.Decrypt(request);
            ClearProgressLine();

            if (!result.Succeeded)
            {
                WriteError(FormatError(result));
                return result.ExitCode;
            }

            Console.Out.WriteLine(outDir);

            //-----------------------------------
            // 暗号化ファイル自体の削除
            //-----------------------------------
            if (_settings.DeleteEncryptedFileMode > 0)
            {
                var deleteExitCode = DeletePaths(
                    new List<string> { inputPath }, _settings.DeleteEncryptedFileMode);
                if (deleteExitCode != AtcExitCode.Success)
                {
                    return deleteExitCode;
                }
            }
        }

        return AtcExitCode.Success;
    }

    //======================================================================
    // 削除
    //======================================================================

    /// <summary>
    /// 処理が成功したあとに、対象ファイル／フォルダーを削除する。
    /// </summary>
    /// <param name="mode">1: 通常削除 / 2: ごみ箱へ移動 / 3: 完全削除（ランダム値とゼロで上書き）</param>
    /// <remarks>
    /// 設定「削除の前に確認する」は端末では確認を出せないので、無人実行の GUI と同じく
    /// 指示どおり削除する。書き込み回数は設定（/delrand, /delnull）に従う。
    /// </remarks>
    private static int DeletePaths(IList<string> paths, int mode)
    {
        try
        {
            // 設定ファイルの破損などで未定義の番号が入ると、「ごみ箱」のつもりが
            // 復元できない削除になりかねない。手前で弾く（GUI の PostProcessHelper と同じ）
            if (mode is not (1 or 2 or 3))
            {
                WriteError("Unknown delete mode in the settings: " + mode + ". Skipped deletion.");
                return AtcExitCode.UsageError;
            }

            if (mode == 2 && OperatingSystem.IsWindows())
            {
                // ごみ箱が使えない場所では Windows が黙って完全削除にフォールバックするため、
                // ローカル固定ドライブ上にあることを事前に確認し、満たさなければ実行しない
                foreach (var path in paths)
                {
                    if (!AttacheCase.Platform.WindowsRecycleBin.CanUseRecycleBin(path, out var reason))
                    {
                        WriteError("Cannot use the Recycle Bin for this path (" + reason + "): " + path);
                        WriteError("Windows would delete it permanently without asking. Use /del=1 (delete) or /del=3 (wipe) instead.");
                        return AtcExitCode.UsageError;
                    }
                }
            }

            foreach (var path in paths)
            {
                var ok = FileEraser.Erase(
                    path,
                    (FileEraser.Mode)mode,
                    _settings.DeleteRandomTimes,
                    _settings.DeleteZeroTimes);

                if (!ok)
                {
                    WriteError("Failed to delete the original files: " + path);
                    return AtcExitCode.GeneralError;
                }
            }

            return AtcExitCode.Success;
        }
        catch (UnauthorizedAccessException ex)
        {
            WriteError("Failed to delete the original files: " + ex.Message);
            return AtcExitCode.AccessDenied;
        }
        catch (Exception ex)
        {
            WriteError("Failed to delete the original files: " + ex.Message);
            return AtcExitCode.GeneralError;
        }
    }

    //======================================================================
    // 補助
    //======================================================================

    /// <summary>
    /// 拡張子から暗号化ファイルらしさを判定する（処理種別が未指定のときの推定用）。
    /// </summary>
    private static bool LooksLikeEncryptedFile(string path)
    {
        if (!File.Exists(path)) return false;

        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".atc", StringComparison.OrdinalIgnoreCase)) return true;

        // 拡張子を偽装している場合もあるため、ヘッダも確認する
        return HeadlessProbe.GetDataFileVersion(path) > 0;
    }

    private static string FormatError(HeadlessResult result)
    {
        var message = string.IsNullOrEmpty(result.ErrorMessage)
            ? "Failed. (exit code " + result.ExitCode + ")"
            : result.ErrorMessage;

        return string.IsNullOrEmpty(result.ErrorFilePath)
            ? message
            : message + " [" + result.ErrorFilePath + "]";
    }

    //======================================================================
    // コンソール出力
    //   進捗と注意書きは標準エラー出力へ、処理結果（出力パス）は標準出力へ。
    //   標準出力をパイプやリダイレクトで受けても進捗が混ざらないようにするため。
    //======================================================================

    private static int _progressLineLength;

    private static void ReportProgress(HeadlessProgress progress)
    {
        if (_quiet || Console.IsErrorRedirected) return;

        var label = progress.Phase switch
        {
            HeadlessPhase.Encrypting => "Encrypting",
            HeadlessPhase.Decrypting => "Decrypting",
            HeadlessPhase.Deleting => "Deleting",
            HeadlessPhase.Completed => "Completed",
            _ => "Preparing",
        };

        var percent = progress.Percent < 0 ? "--" : progress.Percent.ToString("F1");
        var line = label + " " + percent + "%  " + Truncate(progress.CurrentItem, 60);

        Console.Error.Write("\r" + line.PadRight(Math.Max(_progressLineLength, line.Length)));
        _progressLineLength = line.Length;
    }

    private static void ClearProgressLine()
    {
        if (_progressLineLength == 0 || Console.IsErrorRedirected) return;
        Console.Error.Write("\r" + new string(' ', _progressLineLength) + "\r");
        _progressLineLength = 0;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : "..." + text[^(maxLength - 3)..];
    }

    private static void WriteError(string message)
    {
        ClearProgressLine();
        Console.Error.WriteLine(message);
    }

    private static void WriteVerbose(string message)
    {
        if (_quiet) return;
        Console.Error.WriteLine(message);
    }
}
