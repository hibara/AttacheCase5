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

//----------------------------------------------------------------------
// テストからファイルの権限を扱うための小物。
//
// File.SetUnixFileMode は Windows で使えないため、解析器が CA1416 を出す。
// このプロジェクトは macOS 専用なので分岐は形式的なものだが、警告を
// 抑制する設定を足すより、呼び口をひとつにまとめるほうが素直。
//----------------------------------------------------------------------

using System;
using System.IO;

namespace AttacheCase.Tests;

internal static class TestFile
{
    public static void SetMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows()) return;

        File.SetUnixFileMode(path, mode);
    }

    /// <summary>所有者だけが読み書き実行できる状態にする。</summary>
    public static void MakeExecutable(string path) =>
        SetMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    /// <summary>書き込めない状態にする。</summary>
    public static void MakeReadOnly(string path) =>
        SetMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);

    /// <summary>読み書き実行できる状態に戻す。</summary>
    public static void MakeWritable(string path) =>
        SetMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}
