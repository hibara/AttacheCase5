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
// --help / --version の表示内容。
//----------------------------------------------------------------------

using System;
using System.Reflection;
using AttacheCase.Services;

namespace AttacheCase.Cli;

public static class CliHelpText
{
    // 設定「表示言語」に従う（システムが日本語でも英語表示を選べる）
    private static bool IsJapanese => LocalizationService.Instance.IsJapanese;

    public static string Version
    {
        get
        {
            return AttacheCase.Services.AppVersion.Display;
        }
    }

    public static void WriteVersion()
    {
        Console.WriteLine($"AttacheCase {Version}");
    }

    public static void WriteHelp()
    {
        Console.WriteLine(IsJapanese ? JapaneseHelp : EnglishHelp);
    }

    private const string JapaneseHelp = """
        アタッシェケース5 — ファイル暗号化ソフト

        書式:
          AttacheCase [オプション] <ファイルまたはフォルダー> [...]

        オプションは /name=value / --name=value / --name value のいずれでも書けます。
        「/」で始まらない引数は、すべて処理対象のパスとして扱われます。
        AttacheCase.Cli は端末内で完結します（GUI 本体に引数を渡した場合は、
        処理はアプリのウィンドウ上で進行します）。

        引数は、設定画面（左下の歯車）で保存した設定を上書きします（承認済みの持ち運び設定
        _AtcCase.json があればそちらが元になります）。AttacheCase.Cli も同じ設定を読むので、
        省略した項目は設定画面の内容がそのまま既定になります（保存先・自動命名・削除など）。
        上書きはその起動のあいだだけで、設定ファイルには書き戻されません。
        端末では確認ダイアログを出せないため、「上書きを確認する」なら上書きせずに終了
        コード 5、「削除の前に確認する」なら確認なしで削除します。

        処理の種類
          /en, --encrypt          暗号化する
          /de, --decrypt          復号する
                                  どちらも省略した場合はファイルの内容から自動判定

        パスワード
          /p=文字列               パスワード
          /pfile=パス             暗号化に使うパスワードファイル
          /dpfile=パス            復号に使うパスワードファイル
          /pf=0|1                 パスワードファイルのドラッグ＆ドロップを許可する
          /nomsgp=0|1             パスワードファイルが無くてもエラーを出さない

        公開鍵方式
          /pubkey=パス            RSA 公開鍵ファイル（.atcpub）で暗号化
          /privkey=パス           RSA 秘密鍵ファイル（.atcpvt）で復号
          /p-ppap=メールアドレス  Post-PPAP。キーサーバで宛先の公開鍵を検索して暗号化
          /p-ppap                 この PC に登録済みの Post-PPAP アカウントで復号
                                  （値の有無で意味が変わるため「=」区切りのみ。
                                    「--p-ppap メールアドレス」とは書けません）
          /genrsa=ディレクトリ    RSA 鍵ペアを生成して保存する

        出力先
          /out=パス               出力先（暗号化: ファイルまたはフォルダー／復号: 展開先）
          /saveto=フォルダー      暗号化ファイルの保存先
          /dsaveto=フォルダー     復号したファイルの展開先
          /zipsaveto=フォルダー   パスワード ZIP の保存先
          /ow=0|1                 同名ファイルの上書き確認（0=確認しない、既定は 1）
          /y, /force              /ow=0 と同じ

        出力ファイル名
          /entype=1|3             1=暗号化ファイル（.atc）、3=パスワード ZIP
          /zipalgo=0|1|2          ZIP の暗号方式（0=ZipCrypto、1=AES-128、2=AES-256）
          /withext=0|1            元の拡張子を残す（report.xlsx → report.xlsx.atc）
          /camoext=.jpg           暗号化ファイルの拡張子を偽装する
          /autoname=書式          書式からファイル名を組み立てる
                                  <filename> <ext> <date:yyyyMMdd> <num:3> <random:8>

        まとめ方・圧縮
          /allpack=1              指定されたすべてを 1 つにまとめる（既定）
          /oneby=0|1              フォルダー内のファイルを 1 つずつ個別に暗号化する
          /complv=0|1|2           0=無圧縮、1=速度優先、2=サイズ優先
          /comprate=0〜9          圧縮率（0=無圧縮、1〜5=速度優先、6 以上=サイズ優先）
          /orgdt=0|1              暗号化ファイルのタイムスタンプを元ファイルに合わせる
          /now=0|1                復号したファイルのタイムスタンプを復号日時にする
          /nopfldr=0|1            復号時に親フォルダーを作らない

        一般（設定画面と同じ項目）
          /opf=0|1                復号したファイルを関連付けられたアプリで開く
          /nohide=0|1             パスワードを隠さずに入力する

        ウィンドウ
          /tsktr=0|1              メニューバーにアイコンを表示する
          /front=0|1              ウィンドウを最前面に表示する
          /nomulti=0|1            アタッシェケースを複数起動しない

        削除
          /del=1|2|3              暗号化成功後に元ファイルを削除
                                  1=通常削除、2=ごみ箱へ移動、3=完全削除
          /delenc=1|2|3           復号成功後に暗号化ファイルを削除
          /delrand=回数           完全削除でランダム値を書き込む回数（既定 1）
          /delnull=回数           完全削除でゼロを書き込む回数（既定 1）
          /comfdel=0|1            削除の前に確認する

        その他
          /exit=0|1               処理が終わったらアプリを終了する
          /typelimit=1〜10        暗号化ファイルに記録するパスワード入力回数の上限
          /breakfile=0|1          その回数を間違えたら暗号化ファイルを破壊する
          /list=パス              処理対象のパス一覧をテキストファイルから読み込む
          /ignorecrc              CRC32 が一致しなくても復号を続行する
          /q, /quiet              注意書きを表示しない
          --help, /?, -h          このヘルプを表示
          --version               バージョンを表示

        商用ライセンス
          /lc=パス                レジストレーションコードのファイルを読み込んで登録
          /lc=                    登録済みの商用ライセンスを削除

        このビルドで使えないもの
          /exeout, /entype=2      自己実行形式（.exe）の出力は Windows 専用です
          /slvgfolder, /slvgsame  サルベージモードは未実装です

        Windows 版 GUI 専用の設定オプション（/theme /askende /imeon /psm など）は、
        既存のバッチをそのまま流用できるよう受理しますが、効果はありません。
        画面にしか意味の無い項目（/exit /opf /nohide /tsktr /front /nomulti /pf /comfdel）は、
        AttacheCase.Cli では受理するだけで効果がありません。

        終了コード (EXIT CODES):
          0   正常終了
          1   一般的なエラー
          2   コマンドラインの指定が不正
          3   入力ファイル／フォルダーが見つからない
          4   パスワードが違う、または復号に失敗
          5   出力先に同名ファイルが存在（上書き不許可）
          6   ディスクの空き容量が不足
          7   アクセスが拒否された
          8   暗号化ファイルが破損している
          9   対応していないファイル形式・データバージョン
          10  利用者による中断
          11  想定外の内部エラー
          12  パスワードファイルはこの出力形式では使えない

        使用例:
          AttacheCase /en /p=secret ~/Documents/report.xlsx
          AttacheCase /en /p=secret /saveto=~/Backup /autoname=<filename>_<date:yyyyMMdd><ext> ~/data
          AttacheCase /de /p=secret /nopfldr=1 /dsaveto=~/Restore ~/Backup/daily.atc
          AttacheCase /en /p-ppap=someone@example.com ~/Documents/report.xlsx
          AttacheCase /en /p=secret /exit=1 /q ~/data      # スクリプト向け（終わったら終了）
        """;

    private const string EnglishHelp = """
        AttacheCase 5 — File encryption software

        Usage:
          AttacheCase [options] <file or folder> [...]

        Options accept /name=value, --name=value or --name value.
        Any argument that does not start with "/" is treated as a target path.
        AttacheCase.Cli runs entirely in the terminal (when arguments are passed
        to the GUI app instead, the work is carried out in the app window).

        Options override the settings saved in the Settings screen (gear, bottom left;
        an approved portable settings file _AtcCase.json takes its place when present).
        AttacheCase.Cli reads the same settings, so anything you leave out defaults to
        what the Settings screen says (save-to folder, auto naming, deletion, ...).
        The override lasts for that run only and is never written back to the settings file.
        The terminal cannot show confirmation dialogs: "confirm overwriting" therefore
        means "do not overwrite" (exit code 5), and "confirm before deleting" deletes
        without asking.

        Action
          /en, --encrypt          Encrypt
          /de, --decrypt          Decrypt
                                  If neither is given, the file content decides

        Password
          /p=STRING               Password
          /pfile=PATH             Password file used for encryption
          /dpfile=PATH            Password file used for decryption
          /pf=0|1                 Allow dragging and dropping a password file
          /nomsgp=0|1             Do not report a missing password file

        Public key
          /pubkey=PATH            Encrypt with an RSA public key file (.atcpub)
          /privkey=PATH           Decrypt with an RSA private key file (.atcpvt)
          /p-ppap=EMAIL           Post-PPAP. Look up the recipient key on the key server
          /p-ppap                 Decrypt with the Post-PPAP accounts on this computer
                                  (Use "=" only. Since the meaning changes with and
                                   without a value, "--p-ppap EMAIL" is not accepted.)
          /genrsa=DIR             Generate an RSA key pair into DIR

        Output
          /out=PATH               Output path (encrypt: file or folder / decrypt: folder)
          /saveto=DIR             Folder for encrypted files
          /dsaveto=DIR            Folder for decrypted files
          /zipsaveto=DIR          Folder for password ZIP files
          /ow=0|1                 Confirm overwriting (0 = do not confirm, default 1)
          /y, /force              Same as /ow=0

        Output file name
          /entype=1|3             1 = encrypted file (.atc), 3 = password ZIP
          /zipalgo=0|1|2          ZIP cipher (0 = ZipCrypto, 1 = AES-128, 2 = AES-256)
          /withext=0|1            Keep the original extension (report.xlsx.atc)
          /camoext=.jpg           Camouflage the extension of the encrypted file
          /autoname=FORMAT        Build the file name from a format string
                                  <filename> <ext> <date:yyyyMMdd> <num:3> <random:8>

        Packing and compression
          /allpack=1              Pack everything into one encrypted file (default)
          /oneby=0|1              Encrypt each file in a folder separately
          /complv=0|1|2           0 = none, 1 = fastest, 2 = optimal
          /comprate=0-9           Compression rate (0 none, 1-5 fastest, 6+ optimal)
          /orgdt=0|1              Keep the timestamp of the original file
          /now=0|1                Set decrypted timestamps to the current time
          /nopfldr=0|1            Do not create a parent folder when decrypting

        General (same items as the Settings screen)
          /opf=0|1                Open decrypted files with the associated application
          /nohide=0|1             Show the password instead of masking it

        Window
          /tsktr=0|1              Show an icon in the menu bar
          /front=0|1              Keep the window in front of other windows
          /nomulti=0|1            Do not start more than one AttacheCase

        Deletion
          /del=1|2|3              Delete originals after a successful encryption
                                  1 = delete, 2 = move to Trash, 3 = wipe
          /delenc=1|2|3           Delete the encrypted file after a successful decryption
          /delrand=N              Random-value passes when wiping (default 1)
          /delnull=N              Zero passes when wiping (default 1)
          /comfdel=0|1            Confirm before deleting

        Others
          /exit=0|1               Quit the app when the work is finished
          /typelimit=1-10         Password retry limit recorded in the encrypted file
          /breakfile=0|1          Destroy the encrypted file after that many wrong attempts
          /list=PATH              Read the list of target paths from a text file
          /ignorecrc              Continue decrypting even if CRC32 does not match
          /q, /quiet              Do not print notices
          --help, /?, -h          Show this help
          --version               Show the version

        Commercial license
          /lc=PATH                Register the registration code read from the file
          /lc=                    Remove the registered commercial license

        Not available in this build
          /exeout, /entype=2      The self-executable (.exe) output is Windows-only
          /slvgfolder, /slvgsame  Salvage mode is not implemented

        GUI-only preference options (/theme /askende /imeon /psm) are accepted so
        that existing batch files keep working, but have no effect. Items that only
        matter in the app window (/exit /opf /nohide /tsktr /front /nomulti /pf /comfdel)
        are accepted by AttacheCase.Cli but have no effect there either.

        EXIT CODES:
          0   Success
          1   General error
          2   Invalid command line
          3   Input file or folder not found
          4   Wrong password, or decryption failed
          5   Output file already exists (and overwriting is not allowed)
          6   Not enough free disk space
          7   Access denied
          8   The encrypted file is broken
          9   Unsupported file format or data version
          10  Canceled
          11  Internal error
          12  A password file cannot be used with this output format

        Examples:
          AttacheCase /en /p=secret ~/Documents/report.xlsx
          AttacheCase /en /p=secret /saveto=~/Backup /autoname=<filename>_<date:yyyyMMdd><ext> ~/data
          AttacheCase /de /p=secret /nopfldr=1 /dsaveto=~/Restore ~/Backup/daily.atc
          AttacheCase /en /p-ppap=someone@example.com ~/Documents/report.xlsx
          AttacheCase /en /p=secret /exit=1 /q ~/data      # for scripts (quits when finished)
        """;
}
