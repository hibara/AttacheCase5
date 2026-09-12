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
// 差し替える .app の検証。
//
// ここがこのアプリで最も重要な防御線になる。
//
// 更新は HttpClient で取得するため、ダウンロードしたものに隔離属性が付かない。
// つまり **Gatekeeper は新しい .app を検査しない**。macOS が守ってくれないので、
// 検証は完全にこちら側の責任になる。
//
// 4 段で確かめる。
//
//   1. codesign --verify --deep --strict   署名が壊れていないか
//   2. TeamIdentifier が実行中の自分と一致するか
//        実質いちばん強い防御。配信サーバや Ed25519 鍵が破られても、攻撃者は
//        Apple の Developer ID で署名したバイナリを作れない
//   3. xcrun stapler validate              公証チケットが貼られているか
//        spctl の "accepted" だけでは足りない。署名済みだが公証していない .app も
//        accepted になり、source が "Developer ID"（"Notarized Developer ID" ではない）
//        となるだけで、終了コードでは区別できない。stapler なら終了コードで分かり、
//        しかもオフラインで確認できる
//   4. spctl -a -t exec                    Gatekeeper の評価として受理されるか
//
// ひとつでも通らなければ差し替えない。
//----------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AttacheCase.Services;

/// <summary>検証に失敗した理由。</summary>
public enum VerificationFailure
{
    None,

    /// <summary>署名が壊れている、または署名されていない。</summary>
    SignatureInvalid,

    /// <summary>TeamIdentifier を読み取れなかった。</summary>
    TeamIdUnavailable,

    /// <summary>TeamIdentifier が実行中の自分と違う。別人が署名したもの。</summary>
    TeamIdMismatch,

    /// <summary>公証チケットが貼られていない。</summary>
    NotNotarized,

    /// <summary>Gatekeeper に受理されなかった。</summary>
    GatekeeperRejected,

    /// <summary>検証そのものが実行できなかった。</summary>
    VerificationFailed,
}

/// <summary>検証の結果。</summary>
public sealed record VerificationResult(bool Ok, VerificationFailure Failure, string? Detail = null)
{
    public static VerificationResult Success() => new(true, VerificationFailure.None);

    public static VerificationResult Fail(VerificationFailure failure, string? detail = null) =>
        new(false, failure, detail);
}

public class UpdatePackageVerifier
{
    /// <summary>
    /// 検証コマンドの制限時間。--deep は 80MB 程度のバンドルで数秒かかる。
    /// 無制限にはしない（応答しないまま更新処理が止まるのを避ける）。
    /// </summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(120);

    private readonly IProcessRunner _runner;

    public UpdatePackageVerifier(IProcessRunner? runner = null)
    {
        _runner = runner ?? new ProcessRunner();
    }

    /// <summary>
    /// 差し替え候補の .app を検証する。
    /// </summary>
    /// <param name="appPath">検証する .app のパス。</param>
    /// <param name="expectedTeamId">
    /// 一致していなければならない TeamIdentifier。実行中の自分から取るのが基本
    /// （<see cref="GetTeamIdentifierAsync"/>）。
    /// </param>
    public async Task<VerificationResult> VerifyAsync(
        string appPath,
        string expectedTeamId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedTeamId))
        {
            // 比較相手が無いまま通すわけにはいかない
            return VerificationResult.Fail(
                VerificationFailure.TeamIdUnavailable,
                "比較対象の TeamIdentifier がありません。");
        }

        try
        {
            // 1) 署名が壊れていないか
            var codesign = await _runner.RunAsync(
                "/usr/bin/codesign",
                new[] { "--verify", "--deep", "--strict", appPath },
                CommandTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!codesign.Succeeded)
            {
                return VerificationResult.Fail(
                    VerificationFailure.SignatureInvalid, codesign.CombinedOutput.Trim());
            }

            // 2) 署名者が自分と同じか
            var actualTeamId = await GetTeamIdentifierAsync(appPath, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(actualTeamId))
            {
                return VerificationResult.Fail(
                    VerificationFailure.TeamIdUnavailable,
                    "TeamIdentifier を読み取れませんでした。");
            }

            if (!string.Equals(actualTeamId, expectedTeamId, StringComparison.Ordinal))
            {
                return VerificationResult.Fail(
                    VerificationFailure.TeamIdMismatch,
                    $"署名者が違います（期待: {expectedTeamId} / 実際: {actualTeamId}）。");
            }

            // 3) 公証チケットが貼られているか
            var stapler = await _runner.RunAsync(
                "/usr/bin/xcrun",
                new[] { "stapler", "validate", appPath },
                CommandTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!stapler.Succeeded)
            {
                return VerificationResult.Fail(
                    VerificationFailure.NotNotarized, stapler.CombinedOutput.Trim());
            }

            // 4) Gatekeeper の評価
            var spctl = await _runner.RunAsync(
                "/usr/sbin/spctl",
                new[] { "-a", "-t", "exec", "-vv", appPath },
                CommandTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!spctl.Succeeded)
            {
                return VerificationResult.Fail(
                    VerificationFailure.GatekeeperRejected, spctl.CombinedOutput.Trim());
            }

            return VerificationResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 検証できなかったものは、通さない
            return VerificationResult.Fail(VerificationFailure.VerificationFailed, ex.Message);
        }
    }

    /// <summary>
    /// .app の TeamIdentifier を読む。読めなければ null。
    /// </summary>
    public async Task<string?> GetTeamIdentifierAsync(
        string appPath,
        CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(
            "/usr/bin/codesign",
            new[] { "-dvv", appPath },
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);

        // codesign は情報を標準エラーへ出す
        return result.Succeeded ? ParseTeamIdentifier(result.CombinedOutput) : null;
    }

    /// <summary>
    /// codesign の出力から TeamIdentifier を取り出す。
    /// 該当行が無い・"not set"（アドホック署名など）・値が矛盾する場合は null。
    /// </summary>
    internal static string? ParseTeamIdentifier(string codesignOutput)
    {
        if (string.IsNullOrEmpty(codesignOutput)) return null;

        // codesign -dvv の出力には Identifier= など署名者が値を決められる行が
        // 混ざる。改行を仕込んだ Identifier で偽の TeamIdentifier= 行を置けるので、
        //   1. TeamIdentifier= 行は「形を問わず」すべて拾う
        //   2. 1 行でも Team ID の形（英数字 10 文字）でなければ null
        //      （本物が "not set"（アドホック署名）で偽の行だけが正しい形、という
        //      組み合わせを弾く。形の合う行だけ拾うと偽の行が唯一の一致になる）
        //   3. すべて同じ値のときだけ採用（矛盾は細工とみなして null）
        var matches = Regex.Matches(
            codesignOutput,
            @"^TeamIdentifier=(?<id>.*?)\r?$",
            RegexOptions.Multiline);

        if (matches.Count == 0) return null;

        string? id = null;
        foreach (Match m in matches)
        {
            var value = m.Groups["id"].Value;
            if (!Regex.IsMatch(value, "^[A-Z0-9]{10}$")) return null;
            if (id == null) id = value;
            else if (value != id) return null;
        }

        return id;
    }
}
