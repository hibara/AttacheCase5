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
// 暗号化・復号が終わったあとの後始末。
//   - 元ファイル／暗号化ファイルの削除（設定「削除」・引数 /del, /delenc）
//   - 復号したファイルを関連付けられたアプリで開く（/opf）
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AttacheCase.Cli;
using AttacheCase.Models;

namespace AttacheCase.Services;

public static class PostProcessHelper
{
    /// <summary>復号後にまとめて開くファイルの上限（開きすぎないように）</summary>
    private const int MaxOpenItems = 10;

    /// <summary>
    /// 暗号化に成功したあと、設定に従って元ファイルを削除する。
    /// </summary>
    public static Task DeleteOriginalsAsync(IEnumerable<string> paths, RunContext context)
    {
        return DeleteAsync(paths, context, context.Settings.DeleteMode);
    }

    /// <summary>
    /// 復号に成功したあと、設定に従って暗号化ファイルを削除する。
    /// </summary>
    public static Task DeleteEncryptedFileAsync(string path, RunContext context)
    {
        return DeleteAsync([path], context, context.Settings.DeleteEncryptedFileMode);
    }

    private static async Task DeleteAsync(IEnumerable<string> paths, RunContext context, int mode)
    {
        if (mode <= 0) return;

        var targets = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (targets.Count == 0) return;

        var localize = LocalizationService.Instance;

        // 削除確認（無人実行では確認できないため、指示どおり削除する）
        if (context.Settings.ConfirmDelete && !context.IsUnattended)
        {
            var names = targets.Count == 1
                ? Path.GetFileName(targets[0].TrimEnd(Path.DirectorySeparatorChar))
                : localize.GetString("Dialog_DeleteCountFiles", targets.Count);

            var confirmed = await DialogHelper.ConfirmAsync(
                false,
                localize["Dialog_DeleteTitle"],
                localize.GetString("Dialog_DeleteMessage", names));

            if (!confirmed) return;
        }

        // 設定ファイルの破損や将来の値の追加で未定義の番号が入ると、
        // 「ゴミ箱へ移動」のつもりが復元できない削除になりかねない。手前で弾く
        if (!Enum.IsDefined(typeof(FileEraser.Mode), mode))
        {
            Console.Error.WriteLine($"Unknown delete mode: {mode}. Skipped deletion.");
            return;
        }

        foreach (var target in targets)
        {
            var deleted = FileEraser.Erase(
                target,
                (FileEraser.Mode)mode,
                context.Settings.DeleteRandomTimes,
                context.Settings.DeleteZeroTimes);

            if (!deleted)
            {
                Console.Error.WriteLine(localize.GetString("Dialog_DeleteFailed", target));
            }
        }
    }

    /// <summary>
    /// 復号したファイルを、関連付けられたアプリで開く（設定「復号後に開く」・引数 /opf）。
    /// </summary>
    /// <param name="outputDirectory">展開先</param>
    /// <param name="relativePaths">暗号化ファイルに記録されていた相対パス</param>
    public static void OpenDecryptedItems(
        RunContext context, string outputDirectory, IEnumerable<string> relativePaths)
    {
        if (!context.Settings.OpenDecryptedFile) return;

        // 展開されたものの最上位だけを開く（入れ子のファイルを個別に開かない）
        var topLevel = relativePaths
            .Select(p => p.Replace('\\', '/').TrimEnd('/'))
            .Where(p => p.Length > 0)
            .Select(p => p.Split('/')[0])
            .Distinct()
            .Take(MaxOpenItems)
            .Select(name => Path.Combine(outputDirectory, name))
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToList();

        if (topLevel.Count == 0)
        {
            topLevel.Add(outputDirectory);
        }

        foreach (var path in topLevel)
        {
            Open(path);
        }
    }

    /// <summary>
    /// macOS の open コマンドで開く。関連付けの解決は OS に任せる。
    /// </summary>
    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                ArgumentList = { path },
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not open: " + path + " (" + ex.Message + ")");
        }
    }
}
