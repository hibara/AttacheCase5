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
// 自己実行形式（SFX）と埋め込みスタブのテスト。
//
// スタブは本体に byte[] で埋め込まれる（Exeout48.cs の FileEncrypt5.rawData。
// ビルド時に tools\ExeToHex が Exeout.exe を差し込む）。素の dotnet build/test は
// ExeToHex を通さないため rawData は空で、その場合これらは Skip する
// （ExeToHex を通した本ビルド／ビルドバッチのときだけ実行）。検証内容:
//   (1) スタブが日本語リソースをメインアセンブリに内蔵していること
//       （衛星 dll 無しの単一 exe で日本語 UI が出る前提条件）
//   (2) SFX のラウンドトリップ（作成 → MZ 先頭 → 復号一致）
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AttacheCase.Models;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

public class ExeoutStubTests
{
    /// <summary>Windows かつスタブが埋め込まれている（ExeToHex 済み）ときだけ走らせる。</summary>
    private sealed class SfxFactAttribute : FactAttribute
    {
        public SfxFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip = "Windows のみ（SFX は Windows 専用機能）";
            }
            else if (FileEncrypt5.rawData is not { Length: > 0 })
            {
                Skip = "スタブ未埋め込み（ExeToHex を通していないビルド）";
            }
        }
    }

    [SfxFact]
    public void スタブは日本語リソースをメインアセンブリに内蔵している()
    {
        // 衛星 dll（ja-JP\Exeout.resources.dll）は SFX に同梱できないため、
        // 文化名付きリソースがメインアセンブリに埋め込まれていることが必須。
        // 埋め込みスタブ（rawData）を一時 exe に書き出してメタデータを読む。
        var tmp = Path.Combine(Path.GetTempPath(), "atc-stub-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(tmp, FileEncrypt5.rawData);
        try
        {
            var asm = Assembly.LoadFile(tmp);
            var names = asm.GetManifestResourceNames();

            Assert.Contains("Exeout.Form1.ja-JP.resources", names);
            Assert.Contains("Exeout.Form2.ja-JP.resources", names);
            Assert.Contains("Exeout.Properties.Resources.ja-JP.resources", names);

            // 空のリソースを埋めただけ、を防ぐ（翻訳が入っていれば KB 単位になる）。
            using var stream = asm.GetManifestResourceStream("Exeout.Form1.ja-JP.resources")!;
            Assert.True(stream.Length > 500, $"ja-JP リソースが小さすぎる: {stream.Length} bytes");
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* 後始末の失敗は無視 */ }
        }
    }

    [SfxFact]
    public async Task SFXを作成すると先頭がMZで復号すると内容が一致する()
    {
        var work = Directory.CreateTempSubdirectory("atc-sfx-test").FullName;
        try
        {
            var srcPath = Path.Combine(work, "src.txt");
            var srcBytes = new byte[8192];
            Random.Shared.NextBytes(srcBytes);
            await File.WriteAllBytesAsync(srcPath, srcBytes);

            var sfxPath = Path.Combine(work, "out.exe");
            var encryptResult = await new EncryptionService().EncryptAsync(
                new[] { srcPath }, sfxPath, "sfx-pass",
                new EncryptionOptions { CreateExecutable = true });

            Assert.True(encryptResult.Success, encryptResult.ErrorMessage);

            // 先頭 2 バイトは PE の "MZ"、サイズは埋め込みスタブ以上
            await using (var fs = File.OpenRead(sfxPath))
            {
                Assert.True(fs.Length > FileEncrypt5.rawData.Length);
                Assert.Equal('M', (char)fs.ReadByte());
                Assert.Equal('Z', (char)fs.ReadByte());
            }

            var outDir = Path.Combine(work, "dec");
            var decryptResult = await new DecryptionService().DecryptAsync(
                sfxPath, outDir, "sfx-pass",
                new DecryptionOptions { NoParentFolder = true, OverwriteOption = OverwriteOption.OverwriteAll });

            Assert.True(decryptResult.Success, decryptResult.ErrorMessage);
            Assert.Equal(srcBytes, await File.ReadAllBytesAsync(Path.Combine(outDir, "src.txt")));
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* 後始末の失敗は無視 */ }
        }
    }
}
