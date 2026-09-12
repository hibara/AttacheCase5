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
// 設定の保存と読み込み。
// 保存先: ~/Library/Application Support/AttacheCase/settings.json
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Text.Json;
using AttacheCase.Models;

namespace AttacheCase.Services;

public class SettingsService
{
    private static readonly Lazy<SettingsService> LazyInstance = new(() => new SettingsService());

    public static SettingsService Instance => LazyInstance.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly bool _allowRegistryMigration;
    private AppSettings _current;

    /// <summary>
    /// ポータブルモード（実行ファイルの隣の _AtcCase.json を使用中）か。
    /// このとき Save はポータブルファイルへ書き戻す。
    /// </summary>
    public bool IsPortableMode { get; private set; }

    /// <summary>使用中のポータブル設定ファイル（ポータブルモードのときのみ）</summary>
    public string? PortableFilePath { get; private set; }

    /// <summary>
    /// ポータブル設定ファイルは見つかったが、まだ利用者が承認していない。
    /// GUI 起動後に確認ダイアログを出し、承認されたら次回起動から有効になる。
    /// </summary>
    public string? PendingPortableFilePath { get; private set; }

    public SettingsService(string? filePath = null)
    {
        // レジストリからの移行・ポータブル設定の探索は「既定の保存場所」のときだけ。
        // テストなどがパスを注入した場合に実環境を読むと、分離が壊れる
        _allowRegistryMigration = filePath == null;
        _filePath = filePath ?? Path.Combine(GetSettingsDirectory(), "settings.json");
        _current = Load();
    }

    /// <summary>
    /// 保存されている設定。GUI 操作時はこのインスタンスがそのまま使われる。
    /// コマンドライン起動時は <see cref="AppSettings.Clone"/> した複製に引数を上書きする。
    /// </summary>
    public AppSettings Current => _current;

    public string FilePath => _filePath;

    /// <summary>
    /// 設定ファイルを読み込む。無い場合や壊れている場合は既定値を返す。
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            // ポータブル設定（exe の隣の _AtcCase.json）。承認済みのときだけ使う。
            // 未承認なら Pending に控え、GUI 起動後に確認ダイアログを出す
            if (_allowRegistryMigration)
            {
                var portablePath = Settings.PortableSettings.Find();
                if (portablePath != null)
                {
                    // 1 回の読み込みで内容とハッシュを取り、そのハッシュで承認を照合する。
                    // 「ハッシュ照合→読み込み」と 2 回読むと、その間の差し替えで
                    // 未承認の設定が適用されうる（共有フォルダー・USB での脅威）
                    var loaded = Settings.PortableSettings.LoadWithHash(portablePath);
                    if (loaded != null &&
                        new Settings.PortableSettingsGuard().IsApprovedHash(loaded.Value.Hash))
                    {
                        IsPortableMode = true;
                        PortableFilePath = portablePath;
                        return loaded.Value.Settings;
                    }

                    if (loaded != null)
                    {
                        PendingPortableFilePath = portablePath;
                    }
                    // 壊れていて読めないファイルは、承認しても使えないので Pending にしない
                }
            }

            if (!File.Exists(_filePath))
            {
                // 初回起動: Windows で旧版のレジストリ設定があれば一度だけ移行する。
                // 移行結果はすぐ保存して以後はこの経路を通らない。レジストリは消さない
                if (_allowRegistryMigration && OperatingSystem.IsWindows() &&
                    Platform.RegistrySettingsMigrator.TryMigrateFromDefaultLocation(out var migrated) &&
                    migrated != null)
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(_filePath);
                        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                        File.WriteAllText(_filePath, JsonSerializer.Serialize(migrated, JsonOptions));
                    }
                    catch (Exception)
                    {
                        // 保存できなくても、この起動では移行済みの値で動かす
                    }

                    return migrated;
                }

                return new AppSettings();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 壊れていた場合は既定値に戻す（起動できなくなるより良い）
            return new AppSettings();
        }
    }

    /// <summary>
    /// 設定が保存されたときに発生する。ウィンドウの見た目に関わる設定を
    /// すぐ反映させるために使う。
    /// </summary>
    public event EventHandler<AppSettings>? Saved;

    /// <summary>
    /// 設定を保存し、以後の <see cref="Current"/> を差し替える。
    /// </summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // ポータブルモードではポータブルファイルへ書き戻す。
        // 内容が変わるので、承認済みハッシュも自分の変更として更新する
        //（設定画面からの変更は利用者自身の操作であり、信頼してよい）
        if (IsPortableMode && PortableFilePath != null)
        {
            File.WriteAllText(PortableFilePath, JsonSerializer.Serialize(settings, JsonOptions));
            new Settings.PortableSettingsGuard().Approve(PortableFilePath);
            _current = settings;
            Saved?.Invoke(this, settings);
            return;
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        _current = settings;

        Saved?.Invoke(this, settings);
    }

    // =====================================================================
    // ポータブル設定の作成・解除（設定画面の「設定の持ち運び」から呼ばれる）
    // =====================================================================

    /// <summary>
    /// 現在の設定をポータブル設定ファイル（_AtcCase.json）として書き出す。
    /// 自分で作ったファイルなので、承認済みハッシュも同時に記録する。
    /// 記憶パスワードの保管庫（_AtcVault.json）があれば一緒に複製する
    /// （持ち歩く USB 側に無いと、よその PC で移行コードを入れても開く物が無い）。
    ///
    /// 反映は次回起動から（起動時に読む設計のため。旧版の INI と同じ）。
    /// </summary>
    /// <param name="targetDirectory">省略時は実行ファイルのフォルダー。</param>
    /// <returns>書き出したポータブル設定ファイルのパス。</returns>
    public string CreatePortableCopy(string? targetDirectory = null)
    {
        var dir = targetDirectory ?? AppContext.BaseDirectory;
        var portablePath = Path.Combine(dir, Settings.PortableSettings.FileName);

        File.WriteAllText(portablePath, JsonSerializer.Serialize(_current, JsonOptions));
        new Settings.PortableSettingsGuard().Approve(portablePath);

        // 記憶パスワードの保管庫も持ち運び側へ（通常側は残す。
        // この端末の device プロテクターは両方のファイルで有効なまま）
        var vaultSource = Path.Combine(
            Path.GetDirectoryName(_filePath) ?? ".", Settings.SecretVault.FileName);
        if (File.Exists(vaultSource))
        {
            File.Copy(vaultSource, Path.Combine(dir, Settings.SecretVault.FileName), overwrite: true);
        }

        return portablePath;
    }

    /// <summary>
    /// ポータブル設定の使用をやめる。承認を取り消し、ファイルを削除する
    /// （_AtcVault.json も一緒に消す。持ち歩き用の複製であり、通常側の
    /// 保管庫は残っている）。反映は次回起動から。
    /// </summary>
    /// <returns>削除できたか（ファイルが無かった場合も true）。</returns>
    public bool RemovePortableCopy()
    {
        var portablePath = PortableFilePath
                           ?? PendingPortableFilePath
                           ?? Settings.PortableSettings.Find();

        new Settings.PortableSettingsGuard().Revoke();

        if (portablePath == null) return true;

        try
        {
            if (File.Exists(portablePath)) File.Delete(portablePath);

            var vaultPath = Path.Combine(
                Path.GetDirectoryName(portablePath) ?? ".", Settings.SecretVault.FileName);
            if (File.Exists(vaultPath))
            {
                // 通常側に保管庫が無ければ消さずに引き継ぐ（この PC で移行コードから
                // 使い始めた場合など、ポータブル側が唯一の保管庫のことがある）
                var normalVault = Path.Combine(
                    Path.GetDirectoryName(_filePath) ?? ".", Settings.SecretVault.FileName);
                if (!File.Exists(normalVault))
                {
                    var normalDir = Path.GetDirectoryName(normalVault);
                    if (!string.IsNullOrEmpty(normalDir)) Directory.CreateDirectory(normalDir);
                    File.Move(vaultPath, normalVault);
                }
                else
                {
                    File.Delete(vaultPath);
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // UnauthorizedAccessException は IOException を継承しない。
            // USB 上の読み取り専用ファイル等で現実に起こるので、bool の契約を守る
            return false;
        }
    }

    /// <summary>
    /// 通常の設定ディレクトリ。PortableSettingsGuard など、Instance の初期化中にも
    /// 呼ばれうるコードは、シングルトン経由ではなくこれを直接使う（Lazy 再入防止）。
    /// </summary>
    internal static string GetSettingsDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "AttacheCase");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AttacheCase");
    }
}
