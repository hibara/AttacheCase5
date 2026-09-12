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
// 更新の下ごしらえ。ダウンロード → DMG から取り出し → 検証 までを順に行う。
//
// ここでは既存の .app に一切触れない。差し替えは UpdateInstaller の仕事。
// 途中で失敗したり中止されたりしたら、作った一時ディレクトリごと片付ける。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;

namespace AttacheCase.Services;

/// <summary>どこまで進んだか。画面の文言を切り替えるために使う。</summary>
public enum UpdateStage
{
    Downloading,
    Extracting,
    Verifying,
}

/// <summary>検証まで通った、差し替えを待つばかりの状態。</summary>
/// <param name="VerifiedAppPath">検証を通した .app。</param>
/// <param name="WorkDirectory">差し替え後に消してよい作業場所。</param>
public sealed record UpdatePreparation(string VerifiedAppPath, string WorkDirectory);

public class UpdateFlow
{
    private readonly UpdateDownloader _downloader;
    private readonly DiskImageExtractor _extractor;
    private readonly UpdatePackageVerifier _verifier;
    private readonly string _workRoot;

    /// <param name="workRoot">
    /// 作業場所を作る親ディレクトリ。既定は一時ディレクトリ。
    /// テストから専用の場所を渡して、並行して走る他のテストと混ざらないようにできる。
    /// </param>
    public UpdateFlow(
        UpdateDownloader? downloader = null,
        DiskImageExtractor? extractor = null,
        UpdatePackageVerifier? verifier = null,
        string? workRoot = null)
    {
        _downloader = downloader ?? new UpdateDownloader();
        _extractor = extractor ?? new DiskImageExtractor();
        _verifier = verifier ?? new UpdatePackageVerifier();
        _workRoot = workRoot ?? Path.GetTempPath();
    }

    /// <summary>
    /// 差し替えられる状態まで用意する。既存の .app には触れない。
    /// </summary>
    /// <param name="build">検証済みマニフェストに書かれていた配布物。</param>
    /// <param name="targetBundlePath">
    /// 差し替え先。ここから期待する TeamIdentifier を読む。
    /// </param>
    /// <param name="onStage">段階が変わったときの通知。</param>
    /// <param name="onDownloadProgress">ダウンロードの進み具合。</param>
    public async Task<UpdatePreparation> PrepareAsync(
        UpdateBuild build,
        string targetBundlePath,
        Action<UpdateStage>? onStage = null,
        IProgress<DownloadProgress>? onDownloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);

        // 差し替え先から「あるべき署名者」を読む。定数で持たず実物から取ることで、
        // 証明書を更新しても追随できる。読めない（署名されていない）場合は進まない
        var expectedTeamId = await _verifier
            .GetTeamIdentifierAsync(targetBundlePath, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(expectedTeamId))
        {
            throw new InvalidOperationException(
                "動作中のアプリの署名を確認できないため、更新を適用できません。");
        }

        var workDirectory = Path.Combine(
            _workRoot, $"attachecase-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);

        try
        {
            onStage?.Invoke(UpdateStage.Downloading);
            var dmgPath = await _downloader
                .DownloadAsync(build, workDirectory, onDownloadProgress, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            onStage?.Invoke(UpdateStage.Extracting);
            var extractDirectory = Path.Combine(workDirectory, "extracted");
            var appPath = await _extractor
                .ExtractAppAsync(dmgPath, extractDirectory, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            onStage?.Invoke(UpdateStage.Verifying);
            var verification = await _verifier
                .VerifyAsync(appPath, expectedTeamId, cancellationToken)
                .ConfigureAwait(false);

            if (!verification.Ok)
            {
                throw new InvalidOperationException(
                    DescribeFailure(verification));
            }

            // DMG はもう要らない。差し替えのときに一緒に消えるが、
            // ここで先に消しておけば一時領域の使用量が半分で済む
            TryDelete(dmgPath);

            return new UpdatePreparation(appPath, workDirectory);
        }
        catch (Exception)
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    /// <summary>
    /// 検証に失敗した理由を、そのまま利用者に見せられる文にする。
    /// </summary>
    internal static string DescribeFailure(VerificationResult verification)
    {
        var localize = LocalizationService.Instance;

        var reason = verification.Failure switch
        {
            VerificationFailure.SignatureInvalid => localize["Update_Verify_SignatureInvalid"],
            VerificationFailure.TeamIdMismatch => localize["Update_Verify_TeamIdMismatch"],
            VerificationFailure.TeamIdUnavailable => localize["Update_Verify_TeamIdMismatch"],
            VerificationFailure.NotNotarized => localize["Update_Verify_NotNotarized"],
            VerificationFailure.GatekeeperRejected => localize["Update_Verify_Rejected"],
            _ => localize["Update_Verify_Failed"],
        };

        return string.IsNullOrEmpty(verification.Detail)
            ? reason
            : reason + "\n\n" + verification.Detail;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // 消せなくても差し替えのときに片付く
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception)
        {
            // 起動時の CleanUpStaleWorkDirectories で回収される
        }
    }
}
