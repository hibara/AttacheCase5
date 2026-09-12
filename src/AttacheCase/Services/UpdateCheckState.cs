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
// 更新確認の実行状態。
//
// 「最後にいつ確認したか」は利用者が決める設定ではなく、実行時の記録なので
// settings.json とは別のファイルに持つ。設定画面を開いたまま背後で保存が
// 走ったときに、互いの変更を潰し合うのも避けられる。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AttacheCase.Services;

/// <summary>ファイルに保存する内容。</summary>
public sealed class UpdateCheckState
{
    /// <summary>最後に更新確認を行った時刻（UTC）。一度も行っていなければ null。</summary>
    [JsonPropertyName("lastCheckedUtc")]
    public DateTimeOffset? LastCheckedUtc { get; set; }

    /// <summary>
    /// 利用者が「このバージョンはスキップ」を選んだ版。
    ///
    /// これより新しい版が出れば、また知らせる。バージョンは上がるときだけ
    /// 受け付ける作りなので、そこは自然にそうなる。
    /// </summary>
    [JsonPropertyName("skippedVersion")]
    public string? SkippedVersion { get; set; }
}

/// <summary>更新確認の間隔を決め、その記録を読み書きする。</summary>
public class UpdateCheckStateStore
{
    /// <summary>確認の間隔。起動のたびに問い合わせると配信側にも利用者にも負担。</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// 読み出し → 書き換え → 保存を直列化する錠。
    ///
    /// 起動時の確認は背後の継続で走り、手動の確認とスキップの記録は UI スレッドで
    /// 走る。重なると、古い内容を読んだ側があとから保存し、もう一方が書いた
    /// LastCheckedUtc や SkippedVersion を消してしまう。
    ///
    /// 呼び出し口ごとに store を作るので、錠は static でなければ意味がない。
    /// </summary>
    private static readonly object StateLock = new();

    private readonly string _filePath;

    /// <param name="filePath">
    /// 記録の置き場。既定は ~/Library/Application Support/AttacheCase/update_check.json。
    /// テストから一時ディレクトリを渡して、実際の環境を汚さないようにできる。
    /// </param>
    public UpdateCheckStateStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AttacheCase",
            "update_check.json");
    }

    public string FilePath => _filePath;

    /// <summary>
    /// 記録を読む。無い場合や壊れている場合は「一度も確認していない」として扱う。
    /// </summary>
    public UpdateCheckState Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new UpdateCheckState();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<UpdateCheckState>(json, JsonOpts)
                   ?? new UpdateCheckState();
        }
        catch (Exception)
        {
            // 読めなくても更新確認は続けたい。確認しすぎるほうが、しないより安全
            return new UpdateCheckState();
        }
    }

    /// <summary>
    /// 確認した時刻を記録する。書き込めなくても失敗にしない
    /// （毎回確認することになるだけで、害はない）。
    /// </summary>
    public void MarkChecked(DateTimeOffset now)
    {
        lock (StateLock)
        {
            var state = Load();
            state.LastCheckedUtc = now.ToUniversalTime();
            Save(state);
        }
    }

    /// <summary>
    /// 「このバージョンはスキップ」を記録する。
    /// </summary>
    public void MarkSkipped(string version)
    {
        lock (StateLock)
        {
            var state = Load();
            state.SkippedVersion = version;
            Save(state);
        }
    }

    /// <summary>
    /// その版を利用者がスキップ済みか。
    /// </summary>
    public bool IsSkipped(string version)
    {
        var skipped = Load().SkippedVersion;
        return !string.IsNullOrEmpty(skipped) &&
               string.Equals(skipped, version, StringComparison.Ordinal);
    }

    private void Save(UpdateCheckState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(state, JsonOpts));
        }
        catch (Exception)
        {
            // 記録できないだけなので握る
        }
    }

    /// <summary>いま確認すべきか。</summary>
    public bool ShouldCheck(DateTimeOffset now) => ShouldCheck(Load().LastCheckedUtc, now);

    /// <summary>
    /// いま確認すべきかを判定する。
    /// </summary>
    /// <remarks>
    /// 記録が未来を指している場合も確認する。時計が狂っていたり、あとから
    /// 巻き戻されたりしたときに、更新を永久に受け取れなくなるのを避けるため。
    /// セキュリティ上の修正を届けられないほうが危ない。
    /// </remarks>
    internal static bool ShouldCheck(DateTimeOffset? lastChecked, DateTimeOffset now)
    {
        if (lastChecked is null) return true;

        var elapsed = now - lastChecked.Value;
        if (elapsed < TimeSpan.Zero) return true;

        return elapsed >= CheckInterval;
    }
}
