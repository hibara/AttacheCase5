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
// Windows の関連付け（AtcSetup が書くレジストリ）を読む側。
//
// 設定画面の「.atc に表示するアイコン」は、AtcSetup で関連付けたときに実際に
// 登録されたアイコンを映すべきもの。settings.json の値だけを見ていると、
// 「アイコンを選んで『関連付けする』を押したが『保存』は押していない」ときに
// 次回の設定画面が別のアイコンを選択状態にしてしまう（2026-09-12 利用者報告）。
// そこで HKCR\AttacheCase.DataFile\DefaultIcon の実値から「同梱の atcN.ico の
// どれか」を読み取り、設定画面の初期値と、関連付け直後の保存に使う。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AttacheCase.Platform;

public static partial class WindowsFileAssociation
{
    /// <summary>AtcSetup が .atc の ProgID として登録する名前</summary>
    public const string AtcProgId = "AttacheCase.DataFile";

    /// <summary>
    /// DefaultIcon の値（例: "C:\Program Files\AttacheCase5\Icons\atc3.ico" や
    /// "...\AttacheCase.exe",0）から、同梱アイコンの番号（1〜4）を取り出す。
    /// 同梱アイコンでなければ false。
    /// </summary>
    public static bool TryParseIconIndex(string? defaultIconValue, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(defaultIconValue)) return false;

        // 引用符と ",<番号>" を落としてパスだけにする
        var value = defaultIconValue.Trim();
        var comma = value.LastIndexOf(',');
        if (comma > 0 && int.TryParse(value[(comma + 1)..].Trim().Trim('"'), out _))
        {
            value = value[..comma];
        }
        value = value.Trim().Trim('"');

        // レジストリの値は Windows のパスなので、区切りは '\' と '/' の両方を自前で見る
        // （Path.GetFileName は macOS / Linux では '\' を区切りと見なさず、テストが落ちる）
        var sep = value.LastIndexOfAny(['\\', '/']);
        var name = sep >= 0 ? value[(sep + 1)..] : value;
        var m = AtcIconName().Match(name);
        if (!m.Success) return false;

        index = int.Parse(m.Groups[1].Value);
        return index is >= 1 and <= 4;
    }

    /// <summary>
    /// 現在 .atc に登録されている同梱アイコンの番号（1〜4）。未登録・同梱以外・
    /// 読めないときは null。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static int? ReadRegisteredAtcIconIndex()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(AtcProgId + @"\DefaultIcon", writable: false);
            var value = key?.GetValue("") as string;
            return TryParseIconIndex(value, out var index) ? index : null;
        }
        catch (Exception)
        {
            // 権限やレジストリの破損。関連付けの状態が分からないだけなので、無いものとして扱う
            return null;
        }
    }

    [GeneratedRegex(@"^atc([1-4])\.ico$", RegexOptions.IgnoreCase)]
    private static partial Regex AtcIconName();
}
