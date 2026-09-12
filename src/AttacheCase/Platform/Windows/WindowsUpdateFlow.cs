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
// Windows の更新の下ごしらえ。ダウンロード → 検証 まで。
// Windows 版 Updater\UpdateFlow.cs の移植。
//
// ここでは現在のアプリに一切触れない。差し替えはインストーラの仕事で、
// それを起動するのは LaunchInstaller（UAC 昇格つき、無人インストールにはしない。
// 暗号化ツールの入れ替えを黙って行うべきではない）。
//
// ★ ZIP（ポータブル）版はこの経路を通らない。展開先が利用者任せで
//   特定できないため、何も落とさずに配布ページをブラウザで開く（OpenDownloadPage）。
//
// ★ macOS 版の UpdateFlow（Services\UpdateFlow.cs）に対応するもの。
//   あちらは DMG から .app を取り出す段があるが、Windows は落としたものが
//   そのままインストーラなので「取り出し」の段は無い。
//
// セキュリティ（TOCTOU）: 検証したファイルを FileShare.Read で掴んだまま
// 起動する。%TEMP% は同じ利用者の別プロセスから書けるため、検証と起動の
// あいだに差し替えられると、UAC で昇格するインストーラのすり替え
// （利用者→管理者への持ち上げ）になる。マニフェストの SHA-256 とも
// 突き合わせ、正しく署名された「古い版」への引き下げも塞ぐ。
//----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.Platform.Windows;

/// <summary>検証まで通った、実行を待つばかりの状態。掴んだハンドルを持つ。</summary>
public sealed class WindowsUpdatePreparation : IDisposable
{
    private FileStream? _hold;

    /// <summary>検証を通したインストーラ。</summary>
    public string VerifiedInstallerPath { get; }

    /// <summary>後始末してよい作業場所。</summary>
    public string WorkDirectory { get; }

    public WindowsUpdatePreparation(string verifiedInstallerPath, string workDirectory, FileStream hold)
    {
        VerifiedInstallerPath = verifiedInstallerPath;
        WorkDirectory = workDirectory;
        _hold = hold;
    }

    public void Dispose()
    {
        if (_hold == null) return;

        try
        {
            _hold.Dispose();
        }
        catch (Exception)
        {
            // 放すのに失敗しても、更新の流れは止めない
        }

        _hold = null;
    }
}

[SupportedOSPlatform("windows")]
public class WindowsUpdateFlow
{
    /// <summary>作業ディレクトリの名前の頭。起動時の後始末（UpdateInstaller 側）が拾う。</summary>
    private const string WorkPrefix = "attachecase-update-";

    /// <summary>落としたインストーラの拡張子。ShellExecute で起動するので .exe 以外は不可。</summary>
    internal const string InstallerExtension = ".exe";

    private readonly UpdateDownloader _downloader;
    private readonly WindowsPackageVerifier _verifier;
    private readonly string _workRoot;

    public WindowsUpdateFlow(
        UpdateDownloader? downloader = null,
        WindowsPackageVerifier? verifier = null,
        string? workRoot = null)
    {
        _downloader = downloader ?? new UpdateDownloader();
        _verifier = verifier ?? new WindowsPackageVerifier();
        _workRoot = workRoot ?? Path.GetTempPath();
    }

    /// <summary>
    /// インストーラを落として検証する。現在のアプリには触れない。
    /// </summary>
    public async Task<WindowsUpdatePreparation> PrepareAsync(
        UpdateBuild build,
        IProgress<UpdateStage>? onStage = null,
        IProgress<DownloadProgress>? onDownloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);

        // 期待する署名者を、動作中の自分から読む（定数で持たないので証明書更新に追随できる）。
        // 読めない（開発ビルドなど署名されていない）場合は進まない
        var expectedSigner = _verifier.GetRunningAppSigner();
        if (string.IsNullOrEmpty(expectedSigner))
        {
            throw new InvalidOperationException(
                LocalizationService.Instance["Update_WinVerify_SignerUnavailable"]);
        }

        var workDirectory = Path.Combine(_workRoot, WorkPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        FileStream? hold = null;

        try
        {
            onStage?.Report(UpdateStage.Downloading);

            // 保存名の拡張子は必ず .exe にする。ダウンローダーの既定は Mac の .dmg で、
            // そのまま使うと update.dmg として保存され、LaunchInstaller の ShellExecute が
            // インストーラではなく「どのアプリで開きますか」を出す（2026-09-12 の通し試験で実測）
            var installerPath = await _downloader
                .DownloadAsync(build, workDirectory, onDownloadProgress, cancellationToken,
                    expectedExtension: InstallerExtension)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            onStage?.Report(UpdateStage.Verifying);

            // 検証する前に掴む。検証してから掴むと、その隙に差し替えられる
            hold = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // 掴んだハンドルから、マニフェストの大きさとハッシュを数え直す
            //（パスで開き直すと「数えたファイル」と「掴んでいるファイル」が別物になりうる）
            VerifyAgainstManifest(hold, build);

            var verification = _verifier.Verify(installerPath, expectedSigner);
            if (!verification.Ok)
            {
                throw new InvalidOperationException(DescribeFailure(verification));
            }

            var preparation = new WindowsUpdatePreparation(installerPath, workDirectory, hold);
            hold = null;
            return preparation;
        }
        catch (Exception)
        {
            // 掴んだままだと、この下の後始末で消せない
            hold?.Dispose();
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    /// <summary>
    /// 検証済みのインストーラを起動する。成功したら呼び出し側はアプリを終了する。
    /// UAC を出すために UseShellExecute が要る。/SILENT にはしない。
    /// 掴んでいたファイルは呼び出し側が放す（ShellExecuteEx は UAC の同意を経て
    /// プロセスが作られてから戻るので、抜けた時点で差し替えの隙は無い）。
    /// </summary>
    public static void LaunchInstaller(WindowsUpdatePreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        var psi = new ProcessStartInfo
        {
            FileName = preparation.VerifiedInstallerPath,
            UseShellExecute = true,
        };

        Process.Start(psi);
    }

    /// <summary>
    /// 配布ページをブラウザで開く。ZIP（ポータブル）版の更新はこれだけ。
    /// https 以外は開かない（マニフェスト由来の値を ShellExecute へ渡すため）。
    /// </summary>
    public static void OpenDownloadPage(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // ブラウザが開けなくても、アプリの動作は止めない
        }
    }

    /// <summary>検証に失敗した理由を、そのまま利用者に見せられる文にする。</summary>
    internal static string DescribeFailure(WindowsVerificationResult verification)
    {
        var localize = LocalizationService.Instance;
        var reason = verification.Failure switch
        {
            WindowsVerificationFailure.FileNotFound => localize["Update_WinVerify_FileNotFound"],
            WindowsVerificationFailure.NotSigned => localize["Update_WinVerify_NotSigned"],
            WindowsVerificationFailure.SignatureInvalid => localize["Update_WinVerify_SignatureInvalid"],
            WindowsVerificationFailure.SignerMismatch => localize["Update_WinVerify_SignerMismatch"],
            WindowsVerificationFailure.SignerUnavailable => localize["Update_WinVerify_SignerUnavailable"],
            _ => localize["Update_WinVerify_Failed"],
        };

        return string.IsNullOrEmpty(verification.Detail)
            ? reason
            : reason + Environment.NewLine + Environment.NewLine + verification.Detail;
    }

    /// <summary>
    /// 掴んだままのハンドルから、大きさと SHA-256 をマニフェストと突き合わせる。
    /// 食い違ったら UpdateContentMismatchException。位置は呼び出し前へ戻す。
    /// </summary>
    internal static void VerifyAgainstManifest(FileStream hold, UpdateBuild build)
    {
        var position = hold.Position;

        try
        {
            if (hold.Length != build.Size)
            {
                throw new UpdateContentMismatchException(
                    $"配信された大きさが違います（期待: {build.Size} / 実際: {hold.Length}）。");
            }

            hold.Position = 0;

            var actual = Convert.ToHexString(SHA256.HashData(hold)).ToLowerInvariant();
            if (!string.Equals(actual, (build.Sha256 ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateContentMismatchException(
                    $"SHA-256 が一致しません（期待: {build.Sha256} / 実際: {actual}）。");
            }
        }
        finally
        {
            hold.Position = position;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception)
        {
            // 次回起動時の CleanUpStaleWorkDirectories で回収される
        }
    }
}
