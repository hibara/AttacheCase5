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

    /// <summary>メニューバーにアイコンを表示する（Windows 版の「タスクバーに表示する」に相当）</summary>
    public bool ShowMenuBarIcon { get; set; }

    /// <summary>ウィンドウを常に最前面に表示する</summary>
    public bool WindowForeground { get; set; }

    /// <summary>アタッシェケースを複数起動しない</summary>
    public bool NoMultipleInstance { get; set; }

    //-----------------------------------
    // 保存設定（暗号化）
    //-----------------------------------

    /// <summary>暗号化ファイルの種類（/entype）。1: .atc、3: パスワード ZIP</summary>
    public int EncryptionFileType { get; set; } = EncryptionFileTypes.Atc;

    /// <summary>暗号化ファイルを常に同じ場所へ保存する（/saveto）</summary>
    public bool SaveToSameFolder { get; set; }

    public string SaveToFolderPath { get; set; } = string.Empty;

    /// <summary>同名ファイルがあるとき上書きの確認をする（/ow）</summary>
    public bool ConfirmOverwrite { get; set; } = true;

    /// <summary>フォルダー内のファイルを 1 つずつ個別に暗号化する（/oneby）</summary>
    public bool EncryptOneByOne { get; set; }

    /// <summary>暗号化ファイルのタイムスタンプを元ファイルに合わせる（/orgdt）</summary>
    public bool KeepTimeStamp { get; set; }

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

    /// <summary>暗号化後に元ファイルを削除する（/del）。0: しない、1: 通常、2: ごみ箱、3: 完全削除</summary>
    public int DeleteMode { get; set; }

    /// <summary>復号後に暗号化ファイルを削除する（/delenc）</summary>
    public int DeleteEncryptedFileMode { get; set; }

    /// <summary>完全削除でランダム値を書き込む回数（/delrand）</summary>
    public int DeleteRandomTimes { get; set; } = 1;

    /// <summary>完全削除でゼロを書き込む回数（/delnull）</summary>
    public int DeleteZeroTimes { get; set; } = 1;

    /// <summary>削除の前に確認する（/comfdel）</summary>
    public bool ConfirmDelete { get; set; } = true;

    //-----------------------------------
    // 記憶パスワード
    //   実体のパスワードはここには入れない（SecretVault / _AtcVault.json 側）。
    //   ここにあるのは「記憶して事前入力するか」のフラグだけ
    //-----------------------------------

    /// <summary>暗号化パスワードを記憶して事前入力する（旧 fMyEncryptPasswordKeep）</summary>
    public bool RememberEncryptPassword { get; set; }

    /// <summary>復号パスワードを記憶して事前入力する（旧 fMyDecryptPasswordKeep）</summary>
    public bool RememberDecryptPassword { get; set; }

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
