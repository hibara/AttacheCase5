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
// 記憶パスワードなどの「秘密」を保存するファイル（_AtcVault.json）。
//
// 設計は「記憶パスワードのポータブル運用・設計申し送り」（2026-09-08）に従う。
// 要点:
//
//   秘密（payload）── AES-256-GCM ──> DEK（32B 乱数・一度作ったら変えない）
//                                      ▲
//        DEK は複数の「プロテクター」でラップされる:
//          - device … 端末束縛。OS の鍵ストア（IKeychainService）が守る
//                      32B の DPK で DEK をラップする。ファイルを持ち出されても、
//                      同じ端末の別ユーザーでも開けない
//          - code   … 移行コード（16 文字 Crockford Base32 = 80bit、
//                      Argon2id m=64MB t=3 p=4、期限つき・使い捨て）。
//                      新しい端末で 1 回だけ入力して登録する
//
// 旧 Windows 版の INI 記憶パスワード（マシン名・ボリュームシリアル等を PBKDF2 に
// かける方式）は、材料が秘密でないため何も守れていなかった（申し送り §9-8）。
// この実装はそれを置き換える。
//
// 不変条件（申し送りの太字箇所）:
//   - すべての AEAD に AAD を付ける（形式版数・プロテクター id・期限のすり替え検知）
//   - ラベル・作成日時（meta）は DEK で暗号化する（信頼済み端末一覧を漏らさない）
//   - 移行コードは使った時点で必ず消す。「消す」と「足す」は 1 回の書き込みで行う
//   - 開けない device プロテクターを自動削除しない（他人が起動しただけで消える事故）
//   - 期限切れの code プロテクターだけは、device で開けたときに自動削除してよい
//
// 未実装（設計どおり段階導入。次版以降）:
//   - recovery プロテクター（26 文字・自動削除しない回復用コード）
//   - 再鍵化（DEK を作り直す本当の失効）。v1 では「すべて消去して作り直し」で代替
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using AttacheCase.Crypto;
using AttacheCase.Services;

namespace AttacheCase.Settings;

/// <summary>プロテクターの一覧表示用（meta は復号済み）。</summary>
public sealed record VaultProtectorInfo(
    string Id, string Type, string Label, string CreatedAt, string? ExpiresAt);

public sealed class SecretVault
{
    /// <summary>形式識別子。AAD の先頭に必ず入る。</summary>
    public const string FormatId = "atcvault/v1";

    /// <summary>ファイル名。設定ファイル（_AtcCase.json）とは別に置く（申し送り §8）。</summary>
    public const string FileName = "_AtcVault.json";

    /// <summary>DPK を保存する鍵ストアのサービス名。account はプロテクター id。</summary>
    public const string KeychainService = "AttacheCase.Vault";

    /// <summary>移行コードの既定の有効期限（申し送り §6.5 推奨値）。</summary>
    public static readonly TimeSpan DefaultCodeLifetime = TimeSpan.FromDays(7);

    // Argon2id パラメータ（申し送り §3。v150 本体と同じ値だが、独立に固定する）
    private const int KdfMemoryKb = 65536; // 64 MB
    private const int KdfIterations = 3;
    private const int KdfParallelism = 4;

    private const int DekSize = 32;
    private const int NonceSize = 12;
    private const int IdSize = 16;
    private const int SaltSize = 16;
    private const int CodeBytes = 10; // 80 bit → Crockford Base32 で 16 文字

    private readonly string _path;
    private VaultModel _model;

    private SecretVault(string path, VaultModel model)
    {
        _path = path;
        _model = model;
    }

    public string FilePath => _path;

    // =====================================================================
    // 読み込み・新規作成
    // =====================================================================

    /// <summary>ファイルがあれば読み込む。無い・壊れている場合は null。</summary>
    public static SecretVault? TryLoad(string path)
    {
        var model = TryReadModel(path);
        return model == null ? null : new SecretVault(path, model);
    }

    private static VaultModel? TryReadModel(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var model = JsonSerializer.Deserialize<VaultModel>(File.ReadAllText(path));
            if (model == null || model.Format != FormatId) return null;
            if (model.Payload == null || model.Protectors == null) return null;

            return model;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 書き換えの直前に、ディスク上の最新内容をメモリへ取り込む。
    ///
    /// _model はプロセス寿命の間保持される（RememberedPasswordService がセッションを
    /// 持ち続ける）ため、その間に別のプロセス・別の PC（USB 上のポータブル保管庫）が
    /// 加えた変更──たとえば移行コードで登録された新しい端末の device プロテクター──を
    /// 知らないまま SaveAtomically すると、その変更を上書きで消してしまう。
    /// 読み直してから書けば、この「古いモデルの書き戻し」はほぼ防げる
    ///（読み直しと保存の間のごく短い競合は残るが、窓がプロセス寿命から
    /// ミリ秒単位に縮む）。
    ///
    /// false を返したら書き換えを中止すること:
    ///   - ファイルが読めない（消された・壊れた・アクセス不能）。手元のモデルで
    ///     書き戻すと、別プロセスの DeleteAll で消したはずの保管庫が蘇る
    ///   - 読めたが payload がこのセッションの DEK で復号できない。別プロセスが
    ///     消して作り直した「別の DEK の保管庫」であり、この上に書くと
    ///     プロテクターと payload の鍵が食い違った開けないファイルになる
    /// </summary>
    private bool TryRefreshFromDisk(byte[] dek)
    {
        var fresh = TryReadModel(_path);
        if (fresh == null) return false;

        if (TryDecryptPayload(fresh.Payload!, dek) == null) return false;

        _model = fresh;
        return true;
    }

    // =====================================================================
    // プロセス間の排他
    // =====================================================================

    /// <summary>
    /// 同じ保管庫ファイルへの「読み直し→書き込み」をプロセスをまたいで直列化する。
    ///
    /// TryRefreshFromDisk は競合の窓をミリ秒単位に縮めるだけで、GUI と CLI
    /// （や二重起動）が同時に書くと、読み直しと保存の間に割り込まれた側が
    /// 相手の変更を上書きで消せてしまう。書き込みを伴う操作は必ずこのロックの
    /// 中で「読み直し→変更→保存」まで通しで行うこと
    /// （RememberedPasswordService.DeleteAll のファイル削除も同様）。
    ///
    /// 名前付き Mutex なので効くのは同一マシン内だけ。USB 上の保管庫を 2 台の
    /// PC から同時に書く競合はロックできないが、物理的に同時には挿せない前提で
    /// 許容する（そちらは従来どおり TryRefreshFromDisk が窓を縮める）。
    /// </summary>
    public static IDisposable AcquireExclusiveLock(string path)
    {
        // Mutex 名にパス区切り文字は使えないため、正規化したフルパスの
        // ハッシュを名前にする。Windows はパスの大小文字を区別しないので揃える
        var normalized = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            normalized = normalized.ToUpperInvariant();
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        var mutex = new Mutex(initiallyOwned: false, "AttacheCase.Vault." + hash);
        try
        {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // 前の持ち主がロック中に異常終了した。ロック自体は取得できている。
            // 書き込みは SaveAtomically（一時ファイル→rename）なので中途半端な
            // ファイルは残っておらず、そのまま続けてよい
        }
        catch (Exception)
        {
            mutex.Dispose();
            throw;
        }
        return new MutexReleaser(mutex);
    }

    private IDisposable AcquireExclusive() => AcquireExclusiveLock(_path);

    private sealed class MutexReleaser : IDisposable
    {
        private Mutex? _mutex;
        internal MutexReleaser(Mutex mutex) => _mutex = mutex;

        public void Dispose()
        {
            var m = Interlocked.Exchange(ref _mutex, null);
            if (m == null) return;
            try { m.ReleaseMutex(); }
            finally { m.Dispose(); }
        }
    }

    /// <summary>
    /// 新しい保管庫を作る。DEK を生成し、この端末の device プロテクターと
    /// 空の payload を書き込んだうえで、開いたセッションを返す。
    /// </summary>
    public static VaultSession CreateNew(string path, IKeychainService keychain, string deviceLabel)
    {
        using var _ = AcquireExclusiveLock(path);

        // 呼び出し元の「無いから作る」判断からここまでの間に、別プロセスが
        // 先に作っていたら上書きしない（相手の秘密とプロテクターが失われる）
        if (File.Exists(path))
        {
            throw new InvalidOperationException("保管庫は別のプロセスが先に作成しています。");
        }

        var dek = CryptoHelper5.GenerateRandomBytes(DekSize);
        var model = new VaultModel
        {
            Format = FormatId,
            Payload = EncryptPayload(new Dictionary<string, string>(), dek),
            Protectors = new List<ProtectorModel>(),
        };

        var vault = new SecretVault(path, model);
        var protector = vault.BuildDeviceProtector(dek, keychain, deviceLabel);
        model.Protectors.Add(protector);
        try
        {
            vault.SaveAtomically();
        }
        catch (Exception)
        {
            // BuildDeviceProtector が鍵ストアへ書いた DPK を孤立させない。
            // 削除の失敗で元の保存例外を隠さない（AddThisDevice と同じ扱い）
            if (protector.Id != null)
            {
                try { keychain.Delete(KeychainService, protector.Id); }
                catch (Exception) { /* 32B の乱数が残るだけで実害は無い */ }
            }
            throw;
        }

        return new VaultSession(vault, dek);
    }

    // =====================================================================
    // 開錠
    // =====================================================================

    /// <summary>
    /// この端末の device プロテクターで開く（平常時・無入力の経路）。
    /// 開けたついでに、期限切れの code プロテクターを削除する（§6.5）。
    /// 開けない device プロテクターは削除しない（§7.1）。
    /// </summary>
    public VaultSession? TryUnlockWithDevice(IKeychainService keychain)
    {
        foreach (var p in _model.Protectors.Where(p => p.Type == "device"))
        {
            var dek = TryUnwrapDevice(p, keychain);
            if (dek == null) continue;

            // 期限切れの移行コードを掃除する。削除だけの書き込みなので、
            // 失敗しても開錠自体は成立させる
            if (_model.Protectors.Any(c => c.Type == "code" && IsExpired(c)))
            {
                try
                {
                    using var _ = AcquireExclusive();
                    // ロックの外で読んだモデルのまま書き戻さない（他プロセスの
                    // 変更を消してしまう）。読み直せなければ掃除は見送る
                    if (TryRefreshFromDisk(dek))
                    {
                        _model.Protectors!.RemoveAll(c => c.Type == "code" && IsExpired(c));
                        SaveAtomically();
                    }
                }
                catch (Exception) { /* 次回開錠時に再試行される */ }
            }

            return new VaultSession(this, dek);
        }

        return null;
    }

    /// <summary>この端末で（入力なしで）開けるか。</summary>
    public bool CanUnlockWithDevice(IKeychainService keychain)
    {
        var session = TryUnlockWithDevice(keychain);
        if (session == null) return false;
        session.Dispose();
        return true;
    }

    /// <summary>有効期限内の code プロテクターがあるか（=移行コードで開ける状態か）。</summary>
    public bool HasActiveCodeProtector()
        => _model.Protectors.Any(p => p.Type == "code" && !IsExpired(p));

    /// <summary>
    /// プロテクターの id と種別（平文部分のみ）。開錠は要らない。
    /// 削除時に鍵ストアの DPK を掃除する用途（ラベル等は開錠しないと読めない）。
    /// </summary>
    public IReadOnlyList<(string Id, string Type)> ListProtectorIds()
        => _model.Protectors
            .Where(p => p.Id != null && p.Type != null)
            .Select(p => (p.Id!, p.Type!))
            .ToList();

    /// <summary>
    /// 移行コードで開く。成功したら code プロテクターは必ず削除し、
    /// registerThisDevice のときはこの端末の device プロテクターを足して、
    /// 【1 回の書き込みで】保存する（§6.3 / §6.4。別々に書くと、途中で落ちた
    /// ときにどの入口も無いファイルが残り、秘密を永久に失う）。
    /// </summary>
    public VaultSession? TryUnlockWithCode(
        string code, IKeychainService keychain, bool registerThisDevice, string deviceLabel)
    {
        var canonical = MigrationCode.Normalize(code);
        if (canonical == null) return null;

        // ここは「読む→コードを消す→保存」まで通しで排他する。
        // 別プロセスの書き込みが割り込むと、相手の変更を上書きで消してしまう
        using var _ = AcquireExclusive();

        // 最新のディスク内容で照合する（DEK はこれから取り出すので、ここでは
        // 読めることだけを確かめる。読めなければ開けるものが無い）
        var fresh = TryReadModel(_path);
        if (fresh == null) return null;
        _model = fresh;

        foreach (var p in _model.Protectors.Where(p => p.Type == "code").ToList())
        {
            if (IsExpired(p)) continue;

            var dek = TryUnwrapCode(p, canonical);
            if (dek == null) continue;

            // 使い捨て: 登録の有無にかかわらず code プロテクターを消す
            var snapshot = new List<ProtectorModel>(_model.Protectors);
            _model.Protectors.Remove(p);
            ProtectorModel? added = null;
            if (registerThisDevice)
            {
                added = BuildDeviceProtector(dek, keychain, deviceLabel);
                _model.Protectors.Add(added);
            }

            try
            {
                SaveAtomically();
            }
            catch (Exception)
            {
                // 保存できなければ「何も無かったこと」に巻き戻す。
                // ディスク上はコードが健在なので、失敗として返せば後で再試行できる。
                // メモリだけコードが消えた状態や、鍵ストアに孤立した DPK を残さない
                _model.Protectors = snapshot;
                if (added?.Id != null)
                {
                    try { keychain.Delete(KeychainService, added.Id); }
                    catch (Exception) { /* 消せなくても実害は無い（32B の乱数が残るだけ） */ }
                }
                Array.Clear(dek, 0, dek.Length);
                return null;
            }

            return new VaultSession(this, dek);
        }

        return null;
    }

    // =====================================================================
    // 内部: プロテクターの構築・開封
    // =====================================================================

    /// <summary>
    /// この端末の device プロテクターを作る。
    ///
    /// 32B 乱数の DPK を鍵ストア（Windows: DPAPI ファイル / macOS: キーチェーン）に
    /// 保存し、DEK は DPK の AES-GCM でラップする。申し送り §5.2 は Windows で
    /// DEK を DPAPI 直ラップしてもよいとするが、全 OS を同じ経路にそろえる。
    /// DPAPI 側のエントロピー束縛（blob の移し替え防止）は DpapiSecretStore が
    /// (service, account=id) で行っており、要件を満たす。
    /// </summary>
    private ProtectorModel BuildDeviceProtector(byte[] dek, IKeychainService keychain, string label)
    {
        var id = CryptoHelper5.GenerateRandomBytes(IdSize);
        var idB64 = Convert.ToBase64String(id);

        var dpk = CryptoHelper5.GenerateRandomBytes(DekSize);
        keychain.Set(KeychainService, idB64, dpk);

        var nonce = CryptoHelper5.GenerateRandomBytes(NonceSize);
        var aad = Encoding.UTF8.GetBytes(FormatId + "|device|" + idB64);
        var wrapped = CryptoHelper5.AesGcmEncrypt(dek, dpk, nonce, aad);

        return new ProtectorModel
        {
            Id = idB64,
            Type = "device",
            Nonce = Convert.ToBase64String(nonce),
            Wrapped = Convert.ToBase64String(wrapped),
            Meta = EncryptMeta(idB64, label, dek),
        };
    }

    private static byte[]? TryUnwrapDevice(ProtectorModel p, IKeychainService keychain)
    {
        try
        {
            if (p.Id == null || p.Nonce == null || p.Wrapped == null) return null;
            if (!keychain.TryGet(KeychainService, p.Id, out var dpk) || dpk == null) return null;

            var aad = Encoding.UTF8.GetBytes(FormatId + "|device|" + p.Id);
            return CryptoHelper5.AesGcmDecrypt(
                Convert.FromBase64String(p.Wrapped), dpk, Convert.FromBase64String(p.Nonce), aad);
        }
        catch (Exception)
        {
            // 鍵ストアに項目が無い・別端末の項目・改ざん。どれも「この端末のものではない」
            return null;
        }
    }

    private static byte[]? TryUnwrapCode(ProtectorModel p, string canonicalCode)
    {
        try
        {
            if (p.Id == null || p.Salt == null || p.Nonce == null ||
                p.Wrapped == null || p.Kdf == null || p.ExpiresAt == null)
            {
                return null;
            }

            var kek = CryptoHelper5.DeriveKeyFromPassword(
                canonicalCode, Convert.FromBase64String(p.Salt),
                p.Kdf.M, p.Kdf.T, p.Kdf.P);

            // AAD は「ファイルに書かれている文字列そのまま」から組み立てる。
            // 期限や KDF パラメータを書き換えると、ここで認証タグが合わなくなる
            var aad = Encoding.UTF8.GetBytes(CodeAad(p.Id, p.ExpiresAt, p.Kdf));
            return CryptoHelper5.AesGcmDecrypt(
                Convert.FromBase64String(p.Wrapped), kek, Convert.FromBase64String(p.Nonce), aad);
        }
        catch (Exception)
        {
            return null; // コード違い・改ざん
        }
    }

    private static string CodeAad(string idB64, string expiresAt, KdfModel kdf)
        => FormatId + "|code|" + idB64 + "|" + expiresAt +
           "|" + kdf.Alg + ",m=" + kdf.M + ",t=" + kdf.T + ",p=" + kdf.P;

    private static bool IsExpired(ProtectorModel p)
    {
        if (p.ExpiresAt == null) return true;
        if (!DateTimeOffset.TryParse(
                p.ExpiresAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
        {
            return true; // 期限が読めないコードは無効扱い（安全側）
        }
        return t < DateTimeOffset.UtcNow;
    }

    // =====================================================================
    // 内部: payload / meta の暗号化（すべて AAD 付き）
    // =====================================================================

    private static EncBlobModel EncryptPayload(Dictionary<string, string> secrets, byte[] dek)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(secrets);
        var nonce = CryptoHelper5.GenerateRandomBytes(NonceSize);
        var aad = Encoding.UTF8.GetBytes(FormatId + "|payload");
        var data = CryptoHelper5.AesGcmEncrypt(plain, dek, nonce, aad);
        Array.Clear(plain, 0, plain.Length);
        return new EncBlobModel
        {
            Nonce = Convert.ToBase64String(nonce),
            Data = Convert.ToBase64String(data),
        };
    }

    private static Dictionary<string, string>? TryDecryptPayload(EncBlobModel payload, byte[] dek)
    {
        try
        {
            if (payload.Nonce == null || payload.Data == null) return null;
            var aad = Encoding.UTF8.GetBytes(FormatId + "|payload");
            var plain = CryptoHelper5.AesGcmDecrypt(
                Convert.FromBase64String(payload.Data), dek,
                Convert.FromBase64String(payload.Nonce), aad);
            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(plain);
            Array.Clear(plain, 0, plain.Length);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// ラベルと作成日時。平文で置くと、ファイルを拾った人に「信頼済み環境の一覧」を
    /// 渡してしまうため、必ず DEK で暗号化する（申し送り §5.1）。
    /// </summary>
    private static EncBlobModel EncryptMeta(string idB64, string label, byte[] dek)
    {
        var meta = new Dictionary<string, string>
        {
            ["label"] = label,
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        var plain = JsonSerializer.SerializeToUtf8Bytes(meta);
        var nonce = CryptoHelper5.GenerateRandomBytes(NonceSize);
        var aad = Encoding.UTF8.GetBytes(FormatId + "|meta|" + idB64);
        var data = CryptoHelper5.AesGcmEncrypt(plain, dek, nonce, aad);
        return new EncBlobModel
        {
            Nonce = Convert.ToBase64String(nonce),
            Data = Convert.ToBase64String(data),
        };
    }

    private static (string Label, string CreatedAt) DecryptMetaOrDefault(
        ProtectorModel p, byte[] dek)
    {
        try
        {
            if (p.Meta?.Nonce == null || p.Meta.Data == null || p.Id == null) return ("", "");
            var aad = Encoding.UTF8.GetBytes(FormatId + "|meta|" + p.Id);
            var plain = CryptoHelper5.AesGcmDecrypt(
                Convert.FromBase64String(p.Meta.Data), dek,
                Convert.FromBase64String(p.Meta.Nonce), aad);
            var meta = JsonSerializer.Deserialize<Dictionary<string, string>>(plain);
            return (meta?.GetValueOrDefault("label") ?? "",
                    meta?.GetValueOrDefault("createdAt") ?? "");
        }
        catch (Exception)
        {
            return ("", "");
        }
    }

    // =====================================================================
    // 内部: 保存
    // =====================================================================

    /// <summary>
    /// 一時ファイルへ書いてから原子的に置き換える（§6.4）。
    /// 書き込み途中のクラッシュで「どの入口も無いファイル」を残さないため。
    /// </summary>
    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
        // device プロテクターに code 用の null 項目（salt / kdf / expiresAt）を
        // 並べない。サポートで「ファイルを見せてください」と言ったときに読みやすく
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private void SaveAtomically()
    {
        var json = JsonSerializer.Serialize(_model, SaveOptions);
        var tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    // =====================================================================
    // 開いた状態のセッション
    // =====================================================================

    public sealed class VaultSession : IDisposable
    {
        private readonly SecretVault _vault;
        private readonly byte[] _dek;
        private bool _disposed;

        internal VaultSession(SecretVault vault, byte[] dek)
        {
            _vault = vault;
            _dek = dek;
        }

        public SecretVault Vault => _vault;

        /// <summary>秘密の一覧を復号して返す。復号できない場合は null（黙って空にしない）。</summary>
        public Dictionary<string, string>? GetSecrets()
        {
            ThrowIfDisposed();
            return TryDecryptPayload(_vault._model.Payload!, _dek);
        }

        /// <summary>秘密を書き換える。DEK はそのまま、payload だけ暗号化し直す（§7.3 末尾）。</summary>
        public void SetSecrets(Dictionary<string, string> secrets)
        {
            ThrowIfDisposed();
            using var _ = _vault.AcquireExclusive();
            if (_vault.TryRefreshFromDisk(_dek) == false)
            {
                // ディスク上の保管庫が消された・壊れた・別の DEK の物に置き換わった。
                // この上に書くと消したはずの保管庫が蘇るか、開けないファイルになる
                throw new InvalidOperationException(
                    "保管庫がディスク上で消されたか、別の保管庫に置き換えられています。");
            }
            var previous = _vault._model.Payload;
            _vault._model.Payload = EncryptPayload(secrets, _dek);
            try
            {
                _vault.SaveAtomically();
            }
            catch (Exception)
            {
                _vault._model.Payload = previous; // メモリとディスクをずらさない
                throw;
            }
        }

        /// <summary>この端末の device プロテクターを追加する。</summary>
        public void AddThisDevice(IKeychainService keychain, string label)
        {
            ThrowIfDisposed();
            using var _ = _vault.AcquireExclusive();
            if (_vault.TryRefreshFromDisk(_dek) == false)
            {
                // ディスク上の保管庫が消された・壊れた・別の DEK の物に置き換わった。
                // この上に書くと消したはずの保管庫が蘇るか、開けないファイルになる
                throw new InvalidOperationException(
                    "保管庫がディスク上で消されたか、別の保管庫に置き換えられています。");
            }
            var added = _vault.BuildDeviceProtector(_dek, keychain, label);
            _vault._model.Protectors!.Add(added);
            try
            {
                _vault.SaveAtomically();
            }
            catch (Exception)
            {
                // 巻き戻し: 一覧から外し、作ったばかりの DPK も鍵ストアから消す
                _vault._model.Protectors!.Remove(added);
                if (added.Id != null)
                {
                    try { keychain.Delete(KeychainService, added.Id); }
                    catch (Exception) { /* 消せなくても実害は無い */ }
                }
                throw;
            }
        }

        /// <summary>
        /// 移行コードを発行する。既存の code プロテクターは置き換える
        /// （2 本同時には存在させない。§7.2）。返り値は表示用（XXXX-XXXX-XXXX-XXXX）。
        /// </summary>
        public string IssueMigrationCode(TimeSpan? lifetime = null)
        {
            ThrowIfDisposed();

            var codeBytes = CryptoHelper5.GenerateRandomBytes(CodeBytes);
            var canonical = MigrationCode.Encode(codeBytes);

            var id = CryptoHelper5.GenerateRandomBytes(IdSize);
            var idB64 = Convert.ToBase64String(id);
            var salt = CryptoHelper5.GenerateRandomBytes(SaltSize);
            var kdf = new KdfModel { Alg = "argon2id", M = KdfMemoryKb, T = KdfIterations, P = KdfParallelism };
            var expiresAt = (DateTimeOffset.UtcNow + (lifetime ?? DefaultCodeLifetime))
                .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

            var kek = CryptoHelper5.DeriveKeyFromPassword(canonical, salt, kdf.M, kdf.T, kdf.P);
            var nonce = CryptoHelper5.GenerateRandomBytes(NonceSize);
            var aad = Encoding.UTF8.GetBytes(CodeAad(idB64, expiresAt, kdf));
            var wrapped = CryptoHelper5.AesGcmEncrypt(_dek, kek, nonce, aad);

            // KDF（Argon2）はロックの外で済ませ、保持時間を短くする
            using var _ = _vault.AcquireExclusive();
            if (_vault.TryRefreshFromDisk(_dek) == false)
            {
                // ディスク上の保管庫が消された・壊れた・別の DEK の物に置き換わった。
                // この上に書くと消したはずの保管庫が蘇るか、開けないファイルになる
                throw new InvalidOperationException(
                    "保管庫がディスク上で消されたか、別の保管庫に置き換えられています。");
            }
            var protectors = _vault._model.Protectors!;
            var snapshot = new List<ProtectorModel>(protectors);
            protectors.RemoveAll(p => p.Type == "code");
            protectors.Add(new ProtectorModel
            {
                Id = idB64,
                Type = "code",
                Salt = Convert.ToBase64String(salt),
                Kdf = kdf,
                ExpiresAt = expiresAt,
                Nonce = Convert.ToBase64String(nonce),
                Wrapped = Convert.ToBase64String(wrapped),
                Meta = EncryptMeta(idB64, "migration code", _dek),
            });
            try
            {
                _vault.SaveAtomically();
            }
            catch (Exception)
            {
                _vault._model.Protectors = snapshot; // メモリとディスクをずらさない
                throw;
            }

            return MigrationCode.FormatForDisplay(canonical);
        }

        /// <summary>プロテクターの一覧（meta は復号して返す）。管理画面用。</summary>
        public IReadOnlyList<VaultProtectorInfo> ListProtectors()
        {
            ThrowIfDisposed();
            return _vault._model.Protectors!
                .Where(p => p.Id != null && p.Type != null)
                .Select(p =>
                {
                    var (label, createdAt) = DecryptMetaOrDefault(p, _dek);
                    return new VaultProtectorInfo(p.Id!, p.Type!, label, createdAt, p.ExpiresAt);
                })
                .ToList();
        }

        /// <summary>
        /// プロテクターを一覧から消す（軽い解除。§7.3 (i)）。
        /// その端末が過去に DEK を取り出せていた事実は消えない。
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// 最後の device プロテクターを消そうとしたとき。消してしまうと
        /// 「どの入口も無いファイル」だけが残り、秘密を永久に失う。
        /// 全部やめるときは RememberedPasswordService.DeleteAll（ファイルごと削除）を使う。
        /// </exception>
        public bool RemoveProtector(string id)
        {
            ThrowIfDisposed();
            using var _ = _vault.AcquireExclusive();
            if (_vault.TryRefreshFromDisk(_dek) == false)
            {
                // ディスク上の保管庫が消された・壊れた・別の DEK の物に置き換わった。
                // この上に書くと消したはずの保管庫が蘇るか、開けないファイルになる
                throw new InvalidOperationException(
                    "保管庫がディスク上で消されたか、別の保管庫に置き換えられています。");
            }
            var protectors = _vault._model.Protectors!;
            var target = protectors.FirstOrDefault(p => p.Id == id);
            if (target == null) return false;

            if (target.Type == "device" && protectors.Count(p => p.Type == "device") <= 1)
            {
                throw new InvalidOperationException(
                    "最後の device プロテクターは削除できません（誰も開けない保管庫が残る）。");
            }

            // 鍵ストアの DPK はここでは消さない。ポータブル複製（CreatePortableCopy）と
            // 通常側の保管庫は同じプロテクター id を共有するため、片方の一覧から外した
            // だけで DPK を消すと、もう片方まで開けなくなる。孤立した DPK は
            // 32B の乱数が鍵ストアに残るだけで実害は無い
            protectors.Remove(target);
            try
            {
                _vault.SaveAtomically();
            }
            catch (Exception)
            {
                protectors.Add(target); // メモリとディスクをずらさない
                throw;
            }
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Array.Clear(_dek, 0, _dek.Length);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    // =====================================================================
    // JSON モデル（申し送り §8 の形をそのまま使う）
    // =====================================================================

    private sealed class VaultModel
    {
        [JsonPropertyName("format")] public string? Format { get; set; }
        [JsonPropertyName("payload")] public EncBlobModel? Payload { get; set; }
        [JsonPropertyName("protectors")] public List<ProtectorModel>? Protectors { get; set; }
    }

    private sealed class EncBlobModel
    {
        [JsonPropertyName("nonce")] public string? Nonce { get; set; }
        [JsonPropertyName("data")] public string? Data { get; set; }
    }

    private sealed class ProtectorModel
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("salt")] public string? Salt { get; set; }
        [JsonPropertyName("kdf")] public KdfModel? Kdf { get; set; }
        [JsonPropertyName("expiresAt")] public string? ExpiresAt { get; set; }
        [JsonPropertyName("nonce")] public string? Nonce { get; set; }
        [JsonPropertyName("wrapped")] public string? Wrapped { get; set; }
        [JsonPropertyName("meta")] public EncBlobModel? Meta { get; set; }
    }

    private sealed class KdfModel
    {
        [JsonPropertyName("alg")] public string Alg { get; set; } = "argon2id";
        [JsonPropertyName("m")] public int M { get; set; }
        [JsonPropertyName("t")] public int T { get; set; }
        [JsonPropertyName("p")] public int P { get; set; }
    }
}

/// <summary>
/// 移行コード（Crockford Base32）。
/// I / L / O / U を含まないので、1↔I・0↔O の書き写し間違いが起きない。
/// 入力は大文字小文字を区別せず、ハイフン・空白は無視し、O→0・I/L→1 に読み替える。
/// </summary>
public static class MigrationCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int CanonicalLength = 16; // 80 bit

    /// <summary>10 バイト（80 bit）を 16 文字の正規形に符号化する。</summary>
    public static string Encode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length != 10) throw new ArgumentException("移行コードは 10 バイト（80 bit）", nameof(bytes));

        var sb = new StringBuilder(CanonicalLength);
        var buffer = 0;
        var bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(buffer >> bits) & 0x1F]);
            }
        }
        return sb.ToString();
    }

    /// <summary>表示用に 4 文字ごとにハイフンを入れる（XXXX-XXXX-XXXX-XXXX）。</summary>
    public static string FormatForDisplay(string canonical)
        => string.Join("-", Enumerable.Range(0, 4).Select(i => canonical.Substring(i * 4, 4)));

    /// <summary>
    /// 利用者の入力を正規形（16 文字・大文字・区切りなし）へ。読めない入力は null。
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var sb = new StringBuilder(CanonicalLength);
        foreach (var raw in input)
        {
            var c = char.ToUpperInvariant(raw);
            if (c is '-' or ' ') continue;
            c = c switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => c,
            };
            if (Alphabet.IndexOf(c) < 0) return null;
            sb.Append(c);
        }

        return sb.Length == CanonicalLength ? sb.ToString() : null;
    }
}
