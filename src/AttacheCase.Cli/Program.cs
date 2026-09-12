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
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using AttacheCase.Cli;
using AttacheCase.Headless;
using AttacheCase.Services;

namespace AttacheCase.CliApp;

internal static class Program
{
    private static bool _quiet;
    private static HeadlessRunner? _runner;
    private static bool _canceled;

    private static int Main(string[] args)
    {
        // DLL プリロード攻撃対策（Windows のみ）。他の何よりも先に呼ぶ。
        // 旧 Windows 版 5.0 が Main の冒頭でやっていたもの。詳細は DllSearchPathGuard
        AttacheCase.Platform.DllSearchPathGuard.Apply();

        // 日本語 Windows の端末は既定が CP932。ヘルプなどの UTF-8 出力が
        // 化けないよう、コンソール出力を UTF-8 に切り替える。
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception)
        {
            // リダイレクト先やコンソール無し環境では設定できないことがある。出力自体は可能
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
        // パスワード
        //-----------------------------------
        byte[]? passwordBinary = null;
        if (!string.IsNullOrEmpty(options.EncryptPasswordFilePath))
        {
            if (!File.Exists(options.EncryptPasswordFilePath))
            {
                WriteError("Password file not found: " + options.EncryptPasswordFilePath);
                return AtcExitCode.FileNotFound;
            }

            passwordBinary = PasswordFileHash.ComputeSha256(options.EncryptPasswordFilePath);
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
            jobs = BuildEncryptJobs(options);
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

            var request = new HeadlessEncryptRequest
            {
                InputPaths = job.InputPaths,
                OutputFilePath = job.OutputFilePath,
                Password = options.Password,
                PasswordBinary = passwordBinary,
                SelfExecutable = options.SelfExecutable,
                ZipEncryption = options.EncryptionType == CliEncryptionType.Zip,
                // HeadlessEncryptRequest.ZipAlgorithm は公開契約の 0/1/2。
                // enum 値（0/128/256）をそのままキャストしてはいけない
                ZipAlgorithm = (options.ZipAlgorithm ?? ZipPasswordEncryptionMethod.ZipCrypto) switch
                {
                    ZipPasswordEncryptionMethod.Aes128 => 1,
                    ZipPasswordEncryptionMethod.Aes256 => 2,
                    _ => 0,
                },
                KeepTimeStamp = options.KeepTimeStamp ?? false,
                Compression = options.Compression ?? CompressionLevel.Optimal,
                MissTypeLimits = options.MissTypeLimits ?? 3,
                Overwrite = options.OverwriteWithoutConfirm ?? false,
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
            if ((options.DeleteMode ?? 0) > 0)
            {
                var deleteExitCode = DeletePaths(job.InputPaths, options.DeleteMode!.Value, options);
                if (deleteExitCode != AtcExitCode.Success)
                {
                    return deleteExitCode;
                }
            }
        }

        return AtcExitCode.Success;
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
    private static List<EncryptJob> BuildEncryptJobs(CliOptions options)
    {
        var jobs = new List<EncryptJob>();

        //-----------------------------------
        // 既定: すべてを 1 つの暗号化ファイルにまとめる
        //-----------------------------------
        if (!(options.OneByOne ?? false))
        {
            jobs.Add(new EncryptJob
            {
                InputPaths = options.InputPaths,
                OutputFilePath = BuildEncryptOutputPath(options, options.InputPaths[0]),
            });
            return jobs;
        }

        //-----------------------------------
        // /oneby: フォルダーを再帰展開して 1 ファイルずつ処理する
        //-----------------------------------
        // /out= にファイル名まで指定されていると、出力先が 1 つに定まってしまい矛盾する
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
                    BuildEncryptOutputPath(options, filePath), usedOutputPaths),
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
    /// </summary>
    /// <remarks>
    /// 優先順位:
    /// <list type="number">
    ///   <item>/out で明示されたパス</item>
    ///   <item>/saveto（ZIP は /zipsaveto）のディレクトリ ＋ 最初の入力名</item>
    ///   <item>最初の入力と同じディレクトリ ＋ 最初の入力名</item>
    /// </list>
    /// </remarks>
    private static string BuildEncryptOutputPath(CliOptions options, string representativeInputPath)
    {
        var isZip = options.EncryptionType == CliEncryptionType.Zip;

        // 拡張子。偽装が指定されていればそちらを優先する（本体 GUI と同じ扱い）
        var extension = !string.IsNullOrEmpty(options.CamouflageExtension)
            ? options.CamouflageExtension
            : (isZip ? ".zip" : options.SelfExecutable ? ".exe" : ".atc");

        //-----------------------------------
        // 出力ディレクトリ
        //-----------------------------------
        string? baseDir;
        if (!string.IsNullOrEmpty(options.OutputPath))
        {
            var outPath = Path.GetFullPath(options.OutputPath);

            // ファイル名まで指定された場合はそれをそのまま使う（自動命名も行わない）
            if (!Directory.Exists(outPath))
            {
                return outPath;
            }

            baseDir = outPath;
        }
        else
        {
            var saveDir = isZip ? options.ZipOutputDirectory : options.EncryptOutputDirectory;
            baseDir = !string.IsNullOrEmpty(saveDir)
                ? Path.GetFullPath(saveDir)
                : Path.GetDirectoryName(representativeInputPath);
        }

        if (string.IsNullOrEmpty(baseDir))
        {
            throw new InvalidOperationException("Could not determine the output directory. Use /out= or /saveto=.");
        }

        //-----------------------------------
        // ファイル名
        //-----------------------------------
        var candidate = Path.Combine(baseDir, DefaultBaseName(options, representativeInputPath) + extension);

        if (string.IsNullOrEmpty(options.AutoNameFormat))
        {
            return candidate;
        }

        // 書式の中で <filename> は拡張子を除いた名前、<ext> は暗号化ファイルの拡張子を指す。
        // 本体 GUI と同じ扱い。
        var generated = AutoFileName.Format(
            options.AutoNameFormat, candidate, AutoNameCharset.Default, out var containsInvalidChar);

        if (containsInvalidChar)
        {
            throw new InvalidOperationException(
                "The generated file name contains a character that cannot be used: " + generated);
        }

        if (string.IsNullOrEmpty(generated))
        {
            return candidate;
        }

        return Path.Combine(baseDir, generated);
    }

    private static string DefaultBaseName(CliOptions options, string inputPath)
    {
        var first = inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (Directory.Exists(first))
        {
            return Path.GetFileName(first);
        }

        // /withext=1 なら元の拡張子を残す（report.xlsx → report.xlsx.atc）
        return (options.IncludeOriginalExtension ?? false)
            ? Path.GetFileName(first)
            : Path.GetFileNameWithoutExtension(first);
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
            byte[]? passwordBinary = null;
            if (!string.IsNullOrEmpty(options.DecryptPasswordFilePath))
            {
                if (!File.Exists(options.DecryptPasswordFilePath))
                {
                    WriteError("Password file not found: " + options.DecryptPasswordFilePath);
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

                passwordBinary = PasswordFileHash.ComputeForDataFileVersion(
                    options.DecryptPasswordFilePath, dataFileVersion);
            }

            var hasPrivateKeySource = !string.IsNullOrEmpty(options.RsaPrivateKeyFilePath)
                                      || options.UsePostPpapAccounts;

            if (!hasPrivateKeySource && string.IsNullOrEmpty(options.Password) && passwordBinary == null)
            {
                WriteError("No password is specified. Use /p=PASSWORD, /dpfile=FILE, /privkey=FILE or /p-ppap.");
                return AtcExitCode.UsageError;
            }

            //-----------------------------------
            // 展開先
            //-----------------------------------
            var outDir = options.OutputPath ?? options.DecryptOutputDirectory;
            outDir = string.IsNullOrEmpty(outDir)
                ? Path.GetDirectoryName(inputPath)
                : Path.GetFullPath(outDir);

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
                Overwrite = options.OverwriteWithoutConfirm ?? false,
                NoParentFolder = options.NoParentFolder ?? false,
                SetTimeStampToNow = options.SetTimeStampToNow ?? false,
                ContinueOnHashMismatch = options.ContinueOnHashMismatch ?? false,
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
            if ((options.DeleteEncryptedFileMode ?? 0) > 0)
            {
                var deleteExitCode = DeletePaths(
                    new List<string> { inputPath }, options.DeleteEncryptedFileMode!.Value, options);
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
    private static int DeletePaths(IList<string> paths, int mode, CliOptions options)
    {
        try
        {
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
                    options.DeleteRandomTimes ?? 1,
                    options.DeleteZeroTimes ?? 1);

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
