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
// アプリケーション設定。
//
// Windows 版はレジストリ（および _AtcCase.ini）に保存するが、macOS 版は
// ~/Library/Application Support/AttacheCase/settings.json に保存する。
//
// 収録しているのは「コマンドライン引数で指定できる項目」に対応するもの。
// 起動時にコマンドライン引数が与えられた場合は、この設定の複製に対して
// 引数の内容を上書きし、その実行のあいだだけ使う（ファイルには書き戻さない）。
//----------------------------------------------------------------------

using System.IO.Compression;
using System.Text.Json.Serialization;

namespace AttacheCase.Models;

/// <summary>暗号化ファイルの種類</summary>
public static class EncryptionFileTypes
{
    /// <summary>通常の暗号化ファイル（.atc）</summary>
    public const int Atc = 1;

    /// <summary>自己実行形式（.exe）。Windows 専用（CLI の /entype=2 に対応）</summary>
    public const int Exe = 2;

    /// <summary>パスワード付き ZIP</summary>
    public const int Zip = 3;
}

/// <summary>元ファイルの削除方法</summary>
public static class DeleteModes
{
    public const int None = 0;
    public const int Delete = 1;
    public const int Trash = 2;
    public const int Wipe = 3;
}

public class AppSettings
{
    //-----------------------------------
    // メタ情報
    //-----------------------------------

    /// <summary>
    /// 設定スキーマの版数。将来、互換性のない変更をしたときに移行判断へ使う。
    /// </summary>
    public int SettingsVersion { get; set; } = 1;

    /// <summary>
    /// Windows のレジストリ（旧 AttacheCase5 の HKCU\Software\Hibara\AttacheCase5）
    /// から移行して作られた設定か。移行は settings.json が無い初回起動時に一度だけ
    /// 行われ、レジストリ側は消さない（旧版と併用しても壊れないようにするため）。
    /// </summary>
    public bool MigratedFromRegistry { get; set; }

    //-----------------------------------
    // 一般
    //-----------------------------------

    /// <summary>
    /// 表示言語。空文字はシステムの言語に従う。それ以外は "ja" / "en"。
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// テーマ。"auto"（OS の設定に従う）/ "light" / "dark"。
    /// 旧版レジストリの ThemeColorName と同じ語彙。
    /// </summary>
    public string Theme { get; set; } = "auto";

    /// <summary>処理後にアタッシェケースを終了する（/exit）</summary>
    public bool ExitAfterProcess { get; set; }

    /// <summary>復号したファイルを関連付けられたアプリで開く（/opf）</summary>
    public bool OpenDecryptedFile { get; set; }

    /// <summary>パスワードを隠さずに入力する（/nohide）</summary>
    public bool NotMaskPassword { get; set; }

    /// <summary>
    /// 起動時に更新を確認する。確認だけを行い、適用は利用者の同意を得てから。
    /// 既定で有効。セキュリティ上の修正を届けられないほうが危ないため。
    /// </summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    //-----------------------------------
    // ウィンドウ
    //-----------------------------------

    /// <summary>処理中は常にウィンドウを最小化する</summary>
    public bool MinimizeWhileProcessing { get; set; }

    /// <summary>メニューバー（macOS）/ タスクトレイ（Windows）にアイコンを表示する。旧 Windows 版の fTaskTrayIcon に相当</summary>
    public bool ShowMenuBarIcon { get; set; }

    /// <summary>ウィンドウを常に最前面に表示する</summary>
    public bool WindowForeground { get; set; }

    /// <summary>アタッシェケースを複数起動しない</summary>
    public bool NoMultipleInstance { get; set; }

    /// <summary>PC がタブレットモードで動作しているとき、最大化モードで起動しない（Windows のみ。旧版の fNotMaximizedInTabletMode / /notmaximized）</summary>
    public bool NoMaximizeInTabletMode { get; set; }

    //-----------------------------------
    // 保存設定（暗号化）
    //-----------------------------------

    /// <summary>暗号化ファイルの種類（/entype）。1: .atc、2: 自己実行形式（Windows）、3: パスワード ZIP</summary>
    public int EncryptionFileType { get; set; } = EncryptionFileTypes.Atc;

    /// <summary>
    /// 前に使ったファイルタイプで暗号化する（/beforetype、旧 fEncryptionSameFileTypeBefore）。
    /// ON: パスワード入力画面のコンボボックスで選んだ種類（ATC / EXE）を実行時に
    /// <see cref="EncryptionFileType"/> へ書き戻し、次回のドロップも同じ種類になる。
    /// OFF: 設定画面で選んだ種類を常に使う（コンボボックスの選択はその場限り）。
    /// 旧版と同じく、パスワード ZIP は「前回」として記憶しない（専用画面から明示的に選ぶもの）。
    /// </summary>
    public bool UseLastEncryptionFileType { get; set; }

    /// <summary>暗号化ファイルを常に同じ場所へ保存する（/saveto）</summary>
    public bool SaveToSameFolder { get; set; }

    public string SaveToFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// 暗号化時、同名ファイルがあるとき上書きの確認をする（/ow、旧 fEncryptConfirmOverwrite）。
    /// 復号側は別の設定 <see cref="DecryptConfirmOverwrite"/>（旧版と同じ 2 本立て。/ow は両方に効く）
    /// </summary>
    public bool ConfirmOverwrite { get; set; } = true;

    /// <summary>
    /// 複数ファイルの場合: フォルダー内のファイルを 1 つずつ個別に暗号化する（/oneby、旧 fFilesOneByOne）。
    /// <see cref="AllFilePack"/> と排他。両方 false が「通常」（入力 1 つにつき 1 つの暗号化ファイル。
    /// フォルダーはサブフォルダーも含めてフォルダー単位でまとめる）
    /// </summary>
    public bool EncryptOneByOne { get; set; }

    /// <summary>
    /// 複数ファイルの場合: 複数のファイルやフォルダーを 1 つの暗号化ファイルにまとめる（/allpack、旧 fAllFilePack）。
    /// <see cref="EncryptOneByOne"/> と排他（両方 true なら旧版と同じくこちらが勝つ）。
    /// コマンドラインから複数のファイルを渡して 1 つにまとめたいときに使う
    /// </summary>
    public bool AllFilePack { get; set; }

    /// <summary>
    /// ファイルとフォルダーのタイムスタンプを保持する（/orgdt、旧 fKeepTimeStamp）。
    /// 出力した暗号化ファイルの作成・更新日時を元ファイルに合わせる。既定 ON（利用者指示 2026-10-28）
    /// </summary>
    public bool KeepTimeStamp { get; set; } = true;

    /// <summary>暗号化ファイル名に元の拡張子を含める（/withext）</summary>
    public bool IncludeOriginalExtension { get; set; }

    /// <summary>暗号化ファイル名を書式から自動で付ける（/autoname）</summary>
    public bool UseAutoName { get; set; }

    public string AutoNameFormat { get; set; } = "<filename>_<date:yyyy-MM-dd><ext>";

    /// <summary>&lt;random:N&gt; に英大文字を使う</summary>
    public bool AutoNameRandomUpperCase { get; set; } = true;

    /// <summary>&lt;random:N&gt; に英小文字を使う</summary>
    public bool AutoNameRandomLowerCase { get; set; } = true;

    /// <summary>&lt;random:N&gt; に数字を使う</summary>
    public bool AutoNameRandomNumbers { get; set; } = true;

    /// <summary>&lt;random:N&gt; に記号を使う（ファイル名に使えない文字は除いてある）</summary>
    public bool AutoNameRandomSymbols { get; set; }

    /// <summary>暗号化ファイルの拡張子を偽装する（/camoext）</summary>
    public bool UseCamouflageExtension { get; set; }

    public string CamouflageExtension { get; set; } = ".jpg";

    /// <summary>圧縮レベル（/complv）。0: 無圧縮、1: 速度優先、2: サイズ優先</summary>
    public int CompressionLevel { get; set; } = 1;

    /// <summary>暗号化ファイルに記録するパスワード入力回数の上限（/typelimit）</summary>
    public int MissTypeLimits { get; set; } = 3;

    //-----------------------------------
    // パスワード ZIP
    //-----------------------------------

    /// <summary>
    /// ZIP の暗号方式（/zipalgo）。0: ZipCrypto（Standard ZIP 2.0）、1: AES-128、
    /// 2: AES-256、3: 暗号化なし（ただの ZIP。旧版の「なし」と同じ値）。
    /// 既定は Standard（Windows のエクスプローラーでそのまま開ける互換性を優先。
    /// 旧 Windows 版と同じ既定）。CLI の /zipalgo は 0〜2 のみ（凍結契約）。
    /// </summary>
    public int ZipAlgorithm { get; set; } = 0;

    /// <summary>パスワード ZIP を常に同じ場所へ保存する（/zipsaveto）</summary>
    public bool ZipSaveToSameFolder { get; set; }

    public string ZipSaveToFolderPath { get; set; } = string.Empty;

    //-----------------------------------
    // 保存設定（復号）
    //-----------------------------------

    /// <summary>復号したファイルを常に同じ場所へ保存する（/dsaveto）</summary>
    public bool DecryptSaveToSameFolder { get; set; }

    public string DecryptSaveToFolderPath { get; set; } = string.Empty;

    /// <summary>復号時に親フォルダーを生成しない（/nopfldr）</summary>
    public bool NoParentFolder { get; set; }

    /// <summary>復号したファイルのタイムスタンプを復号日時にする（/now）</summary>
    public bool SetTimeStampToNow { get; set; }

    /// <summary>CRC32 が一致しなくても復号を続行する（/ignorecrc）</summary>
    public bool ContinueOnHashMismatch { get; set; }

    /// <summary>
    /// 復号時、同名ファイルがあるとき上書きの確認をする（/ow、旧 fDecryptConfirmOverwrite）。
    /// 暗号化側は <see cref="ConfirmOverwrite"/>。既定 ON（旧版と同じ）
    /// </summary>
    public bool DecryptConfirmOverwrite { get; set; } = true;

    //-----------------------------------
    // 高度な設定（パスワードファイル）
    //-----------------------------------

    /// <summary>パスワード入力欄へのファイルのドラッグ＆ドロップを許可する（/pf）</summary>
    public bool AllowPasswordFileDragDrop { get; set; }

    /// <summary>暗号化時、決まったパスワードファイルがあれば自動的に使う</summary>
    public bool CheckPasswordFileOnEncrypt { get; set; }

    public string EncryptPasswordFilePath { get; set; } = string.Empty;

    /// <summary>復号時、決まったパスワードファイルがあれば自動的に使う</summary>
    public bool CheckPasswordFileOnDecrypt { get; set; }

    public string DecryptPasswordFilePath { get; set; } = string.Empty;

    /// <summary>
    /// パスワード入力の制限回数を超えたら暗号化ファイルを破壊する（/breakfile）。
    /// この指定は暗号化ファイルのヘッダ（4 バイト目）に記録され、ファイルと一緒に運ばれる。
    /// </summary>
    public bool BreakFileWhenLimitExceeded { get; set; }

    /// <summary>指定したパスワードファイルが無くてもエラーを出さない（/nomsgp）</summary>
    public bool NoErrorWhenPasswordFileMissing { get; set; }

    /// <summary>パスワードファイルが使えるときは、入力画面を挟まずに処理を始める</summary>
    public bool ExecuteWithPasswordFileWithoutConfirm { get; set; }

    //-----------------------------------
    // 削除
    //-----------------------------------

    /// <summary>
    /// 暗号化後に元ファイルを削除する（/del、旧 fDelOrgFile）。0: しない、1: 通常、2: ごみ箱、3: 完全削除。
    /// 「削除するか」と「どう削除するか」を 1 つの値で持つ（5.5.0 からの保存形式。0 以外なら削除する）
    /// </summary>
    public int DeleteMode { get; set; }

    /// <summary>復号後に暗号化ファイルを削除する（/delenc、旧 fDelEncFile）。値の意味は <see cref="DeleteMode"/> と同じ</summary>
    public int DeleteEncryptedFileMode { get; set; }

    /// <summary>
    /// 設定画面の「削除方法」（旧 fCompleteDelFile）。1: 通常、2: ごみ箱、3: 完全削除。0 は未設定。
    /// 削除が OFF のときにも方法を覚えておくための値で、メインウィンドウのチェックボックスや
    /// 設定画面で削除を ON にしたときはこの方法で <see cref="DeleteMode"/> /
    /// <see cref="DeleteEncryptedFileMode"/> を埋める。実際の削除は各 *Mode の値で行う
    /// </summary>
    public int DeleteMethod { get; set; }

    /// <summary>
    /// 暗号化画面（メインウィンドウ）に「暗号化後に元のファイルを削除する」チェックボックスを出す
    /// （/chkdel、旧 fEncryptShowDelChkBox）。チェックの初期値は <see cref="DeleteMode"/>、
    /// 切り替えはその実行だけに効き、設定には書き戻さない（旧版と同じ）
    /// </summary>
    public bool EncryptShowDeleteCheckBox { get; set; }

    /// <summary>
    /// 復号画面（メインウィンドウ）に「復号後に暗号化ファイルを削除する」チェックボックスを出す
    /// （/chkdel、旧 fDecryptShowDelChkBox）
    /// </summary>
    public bool DecryptShowDeleteCheckBox { get; set; }

    /// <summary>完全削除でランダム値を書き込む回数（/delrand）</summary>
    public int DeleteRandomTimes { get; set; } = 1;

    /// <summary>完全削除でゼロを書き込む回数（/delnull）</summary>
    public int DeleteZeroTimes { get; set; } = 1;

    /// <summary>暗号化後の元ファイル削除の前に確認する（/comfdel、旧 fConfirmToDeleteAfterEncryption）</summary>
    public bool ConfirmDelete { get; set; } = true;

    /// <summary>復号後の暗号化ファイル削除の前に確認する（/comfdel、旧 fConfirmToDeleteAfterDecryption）</summary>
    public bool ConfirmDeleteAfterDecryption { get; set; } = true;

    //-----------------------------------
    // 記憶パスワード
    //   実体のパスワードはここには入れない（SecretVault / _AtcVault.json 側）。
    //   ここにあるのは「記憶して事前入力するか」のフラグだけ
    //-----------------------------------

    /// <summary>暗号化パスワードを記憶して事前入力する（旧 fMyEncryptPasswordKeep）</summary>
    public bool RememberEncryptPassword { get; set; }

    /// <summary>復号パスワードを記憶して事前入力する（旧 fMyDecryptPasswordKeep）</summary>
    public bool RememberDecryptPassword { get; set; }

    /// <summary>
    /// 記憶パスワードがあるとき、入力・確認の画面を出さずにそのまま暗号化/復号を始める
    /// （旧 fMemPasswordExe、/mempexe）。記憶していない側には効かない
    /// </summary>
    public bool ExecuteWithRememberedPassword { get; set; }

    /// <summary>暗号化画面のパスワード強度メーターを表示する（旧 fPasswordStrengthMeter、/psm）</summary>
    public bool ShowPasswordStrengthMeter { get; set; } = true;

    //-----------------------------------
    // 関連付け（Windows のみ意味を持つ）
    //-----------------------------------

    /// <summary>
    /// 関連付けした .atc ファイルにエクスプローラーで表示するアイコン（1〜4）。
    /// 実体は同梱の Icons\atc1.ico〜atc4.ico。旧版の AtcsFileIconIndex に相当する
    /// （旧版は本体 exe 内のアイコングループ 1〜4 を指していた）
    /// </summary>
    public int AssociationIconIndex { get; set; } = 1;

    //-----------------------------------
    // 派生値
    //-----------------------------------

    /// <summary>
    /// いま有効な削除方法（1〜3）。<see cref="DeleteMethod"/> が未設定（5.5.0 の設定ファイルや既定値）なら
    /// 削除 ON になっている側の方法を引き継ぎ、どちらも無ければ通常削除
    /// </summary>
    [JsonIgnore]
    public int EffectiveDeleteMethod =>
        IsDeleteMethodValue(DeleteMethod) ? DeleteMethod
        : IsDeleteMethodValue(DeleteMode) ? DeleteMode
        : IsDeleteMethodValue(DeleteEncryptedFileMode) ? DeleteEncryptedFileMode
        : DeleteModes.Delete;

    /// <summary>
    /// 暗号化後に元ファイルを削除するときの方法。<paramref name="enabled"/> が false なら削除しない（0）。
    /// 設定が「削除しない」のままメインウィンドウのチェックで ON にされたときは <see cref="EffectiveDeleteMethod"/>。
    /// 1〜3 以外の不正値（壊れた設定ファイルなど）は置き換えずそのまま返し、PostProcessHelper.DeleteAsync の
    /// 未定義値ガードに任せる（有効値へ丸めると「ごみ箱のつもりが完全削除」を防ぐガードが働かない。CodeRabbit PR#20）
    /// </summary>
    public int ResolveDeleteMode(bool enabled) => enabled ? (DeleteMode == DeleteModes.None ? EffectiveDeleteMethod : DeleteMode) : DeleteModes.None;

    /// <summary>復号後に暗号化ファイルを削除するときの方法。<see cref="ResolveDeleteMode"/> の復号側</summary>
    public int ResolveDeleteEncryptedFileMode(bool enabled) => enabled ? (DeleteEncryptedFileMode == DeleteModes.None ? EffectiveDeleteMethod : DeleteEncryptedFileMode) : DeleteModes.None;

    private static bool IsDeleteMethodValue(int v) => v is DeleteModes.Delete or DeleteModes.Trash or DeleteModes.Wipe;

    [JsonIgnore]
    public CompressionLevel CompressionLevelValue => CompressionLevel switch
    {
        0 => System.IO.Compression.CompressionLevel.NoCompression,
        2 => System.IO.Compression.CompressionLevel.Optimal,
        _ => System.IO.Compression.CompressionLevel.Fastest,
    };

    /// <summary>
    /// 設定の複製を作る。コマンドライン引数の上書きは複製に対して行い、
    /// 保存されている設定には影響させない。
    /// </summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
