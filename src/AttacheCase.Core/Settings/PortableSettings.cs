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
// ポータブル設定（実行ファイルと同じフォルダーの _AtcCase.json）。
// 旧 Windows 版の _AtcCase.ini に相当する仕組みの新形式。
//
// USB メモリなどで持ち歩く運用のために、通常の設定
// （%APPDATA% / ~/Library の settings.json）の代わりに、exe の隣に置いた
// 設定を使えるようにする。中身は settings.json と同じスキーマ。
//
// セキュリティ（旧版 IniSecurityManager の考え方を踏襲）:
// 共有フォルダーなどに悪意の設定ファイルを置かれると、保存先や削除方法を
// すり替えられる。そこで、ファイルの SHA-256 を利用者が一度承認するまでは
// 読み込まない。承認の記録は「通常の設定ディレクトリ側」に置く
// （ポータブル側に置くと、ファイルごと差し替えられて意味がない）。
//
// 旧 _AtcCase.ini の読み取り互換は未実装（TODO: 記憶パスワードの
// GCM 復号など、旧版固有の処理を伴うため別途）。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AttacheCase.Models;

namespace AttacheCase.Settings;

public static class PortableSettings
{
    public const string FileName = "_AtcCase.json";

    /// <summary>
    /// 実行ファイルと同じフォルダーにポータブル設定があればそのパスを返す。
    /// </summary>
    public static string? Find(string? baseDirectory = null)
    {
        var dir = baseDirectory ?? AppContext.BaseDirectory;
        var path = Path.Combine(dir, FileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// ポータブル設定を読み込む。壊れている場合は null。
    /// </summary>
    public static AppSettings? Load(string path)
        => LoadWithHash(path)?.Settings;

    /// <summary>
    /// ポータブル設定と、その内容の SHA-256 を【1 回の読み込みで】返す。
    /// 承認確認（ハッシュ照合）と読み込みで別々にファイルを読むと、その間に
    /// 内容を差し替えられて「承認を通過した未承認設定」が適用されうるため、
    /// 検証と復元は必ず同じバイト列から行う。壊れている場合は null。
    /// </summary>
    public static (AppSettings Settings, string Hash)? LoadWithHash(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(bytes);
            if (settings == null) return null;
            return (settings, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>ファイルの SHA-256（16 進大文字）。</summary>
    public static string ComputeHash(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

/// <summary>
/// ポータブル設定の信頼確認。承認済みハッシュを通常の設定ディレクトリ側に記録する。
/// </summary>
public sealed class PortableSettingsGuard
{
    private sealed class TrustRecord
    {
        public string ApprovedHash { get; set; } = "";
    }

    private readonly string _trustFilePath;

    /// <param name="trustFilePath">
    /// 承認記録の保存先。省略時は通常の設定ディレクトリの portable_trust.json。
    /// テストからは一時パスを注入する。
    /// </param>
    public PortableSettingsGuard(string? trustFilePath = null)
    {
        // SettingsService.Instance をここで読んではいけない。
        // Instance（Lazy）の初期化中の Load() からこのコンストラクターが呼ばれるため、
        // Instance に触ると Lazy への再入で InvalidOperationException になり、
        // ポータブル設定が黙って無視される
        _trustFilePath = trustFilePath ?? Path.Combine(
            Services.SettingsService.GetSettingsDirectory(),
            "portable_trust.json");
    }

    /// <summary>
    /// そのポータブル設定ファイルが承認済みか（内容が変わっていれば未承認扱い）。
    /// 読み込みと併用するときは、二度読みを避けるため
    /// <see cref="PortableSettings.LoadWithHash"/> + <see cref="IsApprovedHash"/> を使う。
    /// </summary>
    public bool IsApproved(string portableFilePath)
    {
        try
        {
            return IsApprovedHash(PortableSettings.ComputeHash(portableFilePath));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>計算済みのハッシュが承認済みのものと一致するか。</summary>
    public bool IsApprovedHash(string hash)
    {
        try
        {
            if (!File.Exists(_trustFilePath)) return false;

            var record = JsonSerializer.Deserialize<TrustRecord>(File.ReadAllText(_trustFilePath));
            if (record == null || string.IsNullOrEmpty(record.ApprovedHash)) return false;

            return string.Equals(record.ApprovedHash, hash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 現在の内容を承認として記録する（利用者が確認ダイアログで許可したときに呼ぶ）。
    /// </summary>
    public void Approve(string portableFilePath)
    {
        var dir = Path.GetDirectoryName(_trustFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var record = new TrustRecord { ApprovedHash = PortableSettings.ComputeHash(portableFilePath) };
        File.WriteAllText(_trustFilePath, JsonSerializer.Serialize(record));
    }

    /// <summary>承認を取り消す。</summary>
    public void Revoke()
    {
        try
        {
            if (File.Exists(_trustFilePath)) File.Delete(_trustFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 取り消せなくても、次回のハッシュ照合はファイル内容に基づくので安全側
            //（UnauthorizedAccessException は IOException を継承しない別系統。
            // 読み取り専用属性や ACL で現実に起こる）
        }
    }
}
