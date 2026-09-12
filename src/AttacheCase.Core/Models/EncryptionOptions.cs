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

using System.Collections.Generic;
using System.IO.Compression;

namespace AttacheCase.Models;

/// <summary>
/// 暗号化のオプション設定
/// </summary>
public class EncryptionOptions
{
    /// <summary>
    /// 自己実行形式で暗号化するか
    /// </summary>
    public bool CreateExecutable { get; set; } = false;

    /// <summary>
    /// タイムスタンプを保持するか
    /// </summary>
    public bool KeepTimeStamp { get; set; } = false;

    /// <summary>
    /// 圧縮レベル
    /// </summary>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Fastest;

    /// <summary>
    /// パスワード入力ミスの許容回数
    /// </summary>
    public int MissTypeLimits { get; set; } = 3;

    /// <summary>
    /// 入力ミスが上限に達したら、この暗号化ファイルを破壊してよいか。
    /// 平文ヘッダの 4 バイト目に記録され、復号する側がこれを見て判断する。
    /// </summary>
    public bool BreakFileWhenLimitExceeded { get; set; }

    /// <summary>
    /// 新しいアーカイブ名
    /// </summary>
    public string ArchiveName { get; set; } = string.Empty;

    /// <summary>
    /// パスワードファイル（/pfile）から求めた鍵素材。
    /// 非 null のとき、password 文字列の代わりにこのバイト列を鍵導出に使う。
    /// </summary>
    public byte[]? PasswordBinary { get; set; }

    /// <summary>
    /// RSA公開鍵暗号を使用するか
    /// </summary>
    public bool IsUseRsaEncryption { get; set; } = false;

    /// <summary>
    /// RSA公開鍵XML文字列
    /// </summary>
    public string RsaPublicKeyXml { get; set; } = string.Empty;

    /// <summary>
    /// GUID文字列
    /// </summary>
    public string GuidString { get; set; } = string.Empty;

    /// <summary>
    /// Post-PPAP 公開鍵方式の宛先リスト。Count > 0 のとき公開鍵方式で暗号化される。
    /// IsUseRsaEncryption フラグの有無に関わらず、このリストが非空ならば公開鍵方式が優先される。
    /// </summary>
    public List<PostPpapKeyInfo> Recipients { get; set; } = new();
}
