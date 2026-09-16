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
// コマンドライン引数の解析（macOS 版）。
//
// Windows 版は AttacheCase.exe（GUI に引数を渡す / AppSettings.ParserArguments）と
// AttacheCase.Cli.exe（ヘッドレス実行 / CliOptions.cs）に分かれているが、
// macOS 版は実行ファイルを分けず、本体がそのまま引数を受け取る。
// 引数を付けて起動した場合も GUI は必ず表示され、処理は画面上で進行する。
//
// 表記は Windows と同じ /name=value を第一とし、--name=value / --name value も受理する。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.Cli;

/// <summary>実行する処理の種類。</summary>
public enum CliAction
{
    /// <summary>未指定（ファイルの内容から自動判定する）</summary>
    Auto,
    Encrypt,
    Decrypt,
    ShowHelp,
    ShowVersion,
}

/// <summary>暗号化ファイルの種類（/entype）。</summary>
public enum CliEncryptionType
{
    /// <summary>通常の暗号化ファイル（.atc）</summary>
    Atc = 1,

    /// <summary>パスワード付き ZIP</summary>
    Zip = 3,
}

/// <summary>
/// コマンドラインから解析した実行条件。
/// </summary>
public sealed class CliOptions
{
    public CliAction Action { get; set; } = CliAction.Auto;

    public List<string> InputPaths { get; } = [];

    public string? Password { get; set; }
    public string? EncryptPasswordFilePath { get; set; }
    public string? DecryptPasswordFilePath { get; set; }

    /// <summary>RSA 公開鍵ファイル（.atcpub）。暗号化に使う</summary>
    public string? RsaPublicKeyFilePath { get; set; }

    /// <summary>RSA 秘密鍵ファイル（.atcpvt）。復号に使う</summary>
    public string? RsaPrivateKeyFilePath { get; set; }

    /// <summary>Post-PPAP の宛先メールアドレス。暗号化時にキーサーバを検索する</summary>
    public string? PostPpapRecipientEmail { get; set; }

    /// <summary>この PC に登録済みの Post-PPAP アカウントで復号する（/p-ppap を値なしで指定）</summary>
    public bool UsePostPpapAccounts { get; set; }

    /// <summary>
    /// 自己実行形式（.exe）の指定（/exeout=0|1, /entype=2。Windows のみ）。
    /// null は「指定なし」で、保存されている設定（暗号化ファイルの種類）に従う。
    /// </summary>
    public bool? SelfExecutableOption { get; set; }

    /// <summary>自己実行形式（.exe）で出力すると明示されたか</summary>
    public bool SelfExecutable => SelfExecutableOption == true;

    /// <summary>
    /// 商用ライセンス操作（/lc）。null = 指定なし、空文字 = ライセンス削除、
    /// それ以外 = レジストレーションコードが書かれたファイルのパス。
    /// </summary>
    public string? LicenseFile { get; set; }

    /// <summary>RSA 鍵ペアの出力先ディレクトリ（/genrsa）</summary>
    public string? GenerateRsaKeyOutputDir { get; set; }

    /// <summary>出力先。暗号化ならファイルパスまたはディレクトリ、復号なら展開先ディレクトリ</summary>
    public string? OutputPath { get; set; }

    /// <summary>暗号化ファイルの保存先ディレクトリ（/saveto）</summary>
    public string? EncryptOutputDirectory { get; set; }

    /// <summary>復号先ディレクトリ（/dsaveto）</summary>
    public string? DecryptOutputDirectory { get; set; }

    /// <summary>パスワード ZIP の保存先ディレクトリ（/zipsaveto）</summary>
    public string? ZipOutputDirectory { get; set; }

    // ここから下は保存されている設定を上書きする項目。
    // 「指定が無かった」と「0 を指定した」を区別する必要があるため null 許容にしてある。

    /// <summary>パスワード ZIP の暗号方式（/zipalgo）</summary>
    public ZipPasswordEncryptionMethod? ZipAlgorithm { get; set; }

    /// <summary>暗号化ファイルの種類（/entype）</summary>
    public CliEncryptionType? EncryptionType { get; set; }

    /// <summary>
    /// 同名ファイルを確認なしで上書きするか（/ow=0, /y, /force）。
    /// 既定は GUI と同じく上書き確認ダイアログを出す。
    /// </summary>
    public bool? OverwriteWithoutConfirm { get; set; }

    /// <summary>暗号化ファイル名の書式（/autoname）。空文字なら自動命名しない</summary>
    public string? AutoNameFormat { get; set; }

    /// <summary>偽装する拡張子（/camoext）。空文字なら偽装しない</summary>
    public string? CamouflageExtension { get; set; }

    /// <summary>暗号化ファイル名に元ファイルの拡張子を含める（/withext）</summary>
    public bool? IncludeOriginalExtension { get; set; }

    /// <summary>
    /// フォルダー内のファイルを 1 つずつ個別に暗号化する（/oneby）。
    /// false なら指定されたすべてを 1 つの暗号化ファイルにまとめる（/allpack）。
    /// </summary>
    public bool? OneByOne { get; set; }

    /// <summary>圧縮レベル（/complv, /comprate）。null なら設定に従う</summary>
    public CompressionLevel? Compression { get; set; }

    /// <summary>暗号化ファイルのタイムスタンプを元ファイルに合わせる（/orgdt）</summary>
    public bool? KeepTimeStamp { get; set; }

    /// <summary>復号したファイルのタイムスタンプを復号日時にする（/now）</summary>
    public bool? SetTimeStampToNow { get; set; }

    /// <summary>復号時に親フォルダーを作らない（/nopfldr）</summary>
    public bool? NoParentFolder { get; set; }

    /// <summary>暗号化後に元ファイルを削除する方法（0: 削除しない, 1: 通常削除, 2: ごみ箱, 3: 完全削除）</summary>
    public int? DeleteMode { get; set; }

    /// <summary>復号後に暗号化ファイルを削除する方法（0: 削除しない, 1: 通常削除, 2: ごみ箱, 3: 完全削除）</summary>
    public int? DeleteEncryptedFileMode { get; set; }

    public int? DeleteRandomTimes { get; set; }
    public int? DeleteZeroTimes { get; set; }

    /// <summary>削除の前に確認する（/comfdel）</summary>
    public bool? ConfirmDelete { get; set; }

    /// <summary>パスワードのタイプミス制限回数（/typelimit）</summary>
    public int? MissTypeLimits { get; set; }

    /// <summary>CRC32 が一致しなくても復号を続行する（/ignorecrc）</summary>
    public bool? ContinueOnHashMismatch { get; set; }

    /// <summary>復号したファイルを関連付けられたアプリで開く（/opf）</summary>
    public bool? OpenDecryptedFile { get; set; }

    /// <summary>パスワードを隠さずに入力する（/nohide）</summary>
    public bool? NotMaskPassword { get; set; }

    /// <summary>メニューバーにアイコンを表示する（/tsktr）</summary>
    public bool? ShowMenuBarIcon { get; set; }

    /// <summary>ウィンドウを最前面に表示する（/front）</summary>
    public bool? WindowForeground { get; set; }

    /// <summary>複数起動しない（/nomulti）</summary>
    public bool? NoMultipleInstance { get; set; }

    /// <summary>パスワードファイルのドラッグ＆ドロップを許可する（/pf）</summary>
    public bool? AllowPasswordFileDragDrop { get; set; }

    /// <summary>パスワードファイルが無くてもエラーを出さない（/nomsgp）</summary>
    public bool? NoErrorWhenPasswordFileMissing { get; set; }

    /// <summary>入力制限を超えたら暗号化ファイルを破壊する（/breakfile）</summary>
    public bool? BreakFileWhenLimitExceeded { get; set; }

    /// <summary>
    /// 処理が終わったらアプリを終了する（/exit）。
    /// スクリプトから呼んだときにウィンドウが残らないようにするための指定。
    /// </summary>
    public bool? ExitAfterProcess { get; set; }

    /// <summary>注意書きを標準エラー出力に出さない（/q）</summary>
    public bool Quiet { get; set; }

    /// <summary>解析エラーの内容（null なら正常）</summary>
    public string? ParseError { get; set; }

    /// <summary>解析エラー時に返す終了コード</summary>
    public int ParseErrorCode { get; set; } = AtcExitCode.UsageError;

    /// <summary>受理したが効果のないオプション（警告表示用）</summary>
    public List<string> IgnoredOptions { get; } = [];

    /// <summary>設定を上書きする指定が 1 つでもあったか</summary>
    public bool HasSettingOverride =>
        ExitAfterProcess.HasValue || OpenDecryptedFile.HasValue || NotMaskPassword.HasValue ||
        ShowMenuBarIcon.HasValue || WindowForeground.HasValue || NoMultipleInstance.HasValue ||
        AllowPasswordFileDragDrop.HasValue || NoErrorWhenPasswordFileMissing.HasValue ||
        BreakFileWhenLimitExceeded.HasValue ||
        EncryptionType.HasValue || SelfExecutableOption.HasValue ||
        OverwriteWithoutConfirm.HasValue || OneByOne.HasValue ||
        KeepTimeStamp.HasValue || IncludeOriginalExtension.HasValue || Compression.HasValue ||
        MissTypeLimits.HasValue || ZipAlgorithm.HasValue || NoParentFolder.HasValue ||
        SetTimeStampToNow.HasValue || ContinueOnHashMismatch.HasValue ||
        DeleteMode.HasValue || DeleteEncryptedFileMode.HasValue ||
        DeleteRandomTimes.HasValue || DeleteZeroTimes.HasValue || ConfirmDelete.HasValue ||
        AutoNameFormat != null || CamouflageExtension != null ||
        !string.IsNullOrEmpty(EncryptOutputDirectory) ||
        !string.IsNullOrEmpty(DecryptOutputDirectory) ||
        !string.IsNullOrEmpty(ZipOutputDirectory);

    /// <summary>
    /// 保存されている設定の複製に、コマンドラインで指定された項目だけを上書きする。
    /// 指定が無かった項目は設定の値がそのまま残る。
    /// </summary>
    public AppSettings ApplyTo(AppSettings settings)
    {
        if (ExitAfterProcess.HasValue) settings.ExitAfterProcess = ExitAfterProcess.Value;
        if (OpenDecryptedFile.HasValue) settings.OpenDecryptedFile = OpenDecryptedFile.Value;
        if (NotMaskPassword.HasValue) settings.NotMaskPassword = NotMaskPassword.Value;

        if (ShowMenuBarIcon.HasValue) settings.ShowMenuBarIcon = ShowMenuBarIcon.Value;
        if (WindowForeground.HasValue) settings.WindowForeground = WindowForeground.Value;
        if (NoMultipleInstance.HasValue) settings.NoMultipleInstance = NoMultipleInstance.Value;

        if (AllowPasswordFileDragDrop.HasValue)
            settings.AllowPasswordFileDragDrop = AllowPasswordFileDragDrop.Value;
        if (NoErrorWhenPasswordFileMissing.HasValue)
            settings.NoErrorWhenPasswordFileMissing = NoErrorWhenPasswordFileMissing.Value;
        if (BreakFileWhenLimitExceeded.HasValue)
            settings.BreakFileWhenLimitExceeded = BreakFileWhenLimitExceeded.Value;

        if (EncryptionType.HasValue) settings.EncryptionFileType = (int)EncryptionType.Value;

        // 自己実行形式は「種類」の一つ（EncryptionFileTypes.Exe）として設定に載せる。
        // /entype=2 は種類 ATC + この指定の組で来るので、こちらを後に評価する。
        // /exeout=0 は、設定で EXE が選ばれていても通常の .atc に戻す
        if (SelfExecutableOption == true)
        {
            settings.EncryptionFileType = EncryptionFileTypes.Exe;
        }
        else if (SelfExecutableOption == false && settings.EncryptionFileType == EncryptionFileTypes.Exe)
        {
            settings.EncryptionFileType = EncryptionFileTypes.Atc;
        }

        if (!string.IsNullOrEmpty(EncryptOutputDirectory))
        {
            settings.SaveToSameFolder = true;
            settings.SaveToFolderPath = EncryptOutputDirectory;
        }

        if (OverwriteWithoutConfirm.HasValue) settings.ConfirmOverwrite = !OverwriteWithoutConfirm.Value;
        if (OneByOne.HasValue) settings.EncryptOneByOne = OneByOne.Value;
        if (KeepTimeStamp.HasValue) settings.KeepTimeStamp = KeepTimeStamp.Value;
        if (IncludeOriginalExtension.HasValue) settings.IncludeOriginalExtension = IncludeOriginalExtension.Value;

        if (AutoNameFormat != null)
        {
            settings.UseAutoName = AutoNameFormat.Length > 0;
            if (AutoNameFormat.Length > 0) settings.AutoNameFormat = AutoNameFormat;
        }

        if (CamouflageExtension != null)
        {
            settings.UseCamouflageExtension = CamouflageExtension.Length > 0;
            if (CamouflageExtension.Length > 0) settings.CamouflageExtension = CamouflageExtension;
        }

        if (Compression.HasValue)
        {
            settings.CompressionLevel = Compression.Value switch
            {
                System.IO.Compression.CompressionLevel.NoCompression => 0,
                System.IO.Compression.CompressionLevel.Optimal => 2,
                _ => 1,
            };
        }

        if (MissTypeLimits.HasValue) settings.MissTypeLimits = MissTypeLimits.Value;

        if (ZipAlgorithm.HasValue) settings.ZipAlgorithm = (int)ZipAlgorithm.Value switch
        {
            128 => 1,
            256 => 2,
            _ => 0,
        };

        if (!string.IsNullOrEmpty(ZipOutputDirectory))
        {
            settings.ZipSaveToSameFolder = true;
            settings.ZipSaveToFolderPath = ZipOutputDirectory;
        }

        if (!string.IsNullOrEmpty(DecryptOutputDirectory))
        {
            settings.DecryptSaveToSameFolder = true;
            settings.DecryptSaveToFolderPath = DecryptOutputDirectory;
        }

        if (NoParentFolder.HasValue) settings.NoParentFolder = NoParentFolder.Value;
        if (SetTimeStampToNow.HasValue) settings.SetTimeStampToNow = SetTimeStampToNow.Value;
        if (ContinueOnHashMismatch.HasValue) settings.ContinueOnHashMismatch = ContinueOnHashMismatch.Value;

        if (DeleteMode.HasValue) settings.DeleteMode = DeleteMode.Value;
        if (DeleteEncryptedFileMode.HasValue) settings.DeleteEncryptedFileMode = DeleteEncryptedFileMode.Value;
        if (DeleteRandomTimes.HasValue) settings.DeleteRandomTimes = DeleteRandomTimes.Value;
        if (DeleteZeroTimes.HasValue) settings.DeleteZeroTimes = DeleteZeroTimes.Value;
        if (ConfirmDelete.HasValue) settings.ConfirmDelete = ConfirmDelete.Value;

        return settings;
    }

    /// <summary>
    /// 明示されたオプションのうち、アプリのウィンドウにしか意味が無いもの
    /// （端末専用の AttacheCase.Cli では効果が無い）。注意書きの表示用。
    /// </summary>
    public IEnumerable<string> GuiOnlyOptionsGiven()
    {
        if (ExitAfterProcess.HasValue) yield return "/exit";
        if (OpenDecryptedFile.HasValue) yield return "/opf";
        if (NotMaskPassword.HasValue) yield return "/nohide";
        if (ShowMenuBarIcon.HasValue) yield return "/tsktr";
        if (WindowForeground.HasValue) yield return "/front";
        if (NoMultipleInstance.HasValue) yield return "/nomulti";
        if (AllowPasswordFileDragDrop.HasValue) yield return "/pf";
        if (ConfirmDelete.HasValue) yield return "/comfdel";
    }

    /// <summary>
    /// コマンドラインで何らかの指定があったか。
    /// false なら通常どおり GUI をそのまま表示するだけでよい。
    /// </summary>
    public bool HasAnyInstruction =>
        HasSettingOverride ||
        InputPaths.Count > 0 ||
        Action is CliAction.Encrypt or CliAction.Decrypt ||
        Password != null ||
        EncryptPasswordFilePath != null ||
        DecryptPasswordFilePath != null ||
        RsaPublicKeyFilePath != null ||
        RsaPrivateKeyFilePath != null ||
        PostPpapRecipientEmail != null ||
        UsePostPpapAccounts ||
        GenerateRsaKeyOutputDir != null;
}

/// <summary>
/// コマンドライン引数のパーサー。
/// </summary>
public static class CliParser
{
    /// <summary>
    /// Windows 版 GUI 専用で、macOS 版では意味を持たないオプション。
    /// 既存のバッチ資産をそのまま流用できるよう、受理して無視する。
    /// </summary>
    private static readonly HashSet<string> IgnorableGuiOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // ダイアログ・ウィンドウ・外観の設定（macOS 版には設定保存の仕組みがまだ無い）
        "/exe", "/decnum", "/askende", "/chkexeout", "/theme",
        "/mempexe", "/psm", "/imeon", "/notmaximized",
        "/chkdel", "/sametype", "/beforetype",
        // Mark of the Web は Windows 固有（macOS の quarantine 属性とは仕様が異なる）
        "/motwexe", "/motwoffice", "/motwuser", "/motwall", "/motwtypes",
        // ver.5 で廃止済み
        "/wmin", "/tskb",
    };

    /// <summary>
    /// macOS 版では対応できないオプション。
    /// 黙って無視すると「効いているつもり」で使われるため、理由を示してエラーにする。
    /// </summary>
    private static readonly Dictionary<string, string> UnsupportedOptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            {
                "/slvgfolder",
                "サルベージモードは macOS 版では未実装です。 / Salvage mode is not implemented on macOS yet."
            },
            {
                "/slvgsame",
                "サルベージモードは macOS 版では未実装です。 / Salvage mode is not implemented on macOS yet."
            },
        };

    // TODO(Phase 6): Windows では Exeout スタブの同梱ファイル方式で /exeout を有効にする
    private const string SelfExecutableNotAvailable =
        "自己実行形式（.exe）の出力は、このビルドではまだ利用できません（Windows 専用機能）。 / " +
        "The self-executable (.exe) output is not available in this build yet (Windows-only feature).";

    public static CliOptions Parse(string[]? args)
    {
        var options = new CliOptions();

        if (args == null || args.Length == 0)
        {
            return options;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim('\t', ',', ' ');
            if (arg.Length == 0) continue;

            //-----------------------------------
            // ヘルプ・バージョン
            //-----------------------------------
            if (arg == "/?" || arg == "-h" || Match(arg, "/help") || Match(arg, "--help"))
            {
                options.Action = CliAction.ShowHelp;
                return options;
            }

            if (Match(arg, "/version") || Match(arg, "--version"))
            {
                options.Action = CliAction.ShowVersion;
                return options;
            }

            //-----------------------------------
            // オプションでなければ処理対象のパスとして扱う
            //-----------------------------------
            if (!LooksLikeOption(arg))
            {
                var pathError = AddInputPath(options, arg);
                if (pathError != null)
                {
                    options.ParseError = pathError;
                    options.ParseErrorCode = AtcExitCode.FileNotFound;
                    return options;
                }
                continue;
            }

            //-----------------------------------
            // key=value へ分解
            //-----------------------------------
            string key;
            string? value;
            var eq = arg.IndexOf('=');
            if (eq >= 0)
            {
                key = arg[..eq].ToLowerInvariant();
                value = arg[(eq + 1)..];
            }
            else
            {
                key = arg.ToLowerInvariant();
                value = null;
            }

            // "--name" は "/name" と同義に扱う
            if (key.StartsWith("--", StringComparison.Ordinal))
            {
                key = "/" + key[2..];
            }

            // 値を次の引数から取る形式（--out PATH）に対応
            var index = i;
            string TakeValue()
            {
                if (value != null) return value;
                if (index + 1 < args.Length && !LooksLikeOption(args[index + 1]))
                {
                    index++;
                    return args[index];
                }
                return "";
            }

            switch (key)
            {
                //-----------------------------------
                // 処理の種類
                //-----------------------------------
                case "/en":
                case "/encrypt":
                    if (value is null or "1") options.Action = CliAction.Encrypt;
                    break;

                case "/de":
                case "/decrypt":
                    if (value is null or "1") options.Action = CliAction.Decrypt;
                    break;

                //-----------------------------------
                // パスワード
                //-----------------------------------
                case "/p":
                case "/password":
                    options.Password = TakeValue();
                    break;

                case "/pfile":
                    options.EncryptPasswordFilePath = ToFullPath(TakeValue());
                    break;

                case "/dpfile":
                    options.DecryptPasswordFilePath = ToFullPath(TakeValue());
                    break;

                //-----------------------------------
                // 公開鍵方式
                //-----------------------------------
                case "/pubkey":
                    {
                        var path = ExpandHome(TakeValue());
                        if (!File.Exists(path))
                        {
                            options.ParseError = "公開鍵ファイルが見つかりません / Public key file not found: " + path;
                            options.ParseErrorCode = AtcExitCode.FileNotFound;
                            return options;
                        }
                        options.RsaPublicKeyFilePath = Path.GetFullPath(path);
                    }
                    break;

                case "/privkey":
                    {
                        var path = ExpandHome(TakeValue());
                        if (!File.Exists(path))
                        {
                            options.ParseError = "秘密鍵ファイルが見つかりません / Private key file not found: " + path;
                            options.ParseErrorCode = AtcExitCode.FileNotFound;
                            return options;
                        }
                        options.RsaPrivateKeyFilePath = Path.GetFullPath(path);
                    }
                    break;

                // Post-PPAP
                //   値あり（メールアドレス）: その宛先の公開鍵をキーサーバから検索して暗号化
                //   値なし                  : この Mac の登録済みアカウントで復号する
                case "/p-ppap":
                    {
                        var email = (value ?? "").Trim();
                        if (email.Length > 0)
                        {
                            options.PostPpapRecipientEmail = email;
                        }
                        else
                        {
                            options.UsePostPpapAccounts = true;
                        }
                    }
                    break;

                // RSA 鍵ペアの生成
                case "/genrsa":
                    options.GenerateRsaKeyOutputDir = ToFullPath(TakeValue());
                    break;

                //-----------------------------------
                // 出力先
                //-----------------------------------
                case "/out":
                case "/o":
                    options.OutputPath = ToFullPath(TakeValue());
                    break;

                case "/saveto":
                    options.EncryptOutputDirectory = ToFullPath(TakeValue());
                    break;

                case "/dsaveto":
                    options.DecryptOutputDirectory = ToFullPath(TakeValue());
                    break;

                case "/zipsaveto":
                    options.ZipOutputDirectory = ToFullPath(TakeValue());
                    break;

                //-----------------------------------
                // 上書き
                //   GUI と同じ「上書きを確認するか」の意味づけ:
                //     /ow=1（既定） → 確認ダイアログを出す
                //     /ow=0         → 確認せずに上書きする
                //-----------------------------------
                case "/ow":
                    options.OverwriteWithoutConfirm = value == "0";
                    break;

                // 復号したファイルを関連付けられたアプリで開く
                case "/opf":
                    options.OpenDecryptedFile = value is null or "1";
                    break;

                // パスワードを隠さずに入力する
                case "/nohide":
                    options.NotMaskPassword = value is null or "1";
                    break;

                // メニューバーにアイコンを表示する（Windows のタスクトレイ設定に相当）
                case "/tsktr":
                    options.ShowMenuBarIcon = value is null or "1";
                    break;

                // ウィンドウを最前面に表示する
                case "/front":
                    options.WindowForeground = value is null or "1";
                    break;

                // パスワードファイルのドラッグ＆ドロップを許可する
                case "/pf":
                    options.AllowPasswordFileDragDrop = value is null or "1";
                    break;

                // 入力制限を超えたら暗号化ファイルを破壊する
                case "/breakfile":
                    options.BreakFileWhenLimitExceeded = value is null or "1";
                    break;

                // パスワードファイルが無くてもエラーを出さない
                case "/nomsgp":
                    options.NoErrorWhenPasswordFileMissing = value is null or "1";
                    break;

                // 複数起動しない
                case "/nomulti":
                    options.NoMultipleInstance = value is null or "1";
                    break;

                // 削除の前に確認する
                case "/comfdel":
                    options.ConfirmDelete = value is null or "1";
                    break;

                case "/y":
                case "/force":
                    options.OverwriteWithoutConfirm = true;
                    break;

                //-----------------------------------
                // 暗号化ファイルの種類
                //   1: ATC / 2: 自己実行形式（Windows のみ） / 3: パスワード ZIP
                //-----------------------------------
                case "/entype":
                    switch (value)
                    {
                        case "1":
                            options.EncryptionType = CliEncryptionType.Atc;
                            break;
                        case "2":
                            if (!OperatingSystem.IsWindows())
                            {
                                options.ParseError = "/entype=2 " + SelfExecutableNotAvailable;
                                return options;
                            }
                            options.EncryptionType = CliEncryptionType.Atc;
                            options.SelfExecutableOption = true;
                            break;
                        case "3":
                            options.EncryptionType = CliEncryptionType.Zip;
                            break;
                        default:
                            // 知らない値を黙って既定へ落とさない。指定を取り違えたまま
                            // 意図と違う形式で暗号化されるのを防ぐ
                            options.ParseError =
                                $"/entype の値が不正です / Invalid value for /entype: {value}";
                            return options;
                    }
                    break;

                // パスワード ZIP の暗号方式（0: ZipCrypto / 1: AES-128 / 2: AES-256）
                case "/zipalgo":
                    switch (value)
                    {
                        case "0":
                            options.ZipAlgorithm = ZipPasswordEncryptionMethod.ZipCrypto;
                            break;
                        case "1":
                            options.ZipAlgorithm = ZipPasswordEncryptionMethod.Aes128;
                            break;
                        case "2":
                            options.ZipAlgorithm = ZipPasswordEncryptionMethod.Aes256;
                            break;
                        default:
                            // 知らない値が一番弱い ZipCrypto に落ちていた
                            options.ParseError =
                                $"/zipalgo の値が不正です / Invalid value for /zipalgo: {value}";
                            return options;
                    }
                    break;

                //-----------------------------------
                // エクスプローラーの関連付け経由で起動したことを示すフラグ。
                // インストーラが書く shell\open\command に含まれる。動作は変えない
                //-----------------------------------
                case "/fileassociation":
                    break;

                //-----------------------------------
                // 商用ライセンス（/lc=ファイル で登録、/lc= で削除）
                //-----------------------------------
                case "/lc":
                    options.LicenseFile = value ?? "";
                    break;

                case "/exeout":
                    // /exeout=0 は「自己実行形式にしない」なので、そのまま受理してよい
                    if (value is null or "1")
                    {
                        if (!OperatingSystem.IsWindows())
                        {
                            options.ParseError = "/exeout " + SelfExecutableNotAvailable;
                            return options;
                        }
                        options.SelfExecutableOption = true;
                    }
                    else
                    {
                        options.SelfExecutableOption = false;
                    }
                    break;

                //-----------------------------------
                // ファイル名の自動生成
                //   書式指定子: <filename> <ext> <date:書式> <num:桁数> <random:文字数>
                //-----------------------------------
                case "/autoname":
                    options.AutoNameFormat = TakeValue();
                    break;

                //-----------------------------------
                // 拡張子の偽装
                //-----------------------------------
                case "/camoext":
                    {
                        var ext = TakeValue();
                        if (AutoFileName.ContainsInvalidFileNameChar(ext))
                        {
                            options.ParseError =
                                "拡張子にファイル名として使えない文字が含まれています / " +
                                "The extension contains a character that cannot be used in a file name: " + ext;
                            return options;
                        }
                        if (!string.IsNullOrEmpty(ext) && !ext.StartsWith('.'))
                        {
                            ext = "." + ext;
                        }
                        options.CamouflageExtension = ext;
                    }
                    break;

                // 暗号化ファイル名に元ファイルの拡張子を含める（report.xlsx → report.xlsx.atc）
                case "/withext":
                    options.IncludeOriginalExtension = value is null or "1";
                    break;

                //-----------------------------------
                // まとめ方
                //-----------------------------------
                case "/oneby":
                    options.OneByOne = value is null or "1";
                    break;

                case "/allpack":
                    if (value is null or "1") options.OneByOne = false;
                    break;

                //-----------------------------------
                // 圧縮
                //-----------------------------------
                // 圧縮サイズ（0: 無圧縮 / 1: 速度優先 / 2: サイズ優先）
                case "/complv":
                    options.Compression = TakeValue() switch
                    {
                        "0" => CompressionLevel.NoCompression,
                        "1" => CompressionLevel.Fastest,
                        _ => CompressionLevel.Optimal,
                    };
                    break;

                // 圧縮率（0〜9）。Windows 版 GUI と同じ写像で圧縮レベルへ変換する
                //   0 → 無圧縮 / 1〜5 → 速度優先 / 6 以上 → サイズ優先
                case "/comprate":
                    if (int.TryParse(TakeValue(), out var compressRate))
                    {
                        options.Compression = compressRate switch
                        {
                            0 => CompressionLevel.NoCompression,
                            >= 1 and <= 5 => CompressionLevel.Fastest,
                            _ => CompressionLevel.Optimal,
                        };
                    }
                    break;

                //-----------------------------------
                // タイムスタンプ
                //-----------------------------------
                case "/orgdt":
                    options.KeepTimeStamp = value is null or "1";
                    break;

                case "/now":
                    options.SetTimeStampToNow = value is null or "1";
                    break;

                case "/nopfldr":
                    options.NoParentFolder = value is null or "1";
                    break;

                //-----------------------------------
                // 元ファイルの削除
                //   1: 通常削除 / 2: ごみ箱へ移動 / 3: 完全削除（ランダム値とゼロで上書き）
                //-----------------------------------
                case "/del":
                    options.DeleteMode = ParseDeleteMode(value);
                    break;

                case "/delenc":
                    options.DeleteEncryptedFileMode = ParseDeleteMode(value);
                    break;

                case "/delrand":
                    if (int.TryParse(TakeValue(), out var randTimes) && randTimes >= 0)
                    {
                        options.DeleteRandomTimes = randTimes;
                    }
                    break;

                case "/delnull":
                    if (int.TryParse(TakeValue(), out var zeroTimes) && zeroTimes >= 0)
                    {
                        options.DeleteZeroTimes = zeroTimes;
                    }
                    break;

                //-----------------------------------
                // その他
                //-----------------------------------
                case "/typelimit":
                    if (int.TryParse(TakeValue(), out var limit) && limit > 0)
                    {
                        options.MissTypeLimits = limit;
                    }
                    break;

                case "/ignorecrc":
                    options.ContinueOnHashMismatch = value is null or "1";
                    break;

                // 処理後にアプリを終了する
                case "/exit":
                    options.ExitAfterProcess = value is null or "1";
                    break;

                case "/q":
                case "/quiet":
                    options.Quiet = true;
                    break;

                case "/list":
                    {
                        var listError = LoadPathList(options, ExpandHome(TakeValue()));
                        if (listError != null)
                        {
                            // 終了コードは UsageError(2) のまま変えない。旧版がこの挙動で、
                            // 回帰ハーネス（F-04）が凍結契約として検証している
                            options.ParseError = listError;
                            return options;
                        }
                    }
                    break;

                case "/4gbok":
                    // 4GB 超えは macOS 版では常に許容（ストリーミング処理のため制限が無い）。
                    // 互換のためオプションだけ受理する。
                    break;

                default:
                    if (IgnorableGuiOptions.Contains(key))
                    {
                        options.IgnoredOptions.Add(arg);
                        break;
                    }

                    if (UnsupportedOptions.TryGetValue(key, out var reason))
                    {
                        options.ParseError = key + " は使用できません。" + reason;
                        return options;
                    }

                    options.ParseError = "不明なオプション / Unknown option: " + arg;
                    return options;
            }

            i = index;
        }

        return options;
    }

    private static int ParseDeleteMode(string? value)
    {
        // 値なしは Windows 版と同じく「完全削除」
        var mode = value == null ? 3 : int.TryParse(value, out var m) ? m : 0;
        return mode is 1 or 2 or 3 ? mode : 0;
    }

    private static bool Match(string arg, string name)
    {
        return string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToFullPath(string path)
    {
        return string.IsNullOrEmpty(path) ? path : Path.GetFullPath(ExpandHome(path));
    }

    /// <summary>
    /// 先頭の "~" をホームディレクトリに展開する。
    /// シェルは "/saveto=~/Backup" のように "=" の後ろに来たチルダを展開しないので、
    /// ここで面倒を見ないと、カレント直下の "~" という名前のフォルダーを指してしまう。
    /// </summary>
    private static string ExpandHome(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '~') return path;
        if (path.Length > 1 && path[1] != '/') return path; // "~user/..." は扱わない

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return path;

        return path.Length == 1 ? home : Path.Combine(home, path[2..]);
    }

    /// <summary>
    /// 引数をオプションとみなすか、処理対象のパスとみなすかを判定する。
    /// </summary>
    /// <remarks>
    /// macOS の絶対パスは「/」で始まるため、Windows 版のように
    /// 「先頭が /」だけでオプションと決めつけられない。
    /// 実在するパス、または区切りを 2 つ以上含むもの（/Users/foo/bar）は
    /// パスとして扱い、/name または /name=value の形だけをオプションとみなす。
    /// </remarks>
    private static bool LooksLikeOption(string arg)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal)) return true;
        if (!arg.StartsWith('/')) return false;
        if (File.Exists(arg) || Directory.Exists(arg)) return false;

        var body = arg[1..];
        var eq = body.IndexOf('=');
        var name = eq >= 0 ? body[..eq] : body;
        return !name.Contains('/');
    }

    /// <summary>
    /// 処理対象のパスを追加する。見つからない場合はその旨を返す。
    /// </summary>
    private static string? AddInputPath(CliOptions options, string path)
    {
        var fullPath = Path.GetFullPath(ExpandHome(path));

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return "指定されたファイルまたはフォルダーが見つかりません / " +
                   "The specified file or folder is not found: " + path;
        }

        options.InputPaths.Add(fullPath);
        return null;
    }

    /// <summary>
    /// テキストファイルから処理対象のパス一覧を読み込む（/list）。
    /// 相対パスはリストファイルのあるディレクトリを基準とする。
    /// </summary>
    private static string? LoadPathList(CliOptions options, string listFilePath)
    {
        if (string.IsNullOrEmpty(listFilePath) || !File.Exists(listFilePath))
        {
            return "リストファイルが見つかりません / List file not found: " + listFilePath;
        }

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(listFilePath)) ?? "";

        foreach (var rawLine in File.ReadAllLines(listFilePath, DetectEncoding(listFilePath)))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var expanded = ExpandHome(line);
            var fullPath = Path.IsPathRooted(expanded)
                ? expanded
                : Path.GetFullPath(Path.Combine(baseDir, expanded));
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                return "リストファイル内のパスが見つかりません / The path in the list file is not found: " + fullPath;
            }

            options.InputPaths.Add(fullPath);
        }

        return null;
    }

    /// <summary>
    /// BOM を見てエンコードを判定する。BOM が無い場合は UTF-8 として扱う。
    /// </summary>
    private static Encoding DetectEncoding(string filePath)
    {
        var head = new byte[4];
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            var read = fs.Read(head, 0, 4);
            if (read >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) return new UTF8Encoding(true);
            if (read >= 2 && head[0] == 0xFF && head[1] == 0xFE) return Encoding.Unicode;
            if (read >= 2 && head[0] == 0xFE && head[1] == 0xFF) return Encoding.BigEndianUnicode;
        }
        return new UTF8Encoding(false);
    }
}
