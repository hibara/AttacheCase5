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
// 更新の確認。
//
// 配信マニフェスト（appcast.json）と、その分離署名（appcast.json.sig）を取得し、
// Ed25519 で検証してから中身を信用する。
//
// ここで確認できるのは「新しい版があるかどうか」までで、ダウンロードと差し替えは
// 別に行う。差し替える .app については、Ed25519 とは別に codesign と TeamID を
// 必ず確認すること。ブラウザ経由で落とさないため Gatekeeper が働かず、
// 検証はこちらの責任になる。
//----------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace AttacheCase.Services;

public class UpdateService : IUpdateService
{
    /// <summary>
    /// 配信マニフェストの URL。https 固定。
    ///
    /// 版ごとのディレクトリ（.../mac/501b/）ではなく、その 1 つ上に置く。
    /// アプリは固定の URL を叩くので、置き場が版ごとに変わると次の版を見つけられない。
    /// 配布物（DMG）のほうは版ごとのディレクトリでよい。マニフェストが絶対 URL で指す。
    /// </summary>
    public const string DefaultManifestUrl =
        "https://hibara.jp/dl/attachecase/mac/appcast.json";

    /// <summary>Windows 向けのマニフェスト。builds のキーは installer / zip。</summary>
    public const string DefaultWindowsManifestUrl =
        "https://hibara.jp/dl/attachecase/windows/appcast.json";

    /// <summary>ZIP（ポータブル）版の更新案内で開く配布ページ。</summary>
    public const string DefaultDownloadPageUrl =
        "https://hibara.jp/software/attachecase/";

    /// <summary>Windows マニフェストの builds キー。</summary>
    public const string BuildKeyInstaller = "installer";
    public const string BuildKeyZip = "zip";

    /// <summary>動作中の OS 向けの既定マニフェスト URL。</summary>
    public static string DefaultManifestUrlForCurrentOs =>
        OperatingSystem.IsWindows() ? DefaultWindowsManifestUrl : DefaultManifestUrl;

    /// <summary>
    /// マニフェストの URL を差し替える環境変数。試験用。
    ///
    /// 本番の appcast は配信中の利用者が毎日見に来るので、更新機構の通し確認に
    /// 使えない。この環境変数に試験用の URL（例: 本番と同じサーバーの
    /// <c>.../mac/test/appcast.json</c>）を入れて起動すると、そちらを見に行く。
    ///
    /// 差し替えても安全側の検査は緩まない: https 以外は拒否し、署名は
    /// アプリに焼き込んだ公開鍵で検証する。第三者が環境変数を仕込めても、
    /// 秘密鍵で署名したマニフェストしか受け付けない。
    ///
    /// 空白だけの値は未設定として扱う。コンストラクターに URL を明示した
    /// ときはそちらが勝つ（テストがローカルの試験用サーバーを指すため）。
    /// </summary>
    public const string ManifestUrlEnvironmentVariable = "ATTACHECASE_UPDATE_MANIFEST_URL";

    /// <summary>
    /// 環境変数による差し替えが効いているか。画面やログで「本番ではない」と
    /// 分かるようにするために使う。
    /// </summary>
    public static bool IsManifestUrlOverridden => ManifestUrlFromEnvironment() is not null;

    private static string? ManifestUrlFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(ManifestUrlEnvironmentVariable);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// 使うマニフェスト URL を決める。明示 → 環境変数 → OS 既定の順。
    /// </summary>
    internal static string ResolveManifestUrl(string? explicitUrl) =>
        explicitUrl ?? ManifestUrlFromEnvironment() ?? DefaultManifestUrlForCurrentOs;

    /// <summary>実際に見に行くマニフェストの URL（テストの確認用）。</summary>
    internal string ManifestUrl => _manifestUrl;

    /// <summary>
    /// マニフェストを検証する Ed25519 公開鍵（Base64、32 バイト）。現用の鍵。
    /// 対応する秘密鍵はリリース作業者のみが持つ。appcast_generate.sh で署名する。
    /// </summary>
    /// <remarks>
    /// 未設定（空）のあいだは更新確認そのものを行わない。鍵を作らないまま
    /// 署名なしのマニフェストを受け入れてしまうと、確認の意味が無くなるため。
    /// </remarks>
    public const string UpdatePublicKeyBase64 = "Qd/M1bhhRng+575rT/Cti4gsXW/R4GIpsmGhsEFe+BI=";

    /// <summary>
    /// 予備の Ed25519 公開鍵（Base64、32 バイト）。
    ///
    /// 公開鍵はアプリに焼き込まれるため、あとから足せない。現用の鍵しか知らない
    /// 版を配ってしまうと、その利用者は現用の秘密鍵が漏れても失われても、
    /// 二度と更新を受け取れなくなる（署名を検証できず、手で入れ直すしかない）。
    ///
    /// はじめから 2 本を信頼させておけば、予備の鍵で署名し直すだけで済む。
    /// アプリの変更も再配布も要らない。
    /// </summary>
    /// <remarks>
    /// 引き換えに、正しい署名を作れる鍵が 2 本になる。予備の秘密鍵は現用と
    /// 同じかそれ以上に厳重に、かつ<b>別の場所</b>へ保管すること。同じ
    /// フォルダーに並べて置くと、片方が漏れる状況では両方漏れる。
    /// </remarks>
    public const string UpdateBackupPublicKeyBase64 = "7RSEaLajqWauZVELOIyPQ4CPo7GMYv7BIFoC+irL+bo=";

    /// <summary>
    /// Windows 配信用の Ed25519 公開鍵（現用・予備）。
    /// Windows と macOS で鍵を分けているのは意図的（片方の秘密鍵が漏れても
    /// もう片方の配信は無事に保つ）。値は既存 Windows 版の配信基盤と同じ。
    /// </summary>
    public const string UpdateWindowsPublicKeyBase64 = "1ETyBiTL8Vyl9lqqN/7Sd97xvzPNI5p/rVHnkYpINbU=";
    public const string UpdateWindowsBackupPublicKeyBase64 = "t3pQtuocYPwFjXwgYSfEzpxN/9EhpzvR+/V27gKouCA=";

    /// <summary>
    /// 既定で信頼する公開鍵（動作中の OS の配信に対応するもの）。
    /// どれか 1 本で検証が通れば受理する。空の要素は「未設定」として飛ばす。
    /// </summary>
    internal static string[] DefaultPublicKeys =>
        OperatingSystem.IsWindows()
            ? new[] { UpdateWindowsPublicKeyBase64, UpdateWindowsBackupPublicKeyBase64 }
            : new[] { UpdatePublicKeyBase64, UpdateBackupPublicKeyBase64 };

    /// <summary>
    /// マニフェストの読み込み上限。巨大な本文を掴まされて詰まるのを防ぐ。
    /// 実際のマニフェストは 1KB 程度。
    /// </summary>
    private const int MaxManifestBytes = 64 * 1024;

    /// <summary>署名の読み込み上限。Ed25519 の署名は 64 バイト。</summary>
    private const int MaxSignatureBytes = 1024;

    /// <summary>
    /// 共有の HttpClient。PostPpapManager と同じ理由でインスタンスごとに持たない。
    /// </summary>
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _manifestUrl;
    private readonly string[] _publicKeys;
    private readonly Version _currentVersion;

    /// <param name="manifestUrl">
    /// マニフェストの URL。テストではローカルの試験用サーバーを指す。
    /// </param>
    /// <param name="publicKeyBase64">
    /// 検証に使う Ed25519 公開鍵。渡したときは<b>その 1 本だけ</b>を信頼する。
    /// テストで試験用の鍵に差し替えるために使う。
    /// </param>
    /// <param name="currentVersion">
    /// 比較の基準にする現在のバージョン。既定は実行中のアセンブリのもの。
    /// </param>
    /// <param name="publicKeysBase64">
    /// 信頼する公開鍵を複数まとめて渡す場合。既定は現用と予備の 2 本。
    /// </param>
    public UpdateService(
        HttpClient? http = null,
        string? manifestUrl = null,
        string? publicKeyBase64 = null,
        Version? currentVersion = null,
        string[]? publicKeysBase64 = null)
    {
        _http = http ?? SharedHttp;
        _manifestUrl = ResolveManifestUrl(manifestUrl);
        _publicKeys = publicKeysBase64
                      ?? (publicKeyBase64 is null ? DefaultPublicKeys : new[] { publicKeyBase64 });
        _currentVersion = currentVersion ?? GetCurrentVersion();
    }

    /// <summary>実行中のアプリのバージョン。</summary>
    public static Version GetCurrentVersion() =>
        // Windows は 4 桁目（Revision）で版を進める運用。3 桁に丸めると
        // 5.0.0.3 と 5.0.0.4 が同じに見えて、更新を検出できない
        //（Windows 版で実際に踏んだ問題）。macOS は 3 桁運用のまま
        OperatingSystem.IsWindows() ? AppVersion.CurrentFull : AppVersion.Current;

    /// <summary>
    /// 更新確認で名乗る User-Agent を組み立てる。
    ///
    /// 例: AttacheCase/5.0.1 (macOS 26.6.2; arm64)
    ///
    /// .NET の HttpClient は既定で User-Agent を送らない。付けないと配信側の
    /// アクセスログでクローラーや手動の wget と区別がつかない。
    ///
    /// バージョンとアーキテクチャを載せておくと、どの版がどれだけ使われているかが
    /// アクセスログだけで分かる（Intel 版をいつ切れるかの判断などに使える）。
    ///
    /// 個体を識別する値は載せない。数えたいのは「どの版が使われているか」であって
    /// 「誰が使っているか」ではない。
    /// </summary>
    internal static string BuildUserAgent(Version version, string architecture, Version osVersion) =>
        $"AttacheCase/{version} ({(OperatingSystem.IsWindows() ? "Windows" : "macOS")} {osVersion}; {architecture})";

    /// <summary>この実行環境での User-Agent。</summary>
    public static string UserAgent =>
        BuildUserAgent(GetCurrentVersion(), GetCurrentArchitecture(), Environment.OSVersion.Version);

    /// <summary>
    /// 実行中のプロセスのアーキテクチャ名。マニフェストの builds のキーに対応する。
    /// </summary>
    public static string GetCurrentArchitecture() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!HasAnyKey(_publicKeys))
            {
                // 鍵を用意しないまま無署名のマニフェストを信用すると、確認の意味が無い
                return UpdateCheckResult.Failed("更新確認の公開鍵が設定されていません。");
            }

            if (!IsHttps(_manifestUrl))
            {
                return UpdateCheckResult.Failed("マニフェストの URL が https ではありません。");
            }

            var manifestBytes = await GetBytesAsync(_manifestUrl, MaxManifestBytes, cancellationToken)
                .ConfigureAwait(false);
            var signatureBytes = await GetBytesAsync(_manifestUrl + ".sig", MaxSignatureBytes, cancellationToken)
                .ConfigureAwait(false);

            if (!VerifySignature(manifestBytes, signatureBytes, _publicKeys))
            {
                // ここを通すと、以降の URL もハッシュも信用できなくなる
                return UpdateCheckResult.Failed("マニフェストの署名を検証できませんでした。");
            }

            var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes, JsonOpts);
            if (manifest is null)
            {
                return UpdateCheckResult.Failed("マニフェストを解釈できませんでした。");
            }

            return Evaluate(manifest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 呼び出し側が止めた。中止は失敗ではないので、そのまま投げ返す
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient.Timeout と、本文の読み取りに掛けた期限もここへ来る。
            // TaskCanceledException（＝OperationCanceledException）なので、
            // 分けずに投げ返すと呼び出し側が「利用者が中止した」と取り違える。
            //
            // 起動時の確認では確認日時を記録しないまま抜けることになり、
            // 通信できない間じゅう起動のたびにサーバーを叩きに行く。
            // 手動の確認では "A task was canceled." がそのまま画面に出る
            return UpdateCheckResult.Failed("更新の確認がタイムアウトしました。");
        }
        catch (Exception ex)
        {
            // 起動時の確認で利用者の作業を止めないよう、失敗は握って返す
            return UpdateCheckResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// 署名を検証済みのマニフェストを、現在の環境と突き合わせる。
    /// </summary>
    internal UpdateCheckResult Evaluate(UpdateManifest manifest)
    {
        if (!Version.TryParse(manifest.Version, out var newVersion))
        {
            return UpdateCheckResult.Failed($"バージョンの表記が不正です: {manifest.Version}");
        }

        // 上がるときだけ受け付ける。古い版を掴ませるダウングレード攻撃を防ぐため、
        // 「違っていたら更新」ではなく「新しいときだけ更新」にする
        if (newVersion <= _currentVersion)
        {
            return UpdateCheckResult.UpToDate();
        }

        if (!IsSystemVersionSatisfied(manifest.MinimumSystemVersion))
        {
            // 動かない版を掴ませない。更新は無いものとして扱う
            return UpdateCheckResult.UpToDate();
        }

        // builds のキーは OS で意味が違う:
        //   macOS   … アーキテクチャ（arm64 / x64）
        //   Windows … 導入形態（installer / zip）。ZIP 展開版は自動更新せず
        //             配布ページへ誘導するだけなので、どちらのキーでも案内は出せる
        var buildKey = OperatingSystem.IsWindows()
            ? Platform.Windows.WindowsInstallKind.BuildKeyFor(Platform.Windows.WindowsInstallKind.Detect())
            : GetCurrentArchitecture();
        if (!manifest.Builds.TryGetValue(buildKey, out var build) || build is null)
        {
            return UpdateCheckResult.Failed($"この環境向けの配布物がありません: {buildKey}");
        }

        if (!IsHttps(build.Url))
        {
            return UpdateCheckResult.Failed("配布物の URL が https ではありません。");
        }

        if (build.Size <= 0 || string.IsNullOrWhiteSpace(build.Sha256))
        {
            // サイズとハッシュが無いと、落としたものを検証できない
            return UpdateCheckResult.Failed("配布物のサイズまたはハッシュがありません。");
        }

        return UpdateCheckResult.Available(manifest, build);
    }

    /// <summary>
    /// 信頼する鍵が 1 本でもあるか。空の要素（未設定の予備鍵）は数えない。
    /// </summary>
    /// <remarks>
    /// null と空配列は false を返す。呼び違えたときに、鍵が無いまま
    /// 素通りするのを防ぐため。
    /// </remarks>
    internal static bool HasAnyKey(string[]? publicKeysBase64)
    {
        if (publicKeysBase64 is null) return false;

        foreach (var key in publicKeysBase64)
        {
            if (!string.IsNullOrWhiteSpace(key)) return true;
        }

        return false;
    }

    /// <summary>
    /// Ed25519 の分離署名を、信頼する鍵のどれかで検証する。
    /// 1 本でも通れば受理する。空の要素は飛ばす。
    /// </summary>
    /// <remarks>
    /// 鍵が 1 本も無い場合は false。「鍵が無いから検証しない」ではなく
    /// 「検証できないので拒否」として扱う。
    /// </remarks>
    internal static bool VerifySignature(byte[] data, byte[] signature, string[]? publicKeysBase64)
    {
        if (publicKeysBase64 is null) return false;

        var verified = false;
        foreach (var key in publicKeysBase64)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;

            // 通ったあとも残りを回す。かかった時間から、どの鍵で通ったかを
            // 外から推し量られないようにするため
            verified |= VerifySignature(data, signature, key);
        }

        return verified;
    }

    /// <summary>
    /// Ed25519 の分離署名を、指定の 1 本で検証する。
    /// 受け取ったバイト列そのものを対象にする。
    /// </summary>
    internal static bool VerifySignature(byte[] data, byte[] signature, string publicKeyBase64)
    {
        try
        {
            var publicKey = Convert.FromBase64String(publicKeyBase64.Trim());
            if (publicKey.Length != Ed25519PublicKeyParameters.KeySize) return false;
            if (signature.Length != Ed25519PrivateKeyParameters.SignatureSize) return false;

            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
            verifier.BlockUpdate(data, 0, data.Length);
            return verifier.VerifySignature(signature);
        }
        catch (Exception)
        {
            // 鍵や署名の形が壊れている場合も、検証失敗として扱う
            return false;
        }
    }

    /// <summary>
    /// 動作中の macOS が、要求される最低バージョンを満たすか。
    /// 指定が無い、または解釈できない場合は満たしているものとして扱う。
    /// </summary>
    internal static bool IsSystemVersionSatisfied(string? minimumSystemVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumSystemVersion)) return true;
        if (!Version.TryParse(NormalizeVersion(minimumSystemVersion), out var required)) return true;

        var current = Environment.OSVersion.Version;
        return current >= required;
    }

    /// <summary>"13" や "13.0" を Version が解釈できる形にそろえる。</summary>
    private static string NormalizeVersion(string value) =>
        value.Contains('.') ? value : value + ".0";

    private static bool IsHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// マニフェストと署名の本文を読むのに掛ける期限。
    ///
    /// ResponseHeadersRead で受けているため、HttpClient.Timeout が効くのは
    /// ヘッダーまで。ヘッダーだけ返して本文を止めるサーバーに当たると、
    /// 本文の待ちが永久に返らない。
    ///
    /// 実際のマニフェストは 1KB 程度なので、これで足りる。配布物（DMG）の
    /// ダウンロードには同じものを掛けないこと（何分もかかるのが普通で、
    /// あちらは進捗画面の「中止」が効く）。
    /// </summary>
    private static readonly TimeSpan BodyReadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 上限つきで本文を読む。Content-Length を信用せず、実際に読んだ量でも打ち切る。
    /// </summary>
    private async Task<byte[]> GetBytesAsync(string url, int maxBytes, CancellationToken cancellationToken)
    {
        // https のままの移動先へは 1 回だけ追従する。サーバー側に末尾スラッシュや
        // www の正規化が入っただけで更新が届かなくなるのを避けるため
        using var response = await HttpRedirect.SendAsync(
            _http,
            target =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, target);

                // HttpClient そのものではなく要求ごとに付ける。呼び出し側が別の
                // HttpClient を渡してきても、名乗りが落ちないようにするため
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                return request;
            },
            new Uri(url),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > 0 and var declared && declared > maxBytes)
        {
            throw new InvalidOperationException($"応答が大きすぎます: {url}");
        }

        // 呼び出し側の中止は活かしたまま、本文の読み取りにだけ期限を足す。
        // リンクした CTS なので、止まったままのサーバーは CheckAsync 側の
        // フィルタでタイムアウト扱いに落ちる
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeout.CancelAfter(BodyReadTimeout);

        await using var stream = await response.Content
            .ReadAsStreamAsync(readTimeout.Token).ConfigureAwait(false);

        using var buffer = new System.IO.MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, readTimeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidOperationException($"応答が大きすぎます: {url}");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
