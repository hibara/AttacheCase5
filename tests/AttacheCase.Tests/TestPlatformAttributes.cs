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
// OS 依存テストのガード属性。
//
// クロスプラットフォーム化に伴い、macOS でしか意味を持たないテスト
// （.app バンドル・ditto 差し替えスクリプト・キーチェーン等）と、
// 実行環境の権限に依存するテスト（Windows のシンボリックリンク作成は
// 開発者モードか管理者権限が要る）を「失敗」ではなく「スキップ」として
// 見えるようにする。早期 return だと通ったのかスキップされたのか
// 結果から区別できない。
//----------------------------------------------------------------------

using System;
using System.IO;
using Xunit;

namespace AttacheCase.Tests;

/// <summary>macOS でのみ実行する [Fact]。他 OS ではスキップ表示になる。</summary>
public sealed class MacOsFactAttribute : FactAttribute
{
    public MacOsFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "macOS のみ（.app バンドル / シェルスクリプト / キーチェーンに依存）";
        }
    }
}

/// <summary>macOS でのみ実行する [Theory]。他 OS ではスキップ表示になる。</summary>
public sealed class MacOsTheoryAttribute : TheoryAttribute
{
    public MacOsTheoryAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "macOS のみ（.app バンドル / シェルスクリプト / キーチェーンに依存）";
        }
    }
}

/// <summary>
/// シンボリックリンクを作成できる環境でのみ実行する [Fact]。
/// Windows では開発者モードが無効かつ非管理者だと作成自体が
/// IOException になるため、その環境ではスキップする。
/// </summary>
public sealed class SymlinkFactAttribute : FactAttribute
{
    private static readonly bool CanCreateSymlinks = ProbeSymlinkSupport();

    public SymlinkFactAttribute()
    {
        if (!CanCreateSymlinks)
        {
            Skip = "この環境ではシンボリックリンクを作成できない" +
                   "（Windows は開発者モードを有効にするか管理者で実行すると走る）";
        }
    }

    private static bool ProbeSymlinkSupport()
    {
        var link = Path.Combine(Path.GetTempPath(), "ac5-symlink-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.CreateSymbolicLink(link, Path.Combine(Path.GetTempPath(), "ac5-symlink-probe-target"));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(link); } catch { /* probe の後始末に失敗しても支障はない */ }
        }
    }
}
