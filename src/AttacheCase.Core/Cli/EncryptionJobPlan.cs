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
// 複数の入力を「1 回の暗号化で扱う単位（ジョブ）」に分け、各ジョブの出力先を決める。
// GUI（ATC / パスワード ZIP）と CLI が同じ規則になるように、ここに 1 つだけ置く。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.Cli;

/// <summary>1 回の暗号化で扱う入力の組と、その出力先</summary>
public sealed record EncryptionJob(string[] InputPaths, string OutputPath);

public static class EncryptionJobPlan
{
    /// <summary>
    /// 設定「複数ファイルの場合」（旧版と同じ 3 択）に従って入力をジョブに分け、出力先を一意に決める。
    /// <list type="bullet">
    /// <item>一つにまとめる（/allpack）: すべてを 1 つの暗号化ファイルに。通常モードでも入力が 1 つ、
    /// または /out=&lt;ファイル名&gt; で出力が 1 つに明示されていれば同じ（旧 5.0 CLI の /list + /out と同じ＝まとめる意図）</item>
    /// <item>通常（既定）: 入力 1 つにつき 1 つ（フォルダーはサブフォルダーも含めてフォルダー単位）</item>
    /// <item>一つずつ（/oneby）: フォルダーはサブフォルダーも含めて展開し、ファイル 1 つにつき 1 つ</item>
    /// </list>
    /// 複数ジョブのときは、同じ実行内で出力先が重ならないように連番を付け（別フォルダーの同名ファイルや
    /// report.txt と report.pdf が同じ report.atc に写像されるのを防ぐ）、入力そのものが出力先に選ばれない
    /// ように全入力を先に予約する（対象に同梱された foo.atc を処理前に上書きしない）。
    /// </summary>
    /// <param name="inputPaths">暗号化の対象。存在しないものは除く</param>
    /// <param name="context">この実行の設定と条件</param>
    /// <param name="outputPathFor">入力の組から出力先を決める（CliOutputPath.ForEncryption / ForZip）</param>
    /// <exception cref="InvalidOperationException">一つずつ（/oneby）なのに /out=&lt;ファイル名&gt; で出力が 1 つに定まるとき</exception>
    public static List<EncryptionJob> Build(
        IEnumerable<string> inputPaths, RunContext context, Func<string[], string> outputPathFor)
    {
        var inputs = inputPaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (inputs.Length == 0) return [];

        var settings = context.Settings;
        var outputIsSingleFile = !string.IsNullOrEmpty(context.OutputPath) &&
                                 !Directory.Exists(Path.GetFullPath(context.OutputPath));

        if (settings.AllFilePack || (!settings.EncryptOneByOne && (inputs.Length == 1 || outputIsSingleFile)))
        {
            return [new EncryptionJob(inputs, ResolveSingleOutput(inputs, outputPathFor(inputs), outputIsSingleFile))];
        }

        // 一つずつ処理するのに出力先が 1 つのファイルに定まるのは矛盾する
        if (outputIsSingleFile)
        {
            throw new InvalidOperationException(LocalizationService.Instance["Error_OneByOneWithOutFile"]);
        }

        var units = new List<string>();
        foreach (var path in inputs)
        {
            if (settings.EncryptOneByOne && Directory.Exists(path))
            {
                units.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order());
            }
            else
            {
                units.Add(path);
            }
        }

        var reservation = new OutputPathReservation();
        reservation.Reserve(units);

        return units
            .Select(unit => new EncryptionJob([unit], reservation.MakeUnique(outputPathFor([unit]))))
            .ToList();
    }

    /// <summary>
    /// 1 つにまとめるジョブの出力先が入力そのものと同じにならないようにする。
    /// 偽装拡張子に入力と同じ拡張子（photo.jpg → photo.jpg）を指定したときなどに、EncryptionService が
    /// 入力一覧から同一パスを除いたうえで FileMode.Create で開き、元ファイルを切り詰めてしまう
    /// （復元できない損失。CodeRabbit PR#20）。
    /// 出力先が暗黙（フォルダー指定か未指定）なら連番で避け、/out=&lt;ファイル名&gt; で明示された
    /// 出力が入力と同じなら矛盾としてエラーにする。
    /// </summary>
    /// <exception cref="InvalidOperationException">明示された出力先が入力ファイルと同じとき</exception>
    public static string ResolveSingleOutput(IEnumerable<string> inputs, string outputPath, bool outputIsExplicitFile)
    {
        var reservation = new OutputPathReservation();
        reservation.Reserve(inputs);

        if (!outputIsExplicitFile) return reservation.MakeUnique(outputPath);

        if (reservation.MakeUnique(outputPath) != outputPath)
        {
            throw new InvalidOperationException(
                LocalizationService.Instance.GetString("Error_OutputEqualsInput", outputPath));
        }

        return outputPath;
    }
}

/// <summary>
/// 同じ実行内で使う出力先の集まり。入力そのもの（フォルダーならその中の既存ファイルも）と、
/// 先に決めた出力先を避けて一意な名前を作る。
/// 入力と無関係なディスク上の既存ファイルとの衝突は見ない（それは上書き確認 /ow の管轄）。
/// </summary>
public sealed class OutputPathReservation
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly HashSet<string> _used = new(StringComparer.FromComparison(PathComparison));

    /// <summary>予約した入力フォルダー（末尾は区切り文字）。この中の既存ファイルは入力の一部</summary>
    private readonly List<string> _directories = [];

    /// <summary>入力パスを予約する（出力先に選ばれないようにする）</summary>
    public void Reserve(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            _used.Add(full);

            // 入力フォルダーの中にある既存ファイルも入力の一部。出力先がそこに重なると、暗号化サービスが
            // 入力一覧から同一パスを除いたうえで開き、元ファイルを失う（CodeRabbit PR#20）。
            // 全ファイルを列挙して予約する代わりに、候補がフォルダー配下で実在するかをその場で見る
            if (Directory.Exists(full))
            {
                // ルート（C:\ や /）は TrimEndingDirectorySeparator が区切りを残すので、重ねて付けない
                // （C:\\ になると配下のパスが前方一致せず検査が素通りする。CodeRabbit PR#20）
                _directories.Add(Path.EndsInDirectorySeparator(full) ? full : full + Path.DirectorySeparatorChar);
            }
        }
    }

    /// <summary>
    /// 出力先が入力・先に決めた出力先・入力フォルダー内の既存ファイルのどれかと重なっていれば、
    /// 拡張子の前に _2, _3, ... を付けて一意にする。返したパスは予約済みになる。
    /// </summary>
    public string MakeUnique(string path)
    {
        if (TryReserve(Path.GetFullPath(path))) return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, name + "_" + i + extension);
            if (TryReserve(Path.GetFullPath(candidate))) return candidate;
        }
    }

    /// <summary>完全パスを予約する。入力フォルダー内に実在するファイルは入力の一部なので予約できない</summary>
    private bool TryReserve(string fullPath)
    {
        if (IsExistingFileInsideReservedDirectory(fullPath)) return false;
        return _used.Add(fullPath);
    }

    private bool IsExistingFileInsideReservedDirectory(string fullPath)
    {
        foreach (var directory in _directories)
        {
            if (fullPath.StartsWith(directory, PathComparison) && File.Exists(fullPath)) return true;
        }
        return false;
    }
}
