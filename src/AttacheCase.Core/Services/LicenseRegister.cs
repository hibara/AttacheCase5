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
// 商用ライセンス（レジストレーションコード）の検証と保存。
// Windows 版 AttacheCase\LicenseRegister.cs のクロスプラットフォーム移植。
//
// コードの形式: RSA で暗号化されたブロブの 16 進文字列（改行は無視）。
// 復号すると "HIBARA\tユーザー名\tメールアドレス" になる。
//
// 保存先:
//   Windows: HKCU\Software\Hibara\AttacheCase5\Registration（旧版と同じ場所。
//            旧版と併用しても同じ登録が見える）+ AC3/AC4 からの移行
//   その他 : 設定ディレクトリの registration.code ファイル
//
// テストからは ILicenseCodeStore を注入して実環境を汚さない。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AttacheCase.Services;

/// <summary>レジストレーションコードの保存先。</summary>
public interface ILicenseCodeStore
{
    /// <summary>保存されているコード。無ければ空文字。</summary>
    string Read();

    void Write(string code);

    void Delete();
}

// 復号鍵 PrivateKey は別ファイル（partial）。本物はリポジトリ外の
// _secrets\LicenseRegister.Secret.cs、無ければ空鍵の雛形
// Services\LicenseRegister.Secret.cs.template がコンパイルされる（csproj 参照）。
public partial class LicenseRegister
{
    private const string ENCRYPTED_TOKEN = "HIBARA"; // Encrypted token.

    /// <summary>復号鍵が組み込まれているか（公開ソースからのビルドでは false）。</summary>
    internal static bool HasPrivateKey => !string.IsNullOrEmpty(PrivateKey);

    private readonly ILicenseCodeStore _store;

    /// <summary>復号されたユーザー名</summary>
    public string UserNameString { get; private set; } = "";

    /// <summary>復号されたメールアドレス</summary>
    public string EmailAddressString { get; private set; } = "";

    /// <summary>レジストレーションコード</summary>
    public string RegistrationCode { get; set; } = "";

    /// <summary>
    /// 引数指定がなければ保存先から読み込む（無ければ空文字のまま）。
    /// </summary>
    public LicenseRegister(string? registrationString = null, ILicenseCodeStore? store = null)
    {
        _store = store ?? CreateDefaultStore();
        RegistrationCode = registrationString ?? _store.Read();
    }

    public static ILicenseCodeStore CreateDefaultStore()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsRegistryLicenseCodeStore()
            : new FileLicenseCodeStore();
    }

    /// <summary>
    /// レジストレーションコードの検証。成功時はユーザー名・メールアドレスが入る。
    /// </summary>
    /// <param name="fRegister">true なら検証成功時に保存先へ書き込む</param>
    public bool Decrypt(bool fRegister = false)
    {
        if (string.IsNullOrEmpty(RegistrationCode))
        {
            RegistrationCode = _store.Read();
            if (string.IsNullOrEmpty(RegistrationCode)) return false;
        }

        // 鍵なしビルド（公開ソース）では検証できない＝常にフリー版
        if (!HasPrivateKey)
        {
            UserNameString = "";
            EmailAddressString = "";
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.FromXmlString(PrivateKey);

            // 16 進文字列をバイト配列に（改行は取り除いて一本に連結する）
            var data = FromHexString(RegistrationCode
                .Replace("\r", "").Replace("\n", ""));

            if (data == null)
            {
                UserNameString = "";
                EmailAddressString = "";
                return false;
            }

            // 旧版 RSACryptoServiceProvider.Decrypt(data, fOAEP: true) と同じ OAEP(SHA-1)
            var decryptedData = rsa.Decrypt(data, RSAEncryptionPadding.OaepSHA1);
            var str = Encoding.UTF8.GetString(decryptedData);
            var registrationData = str.Split('\t');

            if (registrationData.Length >= 3 && registrationData[0] == ENCRYPTED_TOKEN)
            {
                UserNameString = registrationData[1];
                EmailAddressString = registrationData[2];

                if (fRegister)
                {
                    _store.Write(RegistrationCode);
                }

                return true;
            }

            UserNameString = "";
            EmailAddressString = "";
            return false;
        }
        catch (CryptographicException)
        {
            UserNameString = "";
            EmailAddressString = "";
            return false;
        }
    }

    /// <summary>商用ライセンスを保存先から取得して検証する。</summary>
    public bool GetCommercialLicense() => Decrypt(fRegister: false);

    /// <summary>レジストレーションコードの削除。</summary>
    public bool DeleteLicense()
    {
        try
        {
            _store.Delete();
            RegistrationCode = "";
            UserNameString = "";
            EmailAddressString = "";
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 旧バージョン (AC3 / AC4) から AC5 へのレジストレーションコード移行（Windows のみ）。
    /// レジストレーションコードのみを扱い、それ以外の設定には触らない。
    /// </summary>
    /// <remarks>
    /// 配布済みの AC4 (〜ver.4.2.5.5) は誤って AC3 側のレジストリにコードを書く
    /// バグがあるため、AC3 にだけコードがあるユーザーの救済（AC4 への書き戻し）も行う。
    /// AC3 は共存しない仕様のため、コードがあれば最終的に削除する。AC4 は残す。
    ///   AC3 有 + AC4 有 → AC4 のコードを AC5 へ、AC3 削除
    ///   AC3 有 + AC4 無 → AC3 のコードを AC4 と AC5 両方へ、AC3 削除
    ///   AC3 無 + AC4 有 → AC4 のコードを AC5 へ
    ///   AC3 無 + AC4 無 → 何もしない
    /// </remarks>
    public static void MigrateLegacyRegistrationIfNeeded()
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowsRegistryLicenseCodeStore.MigrateLegacyRegistrationIfNeeded();
    }

    private static byte[]? FromHexString(string str)
    {
        try
        {
            var length = str.Length / 2;
            var bytes = new byte[length];
            var j = 0;
            for (var i = 0; i < length; i++)
            {
                bytes[i] = Convert.ToByte(str.Substring(j, 2), 16);
                j += 2;
            }

            return bytes;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Windows: 旧版と同じ HKCU\Software\Hibara\AttacheCase5\Registration に保存する。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryLicenseCodeStore : ILicenseCodeStore
{
    private const string AttacheCaseKey = @"Software\Hibara\AttacheCase5";
    private const string RegistryLicenseKey = @"Software\Hibara\AttacheCase5\Registration";

    // 旧バージョンのレジストリパス（レジストレーションコードの移行元としてのみ参照）
    private const string LegacyAttacheCase3Key = @"Software\Hibara\AttacheCase3";
    private const string LegacyAttacheCase3LicenseKey = @"Software\Hibara\AttacheCase3\Registration";
    private const string LegacyAttacheCase4LicenseKey = @"Software\Hibara\AttacheCase4\Registration";

    public string Read()
    {
        // 「読むだけ」に徹し、サブキーが無くても作らない（旧版のコメント参照:
        // 作ると AC4→AC5 一括移行のトリガー条件が早期に失われる）
        using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryLicenseKey, false);
        return reg?.GetValue("RegistrationCode", "") as string ?? "";
    }

    public void Write(string code)
    {
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryLicenseKey);
        using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryLicenseKey, true);
        reg?.SetValue("RegistrationCode", code);
    }

    public void Delete()
    {
        using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AttacheCaseKey, true);
        reg?.DeleteSubKey("Registration", false);
    }

    internal static void MigrateLegacyRegistrationIfNeeded()
    {
        string? code3;
        using (var reg3 = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(LegacyAttacheCase3LicenseKey, false))
        {
            code3 = reg3?.GetValue("RegistrationCode", "") as string;
        }

        string? code4;
        using (var reg4 = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(LegacyAttacheCase4LicenseKey, false))
        {
            code4 = reg4?.GetValue("RegistrationCode", "") as string;
        }

        if (!string.IsNullOrEmpty(code3))
        {
            if (!string.IsNullOrEmpty(code4))
            {
                // AC3 有 + AC4 有 → AC4 のコードを AC5 へコピー (AC3 はスキップ)
                new WindowsRegistryLicenseCodeStore().Write(code4);
            }
            else
            {
                // AC3 有 + AC4 無 → AC3 のコードを AC4 と AC5 両方へコピー
                // (配布済み AC4 のバグにより AC3 にだけコードがあるユーザー向け救済)
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(LegacyAttacheCase4LicenseKey);
                using (var reg4 = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(LegacyAttacheCase4LicenseKey, true))
                {
                    reg4?.SetValue("RegistrationCode", code3);
                }

                new WindowsRegistryLicenseCodeStore().Write(code3);
            }

            // AC3 のコードは共存不可仕様のため削除
            using var legacy3Root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(LegacyAttacheCase3Key, true);
            legacy3Root?.DeleteSubKey("Registration", false);
            return;
        }

        // AC3 無 + AC4 有 → AC4 のコードを AC5 へコピー
        if (!string.IsNullOrEmpty(code4))
        {
            new WindowsRegistryLicenseCodeStore().Write(code4);
        }
        // AC3 無 + AC4 無 → 何もしない
    }
}

/// <summary>
/// Windows 以外: 設定ディレクトリの registration.code ファイルに保存する。
/// </summary>
public sealed class FileLicenseCodeStore : ILicenseCodeStore
{
    private readonly string _path;

    public FileLicenseCodeStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Path.GetDirectoryName(SettingsService.Instance.FilePath) ?? ".",
            "registration.code");
    }

    public string Read()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path, Encoding.UTF8).Trim() : "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    public void Write(string code)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, code, Encoding.UTF8);
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
