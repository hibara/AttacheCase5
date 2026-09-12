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
// 取り返しのつかない壊し方をしないことを見張るテスト。
// いずれも実際に踏んだ不具合の再発防止。
//----------------------------------------------------------------------

using System.Text;
using AttacheCase.Cli;
using AttacheCase.Crypto;
using AttacheCase.Services;
using ICSharpCode.SharpZipLib.Zip;
using Xunit;

namespace AttacheCase.Tests;

public class DestructiveSafetyTests
{
    /// <summary>
    /// 完全削除でシンボリックリンクを辿ると、フォルダーの外にある実体を
    /// 上書きしてしまう。リンクだけを消して実体には触れないこと。
    /// </summary>
    [SymlinkFact]
    public void Wipe_WithSymlinkInside_DoesNotTouchLinkTarget()
    {
        var root = NewTempDir("wipe-symlink");
        try
        {
            var target = Path.Combine(root, "dir");
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(outside);

            const string payload = "フォルダーの外にある、消えてはいけないファイル";
            var important = Path.Combine(outside, "important.txt");
            File.WriteAllText(important, payload);

            File.CreateSymbolicLink(Path.Combine(target, "link.txt"), important);

            FileEraser.Erase(target, FileEraser.Mode.Wipe);

            Assert.True(File.Exists(important), "リンク先が消された");
            Assert.Equal(payload, File.ReadAllText(important));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 削除対象そのものがディレクトリへのシンボリックリンクのとき。
    /// Directory.Exists がリンク先を見て true を返すため、素通しするとリンク先の
    /// 中身を消してしまう。リンクだけを消すこと。
    /// </summary>
    [SymlinkFact]
    public void Wipe_WhenTargetItselfIsDirectoryLink_DoesNotTouchLinkTarget()
    {
        var root = NewTempDir("wipe-rootlink");
        try
        {
            var real = Path.Combine(root, "real");
            Directory.CreateDirectory(real);

            const string payload = "リンク先にある、消えてはいけないファイル";
            var important = Path.Combine(real, "important.txt");
            File.WriteAllText(important, payload);

            var link = Path.Combine(root, "linkdir");
            Directory.CreateSymbolicLink(link, real);

            FileEraser.Erase(link, FileEraser.Mode.Wipe);

            Assert.True(File.Exists(important), "リンク先の中身が消された");
            Assert.Equal(payload, File.ReadAllText(important));
            Assert.False(Directory.Exists(link) || File.Exists(link), "リンク自体が残っている");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 通常削除でも同じ。Directory.Delete(recursive) はリンク先ごと消してしまう。
    /// </summary>
    [SymlinkFact]
    public void Delete_WhenTargetItselfIsDirectoryLink_DoesNotTouchLinkTarget()
    {
        var root = NewTempDir("delete-rootlink");
        try
        {
            var real = Path.Combine(root, "real");
            Directory.CreateDirectory(real);
            var important = Path.Combine(real, "important.txt");
            File.WriteAllText(important, "消えてはいけない");

            var link = Path.Combine(root, "linkdir");
            Directory.CreateSymbolicLink(link, real);

            FileEraser.Erase(link, FileEraser.Mode.Delete);

            Assert.True(File.Exists(important), "リンク先の中身が消された");
            Assert.False(Directory.Exists(link) || File.Exists(link), "リンク自体が残っている");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// フォルダーの中に外を指すディレクトリリンクがある場合、そこへ降りていかないこと。
    /// </summary>
    [SymlinkFact]
    public void Wipe_WithDirectoryLinkInside_DoesNotRecurseIntoIt()
    {
        var root = NewTempDir("wipe-sublink");
        try
        {
            var target = Path.Combine(root, "dir");
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(outside);

            const string payload = "フォルダーの外にある、消えてはいけないファイル";
            var important = Path.Combine(outside, "important.txt");
            File.WriteAllText(important, payload);

            Directory.CreateSymbolicLink(Path.Combine(target, "sub"), outside);

            FileEraser.Erase(target, FileEraser.Mode.Wipe);

            Assert.True(File.Exists(important), "リンクを辿って外のファイルを壊した");
            Assert.Equal(payload, File.ReadAllText(important));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// ZIP の展開先チェックは、末尾に区切り文字を付けて比較しないと
    /// 「出力先と接頭辞が同じ隣のフォルダー」へ抜けられる。
    /// </summary>
    [Fact]
    public async Task ZipExtract_EntryEscapingToSiblingDirectory_IsRejected()
    {
        var root = NewTempDir("zip-traversal");
        try
        {
            // 出力先 <root>/out に対して、<root>/outX へ抜ける名前を仕込む
            var outDir = Path.Combine(root, "out");
            var siblingDir = Path.Combine(root, "outX");
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(siblingDir);

            const string password = "pass";
            var zipPath = Path.Combine(root, "evil.zip");
            await using (var fs = new FileStream(zipPath, FileMode.Create))
            using (var zip = new ZipOutputStream(fs))
            {
                zip.Password = password;
                zip.PutNextEntry(new ZipEntry("../outX/evil.txt"));
                var data = Encoding.UTF8.GetBytes("侵入したファイル");
                zip.Write(data, 0, data.Length);
                zip.CloseEntry();
            }

#pragma warning disable CS0618 // 実装側に [Obsolete] が付いているが、これが現行の入口
            await new ZipPasswordDecryptionService()
                .DecryptAsync(zipPath, outDir, password, new AttacheCase.Models.DecryptionOptions());
#pragma warning restore CS0618

            Assert.False(File.Exists(Path.Combine(siblingDir, "evil.txt")),
                "出力先の外にファイルが書き込まれた");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// CRC32 が取れなかったファイルを黙って「CRC32 なし」として索引に書くと、
    /// 復号側の読み取りが 4 バイトずれて以降のエントリがすべて壊れる。
    /// 読めないファイルは索引に載せないこと。
    /// </summary>
    [Fact]
    public void Crc32_OnUnreadableFile_Throws_InsteadOfReturningNull()
    {
        var root = NewTempDir("crc32-io");
        try
        {
            var missing = Path.Combine(root, "not-exist.bin");
            Assert.Throws<FileNotFoundException>(
                () => Crc32Utility.ComputeFileCrc32(missing));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ac5-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
