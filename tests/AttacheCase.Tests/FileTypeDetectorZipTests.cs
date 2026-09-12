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
// ZIP を容れ物にしているだけの書類（.docx など）を、書庫と取り違えないこと。
//
// 取り違えると、暗号化するつもりで置いた文書が解凍画面へ回り、
// docProps や word といった中身がばらまかれる。実際にそうなった。
//----------------------------------------------------------------------

using System.IO.Compression;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class FileTypeDetectorZipTests
{
    /// <summary>指定した名前の項目を、その順で持つ ZIP を作る。</summary>
    private static string NewZip(string extension, params string[] entryNames)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ac5-zip-{Guid.NewGuid():N}{extension}");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in entryNames)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write("x");
        }

        return path;
    }

    private static int TypeOf(string path)
    {
        try
        {
            return new FileTypeDetectorService().CheckFileType(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".pptx")]
    [InlineData(".odt")]
    [InlineData(".epub")]
    [InlineData(".jar")]
    [InlineData(".numbers")]
    public void ZIPを容れ物にした書類は通常ファイルとして扱う(string extension)
    {
        // 中身が ZIP でも、利用者から見ればただの文書。解凍画面へ送らない
        Assert.Equal(0, TypeOf(NewZip(extension, "readme.txt")));
    }

    [Fact]
    public void 拡張子が違ってもOfficeの書類なら通常ファイルとして扱う()
    {
        // 拡張子を頼りにできない場合の備え。Office Open XML は
        // [Content_Types].xml が先頭に来る
        Assert.Equal(0, TypeOf(NewZip(".dat", "[Content_Types].xml", "word/document.xml")));
    }

    [Fact]
    public void 拡張子が違ってもOpenDocumentなら通常ファイルとして扱う()
    {
        // OpenDocument と EPUB は mimetype が先頭に来る
        Assert.Equal(0, TypeOf(NewZip(".dat", "mimetype", "content.xml")));
    }

    [Fact]
    public void ふつうのZIPは書庫として扱う()
    {
        // ここまで緩めてしまうと、本当の ZIP を解凍できなくなる
        Assert.Equal(3, TypeOf(NewZip(".zip", "readme.txt", "photo.jpg")));
    }

    [Fact]
    public void 拡張子の無いZIPも書庫として扱う()
    {
        Assert.Equal(3, TypeOf(NewZip("", "readme.txt")));
    }

    [Fact]
    public void 名前が似ているだけの項目では書類と見なさない()
    {
        // 先頭の項目名は完全一致で見る
        Assert.Equal(3, TypeOf(NewZip(".zip", "mimetype.txt")));
        Assert.Equal(3, TypeOf(NewZip(".zip", "[Content_Types].xml.bak")));
    }
}
