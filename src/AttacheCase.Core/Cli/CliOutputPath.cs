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
// 設定とコマンドライン指定に従って暗号化ファイルの出力先を決める。
// パスワード方式・RSA・Post-PPAP のどの経路からも同じ規則で決まるように、
// ここに 1 つだけ実装を置く。
//----------------------------------------------------------------------

using System;
using System.IO;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.Cli;

public static class CliOutputPath
{
    /// <summary>
    /// 暗号化ファイルの出力先を決める。
    /// 優先順位は /out（ファイル名まで） → /out（フォルダー） → 保存先の設定（/saveto）
    /// → 入力と同じフォルダー。
    /// </summary>
    /// <param name="inputPaths">暗号化の対象（先頭のパスから既定のファイル名を作る）</param>
    /// <param name="context">この実行の設定と条件</param>
    /// <param name="defaultExtension">拡張子の偽装が無いときに付ける拡張子</param>
    /// <exception cref="InvalidOperationException">指定に矛盾があるとき</exception>
    public static string ForEncryption(string[] inputPaths, RunContext context, string defaultExtension = ".atc")
    {
        var settings = context.Settings;
        var targetPath = inputPaths[0];
        var directory = Path.GetDirectoryName(targetPath);
        var filename = Path.GetFileNameWithoutExtension(targetPath);

        // ディレクトリの場合は、ディレクトリ名を使用
        if (Directory.Exists(targetPath))
        {
            directory = Path.GetDirectoryName(targetPath.TrimEnd(Path.DirectorySeparatorChar));
            filename = new DirectoryInfo(targetPath).Name;
        }
        else if (settings.IncludeOriginalExtension)
        {
            // 元の拡張子を残す（report.xlsx → report.xlsx.atc）
            filename = Path.GetFileName(targetPath);
        }

        if (string.IsNullOrEmpty(directory))
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        //-----------------------------------
        // 出力先フォルダー
        //-----------------------------------
        if (!string.IsNullOrEmpty(context.OutputPath))
        {
            if (Directory.Exists(context.OutputPath))
            {
                directory = context.OutputPath;
            }
            else if (settings.EncryptOneByOne)
            {
                // 1 ファイルずつ処理するのに出力先が 1 つのファイルに定まるのは矛盾する
                throw new InvalidOperationException(
                    LocalizationService.Instance["Error_OneByOneWithOutFile"]);
            }
            else
            {
                // ファイル名まで指定されている場合は、自動命名も拡張子偽装も適用しない
                return context.OutputPath;
            }
        }
        else if (settings.SaveToSameFolder && Directory.Exists(settings.SaveToFolderPath))
        {
            directory = settings.SaveToFolderPath;
        }

        //-----------------------------------
        // 拡張子（偽装の指定があればそれを使う）
        //-----------------------------------
        var extension = settings.UseCamouflageExtension && !string.IsNullOrEmpty(settings.CamouflageExtension)
            ? settings.CamouflageExtension
            : defaultExtension;

        var candidate = Path.Combine(directory, filename + extension);

        //-----------------------------------
        // 自動ファイル名
        //-----------------------------------
        if (settings.UseAutoName && !string.IsNullOrEmpty(settings.AutoNameFormat))
        {
            var generated = AutoFileName.Format(
                settings.AutoNameFormat, candidate, AutoFileName.CharsetFrom(settings), out var containsInvalidChar);

            if (containsInvalidChar)
            {
                throw new InvalidOperationException(
                    LocalizationService.Instance.GetString("Error_AutoNameInvalidChar", generated));
            }

            // 展開結果が空（書式が <filename> のみで、入力名が ".atc" のような
            // 拡張子だけの場合など）だと Path.Combine がディレクトリを返して
            // しまうため、既定の名前のまま進める（Cli/Program.cs と同じ扱い）
            if (!string.IsNullOrEmpty(generated))
            {
                candidate = Path.Combine(directory, generated);
            }
        }

        return candidate;
    }

    /// <summary>
    /// パスワード ZIP の出力先を決める。/zipsaveto の設定を優先する点だけが異なる。
    /// </summary>
    public static string ForZip(string[] inputPaths, RunContext context)
    {
        var settings = context.Settings;

        if (string.IsNullOrEmpty(context.OutputPath) &&
            settings.ZipSaveToSameFolder && Directory.Exists(settings.ZipSaveToFolderPath))
        {
            // ZIP 専用の保存先を、暗号化ファイルの保存先として一時的に差し替える
            var zipSettings = settings.Clone();
            zipSettings.SaveToSameFolder = true;
            zipSettings.SaveToFolderPath = settings.ZipSaveToFolderPath;

            return ForEncryption(
                inputPaths,
                new RunContext
                {
                    Settings = zipSettings,
                    IsCommandLineRun = context.IsCommandLineRun,
                    OutputPath = context.OutputPath,
                },
                ".zip");
        }

        return ForEncryption(inputPaths, context, ".zip");
    }

    /// <summary>
    /// 復号したファイルの展開先を決める。
    /// 優先順位は /out → 保存先の設定（/dsaveto） → 暗号化ファイルと同じフォルダー。
    /// </summary>
    public static string ForDecryption(string encryptedFilePath, RunContext context)
    {
        if (!string.IsNullOrEmpty(context.OutputPath))
        {
            return context.OutputPath;
        }

        var settings = context.Settings;
        if (settings.DecryptSaveToSameFolder && Directory.Exists(settings.DecryptSaveToFolderPath))
        {
            return settings.DecryptSaveToFolderPath;
        }

        var directory = Path.GetDirectoryName(encryptedFilePath);
        return string.IsNullOrEmpty(directory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : directory;
    }
}
