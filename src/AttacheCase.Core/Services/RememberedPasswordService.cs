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
// 記憶パスワード（旧 Windows 版の MyKey 相当）。
//
// パスワードの実体は settings.json には決して入れず、SecretVault
// （_AtcVault.json、Envelope Encryption）に保存する。AppSettings 側には
// 「記憶するかどうか」のフラグ（RememberEncryptPassword 等）だけを持つ。
//
// 置き場所は設定ファイルに追従する:
//   通常モード      … 設定ディレクトリ（%APPDATA%\AttacheCase 等）の _AtcVault.json
//   ポータブルモード … _AtcCase.json と同じフォルダー（=持ち歩く USB 側）
//
// 状態は 3 つ:
//   Unlocked  … この端末の device プロテクターで開けた（無入力・平常時）
//   Locked    … ファイルはあるが、この端末では開けない（初めての PC）。
//               移行コードの入力で登録できる
//   None      … ファイルが無い（記憶パスワード未使用）
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Collections.Generic;
using AttacheCase.Settings;

namespace AttacheCase.Services;

public enum RememberedPasswordState
{
    /// <summary>保管庫ファイルが無い（記憶パスワード未使用）。</summary>
    None,

    /// <summary>この端末で開ける。</summary>
    Unlocked,

    /// <summary>ファイルはあるが、この端末では開けない（移行コードが要る）。</summary>
    Locked,
}

public sealed class RememberedPasswordService
{
    /// <summary>payload 内のキー（申し送り §4: 複数の秘密はまとめて 1 レコード）。</summary>
    public const string KeyEncryptPassword = "encryptPassword";
    public const string KeyDecryptPassword = "decryptPassword";

    private static readonly Lazy<RememberedPasswordService> LazyInstance =
        new(() => new RememberedPasswordService());

    public static RememberedPasswordService Instance => LazyInstance.Value;

    private readonly IKeychainService _keychain;
    private readonly string _vaultPath;
    private SecretVault.VaultSession? _session;

    public RememberedPasswordService(
        SettingsService? settingsService = null,
        IKeychainService? keychain = null,
        string? vaultPath = null)
    {
        _keychain = keychain ?? Platform.SecretStoreFactory.CreateDefault();

        if (vaultPath != null)
        {
            _vaultPath = vaultPath;
        }
        else
        {
            var settings = settingsService ?? SettingsService.Instance;
            var dir = settings.IsPortableMode && settings.PortableFilePath != null
                ? Path.GetDirectoryName(settings.PortableFilePath)!
                : Path.GetDirectoryName(settings.FilePath)!;
            _vaultPath = Path.Combine(dir, SecretVault.FileName);
        }
    }

    public string VaultPath => _vaultPath;

    /// <summary>この端末のプロテクターに付けるラベル（DEK で暗号化されて保存される）。</summary>
    private static string DeviceLabel => Environment.MachineName + " / " + Environment.UserName;

    // =====================================================================
    // 状態
    // =====================================================================

    public RememberedPasswordState GetState()
    {
        if (TryGetSession() != null) return RememberedPasswordState.Unlocked;
        return File.Exists(_vaultPath) ? RememberedPasswordState.Locked : RememberedPasswordState.None;
    }

    /// <summary>移行コードで開ける入口が残っているか（Locked のときの案内用）。</summary>
    public bool HasActiveMigrationCode()
        => SecretVault.TryLoad(_vaultPath)?.HasActiveCodeProtector() ?? false;

    // =====================================================================
    // 読み書き
    // =====================================================================

    /// <summary>記憶されたパスワードを返す。無い・この端末で開けない場合は null。</summary>
    public string? GetPassword(string key)
    {
        var session = TryGetSession();
        return session?.GetSecrets()?.GetValueOrDefault(key);
    }

    /// <summary>
    /// パスワードを記憶する（null や空は「その項目を消す」）。
    /// 保管庫が無ければ、この端末の device プロテクター付きで新規作成する。
    /// この端末で開けない保管庫（Locked）には書けない（先に移行コードで登録する）。
    /// </summary>
    /// <returns>書き込めたか。Locked のときや、保管庫へ保存できなかったときは false。</returns>
    public bool SetPasswords(string? encryptPassword, string? decryptPassword)
    {
        try
        {
            var session = TryGetSession();

            if (session == null)
            {
                if (File.Exists(_vaultPath)) return false; // Locked

                // 両方消すだけなら、わざわざ保管庫を作らない
                if (string.IsNullOrEmpty(encryptPassword) && string.IsNullOrEmpty(decryptPassword))
                {
                    return true;
                }

                var dir = Path.GetDirectoryName(_vaultPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _session = session = SecretVault.CreateNew(_vaultPath, _keychain, DeviceLabel);
            }

            var secrets = session.GetSecrets() ?? new Dictionary<string, string>();
            ApplyOrRemove(secrets, KeyEncryptPassword, encryptPassword);
            ApplyOrRemove(secrets, KeyDecryptPassword, decryptPassword);
            session.SetSecrets(secrets);
            return true;
        }
        catch (Exception)
        {
            // ディスクへ書けない（USB 抜去・読み取り専用・容量不足など）。
            // SecretVault 側が巻き戻し済みなので、false（書き込めなかった）で返してよい
            return false;
        }
    }

    private static void ApplyOrRemove(Dictionary<string, string> secrets, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) secrets.Remove(key);
        else secrets[key] = value;
    }

    // =====================================================================
    // 移行コード
    // =====================================================================

    /// <summary>
    /// 別の PC 用の移行コードを発行する（この端末で開けるときのみ）。
    /// 既存のコードは置き換わる。返り値は XXXX-XXXX-XXXX-XXXX 形式。
    /// 開けない・保存できないときは null。
    /// </summary>
    public string? IssueMigrationCode()
    {
        try
        {
            return TryGetSession()?.IssueMigrationCode();
        }
        catch (Exception)
        {
            return null; // 保存失敗。SecretVault 側が巻き戻し済み
        }
    }

    /// <summary>
    /// 移行コードでこの端末を登録する。成功するとコードは使い捨てられ、
    /// 以後この端末では無入力で開ける。
    /// </summary>
    public bool RegisterThisDeviceWithCode(string code)
    {
        if (_session != null) return true; // すでに開けている

        var vault = SecretVault.TryLoad(_vaultPath);
        if (vault == null) return false;

        var session = vault.TryUnlockWithCode(code, _keychain, registerThisDevice: true, DeviceLabel);
        if (session == null) return false;

        _session = session;
        return true;
    }

    // =====================================================================
    // 削除
    // =====================================================================

    /// <summary>
    /// 記憶した内容をすべて消す（保管庫ファイルごと削除）。
    /// 鍵ストアに残る DPK も分かる範囲で掃除する。
    /// </summary>
    public void DeleteAll()
    {
        // 「読む→鍵ストア掃除→ファイル削除」の途中に別プロセスの書き込みが
        // 割り込まないよう、保管庫単位のロックの中で行う（SecretVault と共通）
        using var _ = SecretVault.AcquireExclusiveLock(_vaultPath);

        var vault = SecretVault.TryLoad(_vaultPath);
        if (vault != null)
        {
            foreach (var (id, type) in vault.ListProtectorIds())
            {
                if (type != "device") continue;
                try { _keychain.Delete(SecretVault.KeychainService, id); }
                catch (Exception) { /* 別端末の項目はこの端末に無い。残っても実害なし */ }
            }
        }

        _session?.Dispose();
        _session = null;

        try
        {
            if (File.Exists(_vaultPath)) File.Delete(_vaultPath);
        }
        catch (IOException)
        {
            // 消せなくても、次回の DeleteAll で再試行できる
        }
    }

    // =====================================================================
    // 旧 Windows 版（レジストリ MyKey）からの移行
    // =====================================================================

    /// <summary>
    /// 旧版レジストリ（HKCU\Software\Hibara\AttacheCase5\MyKey）の記憶パスワード
    /// （DPAPI blob）を、一度だけ新しい保管庫へ移す。Windows のみ。
    ///
    /// 「記憶する」フラグが立っていて、かつ DPAPI で復号できた項目だけを移す
    /// （別ユーザー・別 PC の blob は復号できないので、静かにスキップされる）。
    /// レジストリ側は消さない（旧版と併用しても壊れないようにするため。
    /// RegistrySettingsMigrator と同じ方針）。
    /// </summary>
    /// <returns>移行した項目があったか。</returns>
    public bool TryMigrateFromLegacyRegistry()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (File.Exists(_vaultPath)) return false; // 既に保管庫がある = 移行済みか新規利用中

        try
        {
            using var myKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                Platform.RegistrySettingsMigrator.RegistryRootPath + @"\MyKey");
            if (myKey == null) return false;

            var encrypt = ReadLegacyDpapiPassword(myKey, "MyEncryptPasswordString", "fMyEncryptPasswordKeep");
            var decrypt = ReadLegacyDpapiPassword(myKey, "MyDecryptPasswordString", "fMyDecryptPasswordKeep");

            if (string.IsNullOrEmpty(encrypt) && string.IsNullOrEmpty(decrypt)) return false;

            return SetPasswords(encrypt, decrypt);
        }
        catch (Exception)
        {
            return false; // 読めない環境でも起動は続ける
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? ReadLegacyDpapiPassword(
        Microsoft.Win32.RegistryKey myKey, string valueName, string keepFlagName)
    {
        // 旧版は「記憶する」フラグが立っているときだけ値を使う（Form1 の挙動）
        var keep = myKey.GetValue(keepFlagName);
        var keepOn = keep switch { int i => i != 0, string t => t == "1", _ => false };
        if (!keepOn) return null;

        if (myKey.GetValue(valueName) is not byte[] blob || blob.Length == 0) return null;

        try
        {
            // 旧版 EncryptMyPasswordDpapi は entropy なし・CurrentUser（AppSettings.cs）
            var plain = System.Security.Cryptography.ProtectedData.Unprotect(
                blob, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            var password = System.Text.Encoding.UTF8.GetString(plain);
            Array.Clear(plain, 0, plain.Length);
            return password.Length > 0 ? password : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null; // 別ユーザー・別 PC の blob
        }
    }

    // =====================================================================
    // 内部
    // =====================================================================

    private SecretVault.VaultSession? TryGetSession()
    {
        if (_session != null) return _session;

        var vault = SecretVault.TryLoad(_vaultPath);
        if (vault == null) return null;

        _session = vault.TryUnlockWithDevice(_keychain);
        return _session;
    }
}
