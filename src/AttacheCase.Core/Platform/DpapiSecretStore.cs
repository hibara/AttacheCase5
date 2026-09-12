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
// Windows 用の秘密情報ストア。
//
// macOS 版の KeychainService と同じ IKeychainService 契約を、
// DPAPI（ProtectedData, CurrentUser スコープ）+ ファイル保存で実現する。
// Post-PPAP の秘密鍵・デバイストークンなどがここを通る。
//
// 保存先: %APPDATA%\AttacheCase\Secrets\<SHA-256(service + "\n" + account) の hex>.bin
//   - ファイル名から中身を推測できないようハッシュ名にする
//   - DPAPI の optionalEntropy に (service, account) を結び付け、
//     別の項目の blob を差し替えられても復号できないようにする
// DPAPI はこの PC・このユーザーに紐づくため、ファイルを他所へコピーしても開けない。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AttacheCase.Platform;

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : Services.IKeychainService
{
    private readonly string _directory;

    public DpapiSecretStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AttacheCase", "Secrets");
    }

    public bool TryGet(string service, string account, out byte[]? data)
    {
        data = null;
        var path = PathFor(service, account);

        // 旧形式（区切り文字連結のハッシュ名）からの読み替え。
        // 新形式に無ければ旧名を見て、読めたら新名へ改名しておく
        if (!File.Exists(path))
        {
            var legacy = LegacyPathFor(service, account);
            if (!File.Exists(legacy)) return false;

            try { File.Move(legacy, path); }
            catch (Exception) { path = legacy; /* 改名できなくても読めれば良い */ }
        }

        try
        {
            var blob = File.ReadAllBytes(path);
            data = ProtectedData.Unprotect(blob, EntropyFor(service, account), DataProtectionScope.CurrentUser);
            return true;
        }
        catch (CryptographicException)
        {
            // 別ユーザー・別 PC の blob、または壊れた blob。「無い」として扱う
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // UnauthorizedAccessException は IOException を継承しない
            //（ACL・読み取り専用属性で現実に起こる）。「無い」として扱う
            return false;
        }
    }

    public void Set(string service, string account, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Directory.CreateDirectory(_directory);

        var blob = ProtectedData.Protect(data, EntropyFor(service, account), DataProtectionScope.CurrentUser);

        // 書き込み途中のクラッシュで既存の秘密を壊さないよう、一時ファイル経由で置き換える
        var path = PathFor(service, account);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tempPath, blob);
        File.Move(tempPath, path, overwrite: true);

        // 旧名が残っていると Delete 後の読み取りで古い値が蘇るので、ここで消しておく
        try
        {
            var legacy = LegacyPathFor(service, account);
            if (File.Exists(legacy)) File.Delete(legacy);
        }
        catch (Exception) { /* 消せなくても新名が優先されるので害は小さい */ }
    }

    public bool Delete(string service, string account)
    {
        var deleted = false;
        foreach (var path in new[] { PathFor(service, account), LegacyPathFor(service, account) })
        {
            if (!File.Exists(path)) continue;

            try
            {
                File.Delete(path);
                deleted = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 消せない側は残るが、戻り値は「1 つも消せなかったか」で判断する
            }
        }
        return deleted;
    }

    /// <summary>
    /// 保存ファイル名。識別子は長さプレフィックス付きで符号化してからハッシュする。
    /// 単純な区切り文字連結だと ("a\nb","c") と ("a","b\nc") が同じ名前になり、
    /// 片方の保存がもう片方を上書きして復号できなくする（現在の呼び出し元は
    /// 定数と Base64 のみで衝突しないが、契約として塞いでおく）。
    /// </summary>
    private string PathFor(string service, string account)
    {
        var s = Encoding.UTF8.GetBytes(service);
        var a = Encoding.UTF8.GetBytes(account);
        var buf = new byte[8 + s.Length + a.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf, s.Length);
        s.CopyTo(buf, 4);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4 + s.Length), a.Length);
        a.CopyTo(buf, 8 + s.Length);

        var hash = SHA256.HashData(buf);
        return Path.Combine(_directory, Convert.ToHexString(hash) + ".bin");
    }

    /// <summary>旧形式のファイル名（読み替えと掃除のためだけに残す）。</summary>
    private string LegacyPathFor(string service, string account)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(service + "\n" + account));
        return Path.Combine(_directory, Convert.ToHexString(hash) + ".bin");
    }

    private static byte[] EntropyFor(string service, string account)
        => Encoding.UTF8.GetBytes(service + "\0" + account);
}
