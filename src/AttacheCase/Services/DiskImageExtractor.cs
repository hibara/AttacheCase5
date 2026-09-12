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
// DMG をマウントして、中の .app を取り出す。
//
// マウント点は自前で決めて -nobrowse で開く。Finder に見せず、既定の
// /Volumes/<名前> も使わない。同名のボリュームが既にあると番号が付いて別の場所に
// マウントされ、取り違えるおそれがあるため。
//
// 取り出しは ditto を使う。cp では拡張属性や署名に必要な情報が落ちることがある。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AttacheCase.Services;

public class DiskImageExtractor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    private readonly IProcessRunner _runner;

    public DiskImageExtractor(IProcessRunner? runner = null)
    {
        _runner = runner ?? new ProcessRunner();
    }

    /// <summary>
    /// DMG をマウントして、中の .app を <paramref name="destinationDirectory"/> へ取り出す。
    /// 成否にかかわらず、最後に必ずアンマウントする。
    /// </summary>
    /// <returns>取り出した .app のパス。</returns>
    public async Task<string> ExtractAppAsync(
        string dmgPath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(dmgPath))
        {
            throw new FileNotFoundException("DMG が見つかりません。", dmgPath);
        }

        Directory.CreateDirectory(destinationDirectory);

        // マウント点は自前で用意する。/Volumes は他と衝突しうる。
        // 接頭辞は作業場所（attachecase-update-）と分ける。用途が違うものを
        // 同じ名前にすると、数えたり消したりするときに取り違える
        var mountPoint = Path.Combine(
            Path.GetTempPath(), $"attachecase-mount-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mountPoint);

        try
        {
            await AttachAsync(dmgPath, mountPoint, cancellationToken).ConfigureAwait(false);

            var sourceApp = FindSingleApp(mountPoint);
            var destinationApp = Path.Combine(destinationDirectory, Path.GetFileName(sourceApp));

            // cp ではなく ditto。拡張属性や署名に要る情報を保ったまま複製する
            var ditto = await _runner.RunAsync(
                "/usr/bin/ditto",
                new[] { sourceApp, destinationApp },
                CommandTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!ditto.Succeeded)
            {
                throw new InvalidOperationException(
                    $"DMG から取り出せませんでした: {ditto.CombinedOutput.Trim()}");
            }

            return destinationApp;
        }
        finally
        {
            await DetachAsync(mountPoint, cancellationToken).ConfigureAwait(false);
            TryDeleteDirectory(mountPoint);
        }
    }

    private async Task AttachAsync(
        string dmgPath, string mountPoint, CancellationToken cancellationToken)
    {
        var attach = await _runner.RunAsync(
            "/usr/bin/hdiutil",
            new[]
            {
                "attach", dmgPath,
                "-mountpoint", mountPoint,
                "-nobrowse",      // Finder に出さない
                "-readonly",
                "-noautoopen",
                "-quiet",
            },
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);

        if (!attach.Succeeded)
        {
            throw new InvalidOperationException(
                $"DMG をマウントできませんでした: {attach.CombinedOutput.Trim()}");
        }
    }

    /// <summary>
    /// アンマウントする。使用中で失敗することがあるためリトライし、
    /// それでも駄目なら強制的に外す。
    /// </summary>
    private async Task DetachAsync(string mountPoint, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var detach = await _runner.RunAsync(
                    "/usr/bin/hdiutil",
                    new[] { "detach", mountPoint, "-quiet" },
                    CommandTimeout,
                    CancellationToken.None).ConfigureAwait(false);

                if (detach.Succeeded) return;
            }
            catch (Exception)
            {
                // 次の手を試す
            }

            if (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        try
        {
            await _runner.RunAsync(
                "/usr/bin/hdiutil",
                new[] { "detach", mountPoint, "-force", "-quiet" },
                CommandTimeout,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // マウントが残る可能性はあるが、ここで処理を止めはしない
        }
    }

    /// <summary>
    /// マウントしたボリュームから .app をひとつ選ぶ。
    /// ひとつに定まらない DMG は、こちらが作ったものではないので受け付けない。
    /// </summary>
    internal static string FindSingleApp(string mountPoint)
    {
        var apps = Directory
            .GetDirectories(mountPoint, "*.app", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        return apps.Length switch
        {
            1 => apps[0],
            0 => throw new InvalidOperationException("DMG の中に .app が見つかりません。"),
            _ => throw new InvalidOperationException(
                $"DMG の中に .app が {apps.Length} 個あります。どれを使うか決められません。"),
        };
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: false);
        }
        catch (Exception)
        {
            // 残っても実害は無い（一時ディレクトリ）
        }
    }
}
