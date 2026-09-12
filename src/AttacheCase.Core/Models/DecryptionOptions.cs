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

namespace AttacheCase.Models;

/// <summary>
/// 復号のオプション設定
/// </summary>
public class DecryptionOptions
{
    /// <summary>
    /// 親フォルダーを生成しないか
    /// </summary>
    public bool NoParentFolder { get; set; } = false;

    /// <summary>
    /// タイムスタンプを復号時に合わせるか
    /// </summary>
    public bool RestoreTimeStamp { get; set; } = false;

    /// <summary>
    /// パスワードファイル（/dpfile）から求めた鍵素材。
    /// 非 null のとき、password 文字列の代わりにこのバイト列を鍵導出に使う。
    /// </summary>
    public byte[]? PasswordBinary { get; set; }

    /// <summary>
    /// RSA公開鍵暗号を使用するか
    /// </summary>
    public bool IsUseRsaEncryption { get; set; } = false;

    /// <summary>
    /// サルベージモード: 親フォルダーを一つずつ確認、生成しながら復号する
    /// </summary>
    public bool SalvageCreateParentFolderOneByOne { get; set; } = false;

    /// <summary>
    /// サルベージモード: すべてのファイルを同じ階層のディレクトリーに復号する
    /// </summary>
    public bool SalvageIntoSameDirectory { get; set; } = false;

    /// <summary>
    /// サルベージモード: ファイルのハッシュ値チェックを無視する
    /// </summary>
    public bool SalvageIgnoreHashCheck { get; set; } = false;

    /// <summary>
    /// 上書きオプション
    /// </summary>
    public OverwriteOption OverwriteOption { get; set; } = OverwriteOption.Ask;

    /// <summary>
    /// MD5ハッシュ不一致時の動作
    /// </summary>
    public HashMismatchOption HashMismatchOption { get; set; } = HashMismatchOption.Ask;

    /// <summary>
    /// RSA秘密鍵XML文字列（RSA暗号化データの場合）
    /// </summary>
    public string RsaPrivateKeyXml { get; set; } = string.Empty;
}

/// <summary>
/// ファイル上書きオプション
/// </summary>
public enum OverwriteOption
{
    /// <summary>
    /// ユーザーに確認
    /// </summary>
    Ask = 0,

    /// <summary>
    /// 上書き
    /// </summary>
    Overwrite = 1,

    /// <summary>
    /// すべて上書き
    /// </summary>
    OverwriteAll = 2,

    /// <summary>
    /// 新しいファイルのみ
    /// </summary>
    KeepNewer = 3,

    /// <summary>
    /// すべて新しいファイルのみ
    /// </summary>
    KeepNewerAll = 4,

    /// <summary>
    /// スキップ
    /// </summary>
    Skip = 5,

    /// <summary>
    /// すべてスキップ
    /// </summary>
    SkipAll = 6
}

/// <summary>
/// ハッシュ不一致時のオプション
/// </summary>
public enum HashMismatchOption
{
    /// <summary>
    /// ユーザーに確認
    /// </summary>
    Ask = 0,

    /// <summary>
    /// 続行
    /// </summary>
    Continue = 7
}
