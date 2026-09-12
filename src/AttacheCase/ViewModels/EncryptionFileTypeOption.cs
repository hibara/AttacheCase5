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

using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AttacheCase.Models;

namespace AttacheCase.ViewModels;

/// <summary>
/// パスワード入力画面のコンボボックスで選ぶ「出力ファイルの種類」1 件。
/// ATC / EXE（自己実行形式・Windows 専用）/ ZIP の 3 種。
/// </summary>
public sealed class EncryptionFileTypeOption
{
    /// <summary><see cref="EncryptionFileTypes"/> の値（Atc=1 / Exe=2 / Zip=3）</summary>
    public int Type { get; }

    /// <summary>コンボボックスに出す短いラベル（"ATC" など）</summary>
    public string Label { get; }

    /// <summary>24x24 のアイコン。読み込めなければ null（ラベルだけ出る）</summary>
    public Bitmap? Icon { get; }

    private EncryptionFileTypeOption(int type, string label, string iconAsset)
    {
        Type = type;
        Label = label;
        Icon = TryLoadIcon(iconAsset);
    }

    private static Bitmap? TryLoadIcon(string iconAsset)
    {
        try
        {
            using var stream = AssetLoader.Open(
                new Uri($"avares://AttacheCase/Assets/Icons/ComboBox/{iconAsset}"));
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            // アイコンは飾り。読めなくてもラベルで選べる
            return null;
        }
    }

    /// <summary>
    /// 選べる種類の一覧を作る。EXE（自己実行形式）は Windows でしか出力できないため、
    /// Windows のときだけ含める（Exeout スタブが Windows 専用のため）。
    /// </summary>
    public static IReadOnlyList<EncryptionFileTypeOption> BuildAll()
    {
        var list = new List<EncryptionFileTypeOption>
        {
            // 関連付けアイコンは OS ごとに違う（Windows は sub_icon00、macOS は
            // 素材 _gazo/関連付けアイコン/mac_sub_icon の書類アイコン）
            new(EncryptionFileTypes.Atc, "ATC",
                OperatingSystem.IsMacOS() ? "sub_icon_mac_24x24.png" : "sub_icon_24x24.png"),
        };

        if (OperatingSystem.IsWindows())
        {
            list.Add(new(EncryptionFileTypes.Exe, "EXE", "exeout_icon_24x24.png"));
        }

        list.Add(new(EncryptionFileTypes.Zip, "ZIP", "password-zip_24x24.png"));
        return list;
    }
}
