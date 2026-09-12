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
// 起動時に解析したコマンドライン引数を、GUI 側（App → MainWindowViewModel）へ
// 受け渡すための置き場。Program.Main で組み立て、ウィンドウが開いた直後に
// 一度だけ取り出して適用する。
//----------------------------------------------------------------------

namespace AttacheCase.Cli;

public static class CommandLineStartup
{
    /// <summary>起動時に指定された実行条件。指定が無ければ null</summary>
    public static CliOptions? Options { get; set; }

    /// <summary>
    /// 保持している実行条件を取り出す。二度目以降は null を返すため、
    /// ウィンドウの再表示などで処理が二重に走ることはない。
    /// </summary>
    public static CliOptions? Take()
    {
        var options = Options;
        Options = null;
        return options;
    }
}
