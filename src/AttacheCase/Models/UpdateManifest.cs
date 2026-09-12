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
// 自動更新の配信マニフェスト（appcast）。
//
// 配信側に appcast.json と、その分離署名 appcast.json.sig（Ed25519）を置く。
// 署名は JSON を再構築したものではなく、受け取ったバイト列そのものに対して
// 検証する。整形や鍵の並び順が変わっても壊れないようにするため。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AttacheCase.Models;

/// <summary>
/// 配信マニフェスト。1 つのバージョンについて、アーキテクチャごとの DMG を持つ。
/// </summary>
public sealed class UpdateManifest
{
    /// <summary>公開バージョン。"5.1.0" のような形式。</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 動作に必要な OS の最低バージョン。macOS は "13.0"、Windows は
    /// "10.0.19041" のような形式（Version.TryParse がどちらも扱える）。
    /// </summary>
    [JsonPropertyName("minimumSystemVersion")]
    public string? MinimumSystemVersion { get; set; }

    /// <summary>ベータ版か（Windows のマニフェストが持つ。表示の参考情報）。</summary>
    [JsonPropertyName("beta")]
    public bool Beta { get; set; }

    /// <summary>公開日時。</summary>
    [JsonPropertyName("pubDate")]
    public DateTimeOffset? PubDate { get; set; }

    /// <summary>リリースノートの URL。参考情報として持つが、表示には使わない。</summary>
    [JsonPropertyName("releaseNotesUrl")]
    public string? ReleaseNotesUrl { get; set; }

    /// <summary>
    /// 変更点。言語コード（"ja" / "en"）ごとに、見出し付きの節を並べる。
    ///
    /// HTML ではなく構造化データで持つ。更新の確認画面は、更新経路のうち唯一
    /// 外から来た内容を描く場所なので、HTML レンダラを介在させたくない。
    /// </summary>
    [JsonPropertyName("releaseNotes")]
    public Dictionary<string, List<ReleaseNoteSection>>? ReleaseNotes { get; set; }

    /// <summary>アーキテクチャ別の配布物。キーは "arm64" / "x64"。</summary>
    [JsonPropertyName("builds")]
    public Dictionary<string, UpdateBuild> Builds { get; set; } = new();

    /// <summary>
    /// 表示に使う変更点を選ぶ。指定の言語が無ければ英語、それも無ければ
    /// 入っているものから 1 つ。どれも無ければ空。
    /// </summary>
    public IReadOnlyList<ReleaseNoteSection> NotesFor(string language)
    {
        if (ReleaseNotes is null || ReleaseNotes.Count == 0)
        {
            return Array.Empty<ReleaseNoteSection>();
        }

        // JSON の null 値（"ja": null）はキーとしては存在するので、値も確かめる
        if (ReleaseNotes.TryGetValue(language, out var exact) && exact is not null) return exact;
        if (ReleaseNotes.TryGetValue("en", out var english) && english is not null) return english;

        foreach (var sections in ReleaseNotes.Values)
        {
            if (sections is not null) return sections;
        }

        return Array.Empty<ReleaseNoteSection>();
    }
}

/// <summary>変更点のひとまとまり。「新機能」「改善」「修正」など。</summary>
public sealed class ReleaseNoteSection
{
    /// <summary>
    /// 見出しの種類。"new" / "improvements" / "fixes" のいずれか。
    /// 表示する語はアプリ側で訳す。配信側に訳語を持たせると、言語ごとに
    /// 見出しがずれる余地ができるため。
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>その節の項目。</summary>
    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = new();
}

/// <summary>1 アーキテクチャ分の配布物。</summary>
public sealed class UpdateBuild
{
    /// <summary>DMG の URL。https のみ受け付ける。</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>DMG のバイト数。ダウンロード前の妥当性確認に使う。</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>DMG の SHA-256（16 進小文字）。</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>更新確認の結果。</summary>
public enum UpdateCheckStatus
{
    /// <summary>最新版を使っている。</summary>
    UpToDate,

    /// <summary>新しい版がある。</summary>
    UpdateAvailable,

    /// <summary>
    /// 確認できなかった。通信不能・署名不一致・マニフェスト不正のいずれも含む。
    /// 起動時の自動確認では黙って諦める（利用者の作業を止めない）。
    /// </summary>
    Failed,
}

/// <summary>更新確認の結果。</summary>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateManifest? Manifest = null,
    UpdateBuild? Build = null,
    string? Error = null)
{
    public static UpdateCheckResult UpToDate() => new(UpdateCheckStatus.UpToDate);

    public static UpdateCheckResult Failed(string error) =>
        new(UpdateCheckStatus.Failed, Error: error);

    public static UpdateCheckResult Available(UpdateManifest manifest, UpdateBuild build) =>
        new(UpdateCheckStatus.UpdateAvailable, manifest, build);
}
