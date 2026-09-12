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
// 自己実行形式（.exe）用スタブ（Exeout.exe / net48）のバイナリ埋め込み。
//
// これは「Exeout48.cs」の雛形（空版）。実体の Exeout48.cs は生成物で、
// 実質バイナリのため git 追跡しない（.gitignore）。ビルドバッチがこの雛形を
// Exeout48.cs へコピーし、外部ツール tools\ExeToHex が下の 2 つのマーカー領域
//  （ATC executable file size / ATC executable file bytes data）に
// スタブのサイズと byte[] リテラルを差し込む。手で編集しないこと。
// （注意: このコメントにマーカー字句そのものを書かないこと。ExeToHex は
//   その字句を単純な文字列一致で探すため、コメントに書くと誤検出で壊れる）
//
// 本体（EncryptionService.WriteExecutableStub）は rawData を出力の先頭に書き、
// その後ろに暗号化データを続けて自己実行形式ファイルを作る。
//----------------------------------------------------------------------
namespace AttacheCase
{
  internal partial class FileEncrypt5
  {
    public static int ExeOutFileSize =
#region ATC executable file size
      0;
#endregion

    public static byte[] rawData = new byte[]
    {
#region ATC executable file bytes data
#endregion
    };
  }
}
