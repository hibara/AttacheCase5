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
// 自己実行形式（EXE(ATC)）の判定は、8KB ずつ読みながらトークンを探す。
// 読み取り単位の継ぎ目や末尾で取りこぼさないことを見張る。
//----------------------------------------------------------------------

using System.Text;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class FileTypeDetectorScanTests
{
    private const string AtcSignature = "_AttacheCaseData";

    /// <summary>
    /// トークンが 8KB の読み取り境界をまたぐ位置にあっても見つけること。
    /// </summary>
    [Theory]
    [InlineData(8192 - 8)]   // 継ぎ目をまたぐ
    [InlineData(8192 - 1)]   // 1 バイトだけ手前に残る
    [InlineData(8192)]       // ちょうど継ぎ目から始まる
    [InlineData(8192 + 1)]
    public void ExeWithTokenAcrossBufferBoundary_IsDetected(int tokenOffset)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ac5-exe-{Guid.NewGuid():N}.exe");
        try
        {
            WriteFakeExe(path, tokenOffset, totalSize: tokenOffset + 4096);

            Assert.Equal(2, new FileTypeDetectorService().CheckFileType(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// トークンがファイルの最後尾で終わる場合も見つけること。
    /// （走査ループが最後の有効オフセットを見ていないと落ちる）
    /// </summary>
    [Fact]
    public void ExeWithTokenAtVeryEnd_IsDetected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ac5-exe-{Guid.NewGuid():N}.exe");
        try
        {
            const int offset = 3000;
            WriteFakeExe(path, offset, totalSize: offset + AtcSignature.Length);

            Assert.Equal(2, new FileTypeDetectorService().CheckFileType(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 先頭が "&lt;?xml" なだけの巨大ファイルを、鍵ファイルとして丸ごと読み込まないこと。
    /// </summary>
    [Fact]
    public void HugeXmlLikeFile_IsNotTreatedAsKeyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ac5-huge-{Guid.NewGuid():N}.xml");
        try
        {
            using (var fs = new FileStream(path, FileMode.Create))
            {
                var head = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><root>");
                fs.Write(head, 0, head.Length);
                fs.SetLength(2L * 1024 * 1024); // スパースな 2MB（上限 64KB 超）
            }

            // 鍵（5 / 6）ではなく通常ファイル扱いになること
            Assert.Equal(0, new FileTypeDetectorService().CheckFileType(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteFakeExe(string path, int tokenOffset, int totalSize)
    {
        var bytes = new byte[Math.Max(totalSize, tokenOffset + AtcSignature.Length)];
        bytes[0] = 0x4D; // 'M'
        bytes[1] = 0x5A; // 'Z'
        Encoding.ASCII.GetBytes(AtcSignature).CopyTo(bytes, tokenOffset);
        File.WriteAllBytes(path, bytes);
    }
}
