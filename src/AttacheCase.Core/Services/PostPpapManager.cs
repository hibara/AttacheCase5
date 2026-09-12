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
// Post-PPAP（公開鍵交換）マネージャ（macOS 版）。
// Windows 版 AttacheCase.PostPpapManager のアルゴリズム互換実装で、
// JSON ライブラリと秘密鍵保護を以下のように置換している:
//   - Newtonsoft.Json    → System.Text.Json
//   - DPAPI ProtectedData → macOS Keychain（IKeychainService 経由）
//
// accounts.json はプラットフォーム固有のため Windows 版とは構造が
// 異なる（encryptedPrivateKey フィールドは持たず、Keychain に直接保存）。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Crypto;
using AttacheCase.Models;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Pqc.Crypto.Utilities;
using Org.BouncyCastle.Security;

namespace AttacheCase.Services;

public class PostPpapManager
{
    /// <summary>表示文字列（利用者に見えるメッセージは必ずここを通す）</summary>
    private static readonly LocalizationService Localize = LocalizationService.Instance;

    public const byte DefaultAlgorithmId = PkaAlgorithmId.Rsa4096MlKem768;
    private const int RsaKeySize = 4096;

    /// <summary>Keychain のサービス名（既定値）。テストでは差し替えられる</summary>
    public const string KeychainService = "org.hibara.AttacheCase.PostPPAP";
    private const string RsaAccountPrefix = "rsa-priv-";
    private const string MlKemAccountPrefix = "mlkem-priv-";

    private const string KeyServerBaseUrl = "https://attachecase-key-server.m-229.workers.dev";

    private static readonly string DefaultDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AttacheCase", "PostPPAP");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// 既定の HttpClient。PostPpapManager は復号経路などから繰り返し作られるうえ
    /// IDisposable ではないので、インスタンスごとに持つとソケットが枯渇する。
    /// PooledConnectionLifetime は DNS の変更に追従するため。
    /// </summary>
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly IKeychainService _keychain;
    private readonly HttpClient _http;
    private readonly string _dataDir;
    private readonly string _accountsPath;
    private readonly string _keychainService;

    /// <param name="dataDir">
    /// accounts.json の置き場。既定は ~/Library/Application Support/AttacheCase/PostPPAP。
    /// テストから一時ディレクトリを渡して、実際の環境を汚さないようにできる。
    /// </param>
    /// <param name="keychainService">
    /// Keychain のサービス名。テストでは専用の名前を渡して本番の鍵と混ぜない。
    /// </param>
    public PostPpapManager(
        IKeychainService? keychain = null,
        HttpClient? http = null,
        string? dataDir = null,
        string? keychainService = null)
    {
        // 既定は OS ごとの秘密情報ストア（macOS: キーチェーン / Windows: DPAPI）
        _keychain = keychain ?? Platform.SecretStoreFactory.CreateDefault();
        _http = http ?? SharedHttp;
        _dataDir = dataDir ?? DefaultDataDir;
        _accountsPath = Path.Combine(_dataDir, "accounts.json");
        _keychainService = keychainService ?? KeychainService;
    }

    // =================================================================
    // アカウント一覧の入出力
    // =================================================================

    public bool HasAccounts => LoadAccounts().Count > 0;

    public List<PostPpapKeyInfo> LoadAccounts()
    {
        EnsureDataDir();
        if (!File.Exists(_accountsPath)) return new List<PostPpapKeyInfo>();

        var json = File.ReadAllText(_accountsPath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json)) return new List<PostPpapKeyInfo>();

        return JsonSerializer.Deserialize<List<PostPpapKeyInfo>>(json, JsonOpts)
               ?? new List<PostPpapKeyInfo>();
    }

    public void SaveAccounts(List<PostPpapKeyInfo> accounts)
    {
        EnsureDataDir();
        var json = JsonSerializer.Serialize(accounts, JsonOpts);
        File.WriteAllText(_accountsPath, json, Encoding.UTF8);

        // deviceToken（サーバ認証トークン）とメールアドレスが入るので、
        // 既定の 0644 のままにせず所有者だけが読めるようにする
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_accountsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>後方互換: 最初のアカウント情報を返す。</summary>
    public PostPpapKeyInfo? LoadKeyInfo()
    {
        var accounts = LoadAccounts();
        return accounts.Count > 0 ? accounts[0] : null;
    }

    /// <summary>アカウントを accounts.json と Keychain の両方から削除する。</summary>
    public bool DeleteAccount(string fingerprint)
    {
        var accounts = LoadAccounts();
        var idx = accounts.FindIndex(a =>
            string.Equals(a.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;

        accounts.RemoveAt(idx);
        SaveAccounts(accounts);

        _keychain.Delete(_keychainService, RsaAccountPrefix + fingerprint);
        _keychain.Delete(_keychainService, MlKemAccountPrefix + fingerprint);
        return true;
    }

    // =================================================================
    // 秘密鍵の Keychain ロード
    // =================================================================

    public RsaPrivateCrtKeyParameters LoadRsaPrivateKey(PostPpapKeyInfo account)
    {
        if (!_keychain.TryGet(_keychainService, RsaAccountPrefix + account.Fingerprint, out var der)
            || der == null)
            throw new InvalidOperationException(
                Localize["PostPpap_RsaKeyMissing"]);

        try
        {
            return (RsaPrivateCrtKeyParameters)PrivateKeyFactory.CreateKey(der);
        }
        finally
        {
            CryptoHelper5.SecureClear(der);
        }
    }

    public MLKemPrivateKeyParameters LoadMlKemPrivateKey(PostPpapKeyInfo account)
    {
        if (!_keychain.TryGet(_keychainService, MlKemAccountPrefix + account.Fingerprint, out var raw)
            || raw == null)
            throw new InvalidOperationException(
                Localize["PostPpap_MlKemKeyMissing"]);

        try
        {
            // BouncyCastle 2.6 の ML-KEM は PKCS#8 の往復に不具合があるため、
            // raw エンコード（MLKemPrivateKeyParameters.GetEncoded() 形式）で保存・復元する。
            return MLKemPrivateKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, raw);
        }
        finally
        {
            CryptoHelper5.SecureClear(raw);
        }
    }

    // =================================================================
    // 登録フロー
    // =================================================================

    /// <summary>
    /// 登録を開始する。鍵ペア生成 → 秘密鍵を Keychain 保存 → サーバへ公開鍵を登録要求。
    /// 戻り値の Account はメモリ上のみで、accounts.json には未保存。
    /// AwaitVerificationAsync 内でメール認証完了時に accounts.json に追加される。
    /// </summary>
    public async Task<(string PollToken, PostPpapKeyInfo Account)> StartRegistrationAsync(
        string email,
        byte algorithmId = DefaultAlgorithmId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException(Localize["PostPpap_EnterEmail"], nameof(email));

        EnsureDataDir();

        // RSA 鍵ペア生成
        var rsaPair = CryptoHelper5.GenerateRsaKeyPair(RsaKeySize);
        var rsaPub = (RsaKeyParameters)rsaPair.Public;
        var rsaPriv = (RsaPrivateCrtKeyParameters)rsaPair.Private;

        var modulusBytes = rsaPub.Modulus.ToByteArrayUnsigned();
        var exponentBytes = rsaPub.Exponent.ToByteArrayUnsigned();
        var modulusB64 = Convert.ToBase64String(modulusBytes);
        var exponentB64 = Convert.ToBase64String(exponentBytes);
        var rsaPrivDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(rsaPriv).GetDerEncoded();

        // ML-KEM-768 鍵ペア（ハイブリッド時のみ）
        byte[]? mlkemPubBytes = null;
        byte[]? mlkemPrivDer = null;
        string? mlkemPubB64 = null;

        var isHybrid = algorithmId is PkaAlgorithmId.Rsa2048MlKem768
                                    or PkaAlgorithmId.Rsa4096MlKem768;
        if (isHybrid)
        {
            var gen = new MLKemKeyPairGenerator();
            gen.Init(new MLKemKeyGenerationParameters(new SecureRandom(), MLKemParameters.ml_kem_768));
            var pair = gen.GenerateKeyPair();
            var mlkemPub = (MLKemPublicKeyParameters)pair.Public;
            var mlkemPriv = (MLKemPrivateKeyParameters)pair.Private;

            mlkemPubBytes = mlkemPub.GetEncoded();
            // ML-KEM 秘密鍵は raw 形式で Keychain に保存する（PKCS#8 は BC 2.6 で復元不可のため）
            mlkemPrivDer = mlkemPriv.GetEncoded();
            mlkemPubB64 = Convert.ToBase64String(mlkemPubBytes);
        }

        var fingerprintBytes = CryptoHelper5.ComputeFingerprintFromBytes(modulusBytes, mlkemPubBytes);
        var fingerprintHex = ToHexLower(fingerprintBytes);

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var emailHash = ComputeEmailHash(normalizedEmail);

        // 秘密鍵の保存を先に済ませる。
        // 逆順にすると、Keychain への保存に失敗したときには公開鍵がサーバに載ったまま
        // 秘密鍵だけが失われ、宛先に送られたファイルを永久に復号できなくなる
        var hasMlKemKey = mlkemPrivDer != null;

        try
        {
            _keychain.Set(_keychainService, RsaAccountPrefix + fingerprintHex, rsaPrivDer);
            if (mlkemPrivDer != null)
                _keychain.Set(_keychainService, MlKemAccountPrefix + fingerprintHex, mlkemPrivDer);
        }
        catch
        {
            // RSA だけ書けて ML-KEM で失敗する、といった中途半端な状態を残さない
            RollBackStoredPrivateKeys(fingerprintHex, hasMlKemKey);
            throw;
        }
        finally
        {
            CryptoHelper5.SecureClear(rsaPrivDer);
            if (mlkemPrivDer != null) CryptoHelper5.SecureClear(mlkemPrivDer);
        }

        // サーバに登録要求。失敗したら、行き場のなくなった秘密鍵を消して元に戻す
        string pollToken;
        try
        {
            pollToken = await RequestRegistrationAsync(
                normalizedEmail, modulusB64, exponentB64, fingerprintHex,
                algorithmId, mlkemPubB64, cancellationToken);
        }
        catch
        {
            RollBackStoredPrivateKeys(fingerprintHex, hasMlKemKey);
            throw;
        }

        var account = new PostPpapKeyInfo
        {
            Email = normalizedEmail,
            EmailHash = emailHash,
            Modulus = modulusB64,
            Exponent = exponentB64,
            Fingerprint = fingerprintHex,
            AlgorithmId = algorithmId,
            MlKemPublicKey = mlkemPubB64,
            RegisteredAt = DateTime.UtcNow,
        };

        return (pollToken, account);
    }

    /// <summary>
    /// メール認証完了を待つ。認証成功時に DeviceToken をセットして accounts.json に保存する。
    /// timeout かキャンセル/expired の場合は false を返し、accounts.json には何も保存せず、
    /// StartRegistrationAsync が Keychain へ保存した秘密鍵も削除して元に戻す
    /// （認証に至らなかった鍵ペアは accounts.json に載らないため DeleteAccount からも
    /// 消せず、再試行のたびに孤児として溜まっていく）。
    /// </summary>
    public async Task<bool> AwaitVerificationAsync(
        string pollToken,
        PostPpapKeyInfo pendingAccount,
        int timeoutMinutes = 10,
        CancellationToken cancellationToken = default)
    {
        if (pendingAccount == null) throw new ArgumentNullException(nameof(pendingAccount));

        var hasMlKemKey = pendingAccount.MlKemPublicKey != null;

        try
        {
            var deadline = DateTime.UtcNow.AddMinutes(timeoutMinutes);
            var seenVerifiedWithoutToken = false;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var status = await GetRegistrationStatusAsync(pollToken, cancellationToken);
                switch (status.Status)
                {
                    case "verified":
                        // DeviceToken が無い verified はサーバ都合の中途半端な応答。
                        // そのまま受理するとハートビート等の認証つき通信が全部
                        // 効かない口だけのアカウントが残るので、少し待って読み直す
                        if (string.IsNullOrEmpty(status.DeviceToken))
                        {
                            seenVerifiedWithoutToken = true;
                            break; // 次のポーリングで再取得
                        }

                        pendingAccount.DeviceToken = status.DeviceToken;
                        PersistVerifiedAccount(pendingAccount);
                        return true;

                    case "expired":
                        RollBackStoredPrivateKeys(pendingAccount.Fingerprint!, hasMlKemKey);
                        return false;
                }

                await Task.Delay(3000, cancellationToken);
            }

            if (seenVerifiedWithoutToken)
            {
                // 期限まで待っても DeviceToken が来なかったが、サーバ上の登録
                // （verified）は成立している。ここで秘密鍵を消すと、この公開鍵に
                // 宛てて送られたファイルが永久に開けなくなるため、アカウントは
                // 保存する（DeviceToken 無しでもハートビートが黙って休むだけで、
                // 暗号化・復号はできる）
                PersistVerifiedAccount(pendingAccount);
                return true;
            }

            RollBackStoredPrivateKeys(pendingAccount.Fingerprint!, hasMlKemKey);
            return false;
        }
        catch (OperationCanceledException)
        {
            // 利用者が確認待ちをやめた。行き場のない秘密鍵を残さない
            RollBackStoredPrivateKeys(pendingAccount.Fingerprint!, hasMlKemKey);
            throw;
        }
    }

    private void PersistVerifiedAccount(PostPpapKeyInfo pendingAccount)
    {
        var accounts = LoadAccounts();
        // 取り除くのは同じ fingerprint の重複だけ。同じメールの旧鍵まで消すと、
        // その鍵宛てに受け取った既存ファイルが復号できなくなる
        // （復号は accounts.json の全アカウントを fingerprint で照合するため、
        // 一覧から外れた時点で Keychain に秘密鍵が残っていても使われない）
        accounts.RemoveAll(a => string.Equals(
            a.Fingerprint, pendingAccount.Fingerprint, StringComparison.OrdinalIgnoreCase));
        // 先頭が「現在の主アカウント」（LoadKeyInfo・ハートビートが参照する）
        accounts.Insert(0, pendingAccount);
        SaveAccounts(accounts);
    }

    // =================================================================
    // 公開鍵検索（宛先メールから）
    // =================================================================

    public async Task<PostPpapKeyInfo?> LookupPublicKeyAsync(
        string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException(Localize["PostPpap_EnterEmail"], nameof(email));

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var emailHash = ComputeEmailHash(normalizedEmail);

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"{KeyServerBaseUrl}/api/keys/{emailHash}");

        // 自デバイスの情報をヘッダーに付与（サーバ側 lastSeen の自動更新用）
        var me = LoadKeyInfo();
        if (me != null
            && !string.IsNullOrEmpty(me.EmailHash)
            && !string.IsNullOrEmpty(me.Fingerprint)
            && !string.IsNullOrEmpty(me.DeviceToken))
        {
            req.Headers.Add("X-My-EmailHash", me.EmailHash);
            req.Headers.Add("X-My-Fingerprint", me.Fingerprint);
            req.Headers.Add("X-My-DeviceToken", me.DeviceToken);
        }

        using var response = await _http.SendAsync(req, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                Localize.GetString("PostPpap_ServerError", (int)response.StatusCode, response.StatusCode));

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<LookupResponse>(json, JsonOpts);
        if (parsed?.Ok != true || parsed.Data?.Registered != true || parsed.Data.Keys == null)
            return null;

        // 最新の鍵エントリを使用（配列末尾を採用）
        var keys = parsed.Data.Keys;
        if (keys.Length == 0) return null;
        var latest = keys[^1];

        if (string.IsNullOrEmpty(latest.Modulus)) return null;

        // Post-PPAP ではハイブリッド暗号 (0x11, 0x12) のみ受信可
        var algorithmId = latest.AlgorithmId ?? PkaAlgorithmId.None;
        var isHybrid = algorithmId is PkaAlgorithmId.Rsa2048MlKem768
                                    or PkaAlgorithmId.Rsa4096MlKem768;
        if (!isHybrid || string.IsNullOrEmpty(latest.MlKemPublicKey))
            throw new InvalidOperationException(
                Localize["PostPpap_RecipientNotHybrid"]);

        // サーバ応答（外部入力）の検証。
        //   - 符号化の壊れた鍵をここで弾かないと、後段の暗号化が分かりにくい
        //     例外で失敗する
        //   - fingerprint は暗号化ファイルへそのまま書き込まれ、受信側は自分の
        //     鍵との一致で復号可否を判定する。鍵素材と食い違った fingerprint を
        //     受け入れると、正しい受信者が開けないファイルを作ってしまう。
        //     鍵素材から計算し直した値と一致するものだけを通す
        byte[] modulusBytes;
        byte[] mlkemPubBytes;
        try
        {
            modulusBytes = Convert.FromBase64String(latest.Modulus);
            mlkemPubBytes = Convert.FromBase64String(latest.MlKemPublicKey);
            _ = Convert.FromBase64String(latest.Exponent ?? "");
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(Localize["PostPpap_InvalidServerKey"]);
        }

        if (string.IsNullOrEmpty(latest.Exponent) ||
            modulusBytes.Length == 0 || mlkemPubBytes.Length == 0)
        {
            throw new InvalidOperationException(Localize["PostPpap_InvalidServerKey"]);
        }

        var computedFingerprint = ToHexLower(
            CryptoHelper5.ComputeFingerprintFromBytes(modulusBytes, mlkemPubBytes));
        if (!string.Equals(computedFingerprint, latest.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Localize["PostPpap_InvalidServerKey"]);
        }

        return new PostPpapKeyInfo
        {
            Email = normalizedEmail,
            EmailHash = emailHash,
            Modulus = latest.Modulus,
            Exponent = latest.Exponent,
            Fingerprint = latest.Fingerprint,
            AlgorithmId = algorithmId,
            MlKemPublicKey = latest.MlKemPublicKey,
            RegisteredAt = DateTime.TryParse(latest.RegisteredAt, out var dt) ? dt : DateTime.UtcNow,
        };
    }

    // =================================================================
    // ハートビート
    // =================================================================

    public async Task SendHeartbeatAsync(
        PostPpapKeyInfo account, CancellationToken cancellationToken = default)
    {
        if (account == null) throw new ArgumentNullException(nameof(account));
        if (string.IsNullOrEmpty(account.DeviceToken)) return;
        if (string.IsNullOrEmpty(account.EmailHash) || string.IsNullOrEmpty(account.Fingerprint))
            return;

        var payload = JsonSerializer.Serialize(new HeartbeatRequest
        {
            Fingerprint = account.Fingerprint,
            DeviceToken = account.DeviceToken,
        }, JsonOpts);

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(
                $"{KeyServerBaseUrl}/api/keys/{account.EmailHash}/heartbeat",
                content, cancellationToken);
        }
        catch
        {
            // ベストエフォート（次回リトライすれば良い）
        }
    }

    // =================================================================
    // サーバ通信下回り
    // =================================================================

    private async Task<string> RequestRegistrationAsync(
        string email, string modulus, string exponent, string fingerprint,
        byte algorithmId, string? mlkemPublicKey,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new RegisterInitiateRequest
        {
            Email = email,
            Modulus = modulus,
            Exponent = exponent,
            Fingerprint = fingerprint,
            AlgorithmId = algorithmId,
            MlKemPublicKey = mlkemPublicKey,
        }, JsonOpts);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(
            $"{KeyServerBaseUrl}/api/register/initiate", content, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        RegisterInitiateResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RegisterInitiateResponse>(json, JsonOpts);
        }
        catch (JsonException)
        {
            // 5xx やプロキシが返す HTML など、JSON でない本文。解析エラーの
            // 生メッセージを利用者に見せず、サーバエラーとして案内へ落とす
            parsed = null;
        }

        if (!response.IsSuccessStatusCode || parsed?.Ok != true || parsed.Data == null)
        {
            // 待ち時間はボディを優先し、無ければ Retry-After ヘッダを見る。
            var retryAfterSeconds = parsed?.Data?.RetryAfterSeconds
                                    ?? (int?)response.Headers.RetryAfter?.Delta?.TotalSeconds;

            throw new InvalidOperationException(
                ServerErrorToMessage(parsed?.Error, (int)response.StatusCode, retryAfterSeconds));
        }

        return parsed.Data.PollToken;
    }

    private async Task<RegisterStatusData> GetRegistrationStatusAsync(
        string pollToken, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(
                $"{KeyServerBaseUrl}/api/register/status/{pollToken}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new RegisterStatusData { Status = "pending" };

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<RegisterStatusResponse>(json, JsonOpts);
            return parsed?.Data ?? new RegisterStatusData { Status = "pending" };
        }
        catch (TaskCanceledException)
        {
            return new RegisterStatusData { Status = "pending" };
        }
        catch (JsonException)
        {
            // 200 なのに JSON でない本文（プロキシの HTML 等）。
            // 登録を失敗させず、次のポーリングで取り直す
            return new RegisterStatusData { Status = "pending" };
        }
    }

    private static string ServerErrorToMessage(string? errorCode, int httpStatus, int? retryAfterSeconds = null)
    {
        return errorCode switch
        {
            // 待ち時間が分かるなら「あと何分か」まで伝える。
            // 旧サーバは返さないので、その場合は従来どおり曖昧な案内に落とす。
            "rate_limited" => retryAfterSeconds is > 0
                ? Localize.GetString("PostPpap_TryAgainAfter", FormatRetryAfter(retryAfterSeconds.Value))
                : Localize["PostPpap_TryAgainLater"],
            "service_busy" => retryAfterSeconds is > 0
                ? Localize.GetString("PostPpap_ServiceBusyAfter", FormatRetryAfter(retryAfterSeconds.Value))
                : Localize["PostPpap_ServiceBusy"],
            "missing_fields" => Localize["PostPpap_MissingInformation"],
            "invalid_email" => Localize["PostPpap_InvalidEmail"],
            "invalid_fingerprint"
                or "invalid_modulus"
                or "invalid_exponent"
                or "invalid_algorithm"
                or "missing_mlkem_key"
                or "invalid_mlkem_key" => Localize["PostPpap_KeyDataProblem"],
            "already_registered" => Localize["PostPpap_KeyAlreadyRegistered"],
            _ => Localize.GetString("PostPpap_ServerErrorHttp", httpStatus),
        };
    }

    /// <summary>
    /// 残り秒数を「約○分」「約○時間」の粒度に丸める。
    /// 秒まで出しても待つ側には意味がないので、切り上げて分・時間で言い切る。
    /// </summary>
    private static string FormatRetryAfter(int seconds)
    {
        if (seconds < 60)
            return Localize["PostPpap_DurationLessThanAMinute"];

        var minutes = (int)Math.Ceiling(seconds / 60.0);
        if (minutes < 60)
            return Localize.GetString(
                minutes == 1 ? "PostPpap_DurationMinute" : "PostPpap_DurationMinutes", minutes);

        var hours = (int)Math.Ceiling(minutes / 60.0);
        return Localize.GetString(
            hours == 1 ? "PostPpap_DurationHour" : "PostPpap_DurationHours", hours);
    }

    // =================================================================
    // ユーティリティ
    // =================================================================

    /// <summary>
    /// 登録を最後まで進められなかったときに、書き込んだ秘密鍵を Keychain から消す。
    /// accounts.json に載らない鍵が残っても誰も使えず、消す手立てもなくなる。
    /// </summary>
    private void RollBackStoredPrivateKeys(string fingerprintHex, bool hasMlKemKey)
    {
        try
        {
            _keychain.Delete(_keychainService, RsaAccountPrefix + fingerprintHex);
            if (hasMlKemKey)
                _keychain.Delete(_keychainService, MlKemAccountPrefix + fingerprintHex);
        }
        catch (Exception ex)
        {
            // 後始末に失敗しても、元の例外を握り潰して隠さない
            Console.Error.WriteLine($"Failed to roll back Keychain entries: {ex.Message}");
        }
    }

    private void EnsureDataDir()
    {
        // 既存でも作り直しても、権限は毎回そろえる。
        // 以前に緩い権限で作られたディレクトリを直せるようにするため
        Directory.CreateDirectory(_dataDir);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_dataDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static string ComputeEmailHash(string email)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(email));
        return ToHexLower(hash);
    }

    private static string ToHexLower(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
