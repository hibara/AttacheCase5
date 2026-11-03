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
// v5（v150）復号の出力の扱い。
// 復号した平文は、本体全体の GCM タグ検証を通るまで最終パスに出さず
// （同じフォルダーの .attmp に書く）、失敗・中止したときは作りかけを残さない。
// 書庫の形式は変えない（切り詰め・改ざんした書庫で復号側の挙動だけを見る）。
//----------------------------------------------------------------------

using System.Security.Cryptography;
using AttacheCase.Models;
using AttacheCase.Services;

namespace AttacheCase.Tests;

public class DecryptionOutputLifecycleTests : IDisposable
{
    private const string Password = "test-password";

    private readonly string _dir = Directory.CreateTempSubdirectory("atc-lifecycle").FullName;

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 一時フォルダーの片付けに失敗しても、テストの結果には関係ない
        }
    }

    /// <summary>開いたままのファイルが削除を阻むのは Windows だけ（他 OS は開いていても消せる）</summary>
    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Windows のみ（開いているファイルの削除が拒否される前提）";
        }
    }

    /// <summary>進捗を呼び出しスレッド上で同期的に受ける（Progress&lt;T&gt; は後回しになり、観測の時刻がずれる）</summary>
    private sealed class SyncProgress(Action<EncryptionProgress> onReport) : IProgress<EncryptionProgress>
    {
        public void Report(EncryptionProgress value) => onReport(value);
    }

    /// <summary>圧縮で縮まない内容（切り詰める位置をファイル境界に対して見積もりやすい）</summary>
    private string Source(string name, int size, FileAttributes attributes = FileAttributes.Normal)
    {
        var dir = Path.Combine(_dir, "src");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        var bytes = new byte[size];
        RandomNumberGenerator.Fill(bytes);
        File.WriteAllBytes(path, bytes);
        File.SetAttributes(path, attributes);
        return path;
    }

    private async Task<string> EncryptAsync(string archiveName, params string[] inputs)
    {
        var atc = Path.Combine(_dir, archiveName);
        var result = await new EncryptionService().EncryptAsync(inputs, atc, Password, new EncryptionOptions());
        Assert.True(result.Success, result.ErrorMessage);
        return atc;
    }

    private static Task<EncryptionResult> DecryptAsync(
        string atc, string outDir, IProgress<EncryptionProgress>? progress = null, CancellationToken ct = default)
        => new DecryptionService().DecryptAsync(
            atc, outDir, Password, new DecryptionOptions { OverwriteOption = OverwriteOption.Overwrite }, progress, ct);

    private string OutputFolder(string name)
    {
        var dir = Path.Combine(_dir, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>出力フォルダーにあるファイル（相対パス、並び順固定）</summary>
    private static string[] FilesIn(string dir) => Directory.Exists(dir)
        ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(dir, p)).Order().ToArray()
        : [];

    /// <summary>失敗メッセージ用。何が何バイト残ったかが分かるように</summary>
    private static string Describe(string dir) => string.Join(", ",
        Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(dir, p) + (Directory.Exists(p) ? "/" : $" ({new FileInfo(p).Length} bytes)")));

    private static void Truncate(string atc, int bytes)
    {
        var all = File.ReadAllBytes(atc);
        File.WriteAllBytes(atc, all[..^bytes]);
    }

    /// <summary>本体の GCM 認証タグ（末尾 16 バイト）を 1 ビット反転する</summary>
    private static void TamperTag(string atc)
    {
        var all = File.ReadAllBytes(atc);
        all[^1] ^= 0x01;
        File.WriteAllBytes(atc, all);
    }

    [Fact]
    public async Task TruncatedArchive_LeavesNoPlaintext_EvenForTheFileBeingWritten()
    {
        // 2 つ目のファイルの途中で切れた書庫。1 つ目は書き終わり、2 つ目は書き込み中に例外になる
        var a = Source("a.bin", 1024 * 1024);
        var b = Source("b.bin", 1024 * 1024);
        var atc = await EncryptAsync("truncated.atc", a, b);
        Truncate(atc, 256 * 1024);
        var outDir = OutputFolder("out");

        var result = await DecryptAsync(atc, outDir);

        Assert.False(result.Success);
        Assert.True(FilesIn(outDir).Length == 0, "残骸: " + Describe(outDir));
        Assert.Empty(result.LeftoverPaths);
    }

    [Fact]
    public async Task TamperedTag_LeavesNoPlaintext_EvenWhenTheEntryIsReadOnly()
    {
        // タグ失敗は最終ブロックの DoFinal で表面化する（＝最後のファイルの書き込み中）。
        // 読み取り専用属性を写した項目も、片付けで属性を戻して消せること
        var ro = Source("ro.bin", 64 * 1024, FileAttributes.ReadOnly);
        var rw = Source("rw.bin", 64 * 1024);
        var atc = await EncryptAsync("tampered.atc", ro, rw);
        TamperTag(atc);
        var outDir = OutputFolder("out");

        var result = await DecryptAsync(atc, outDir);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ATC_BROKEN_DATA, result.ErrorCode);
        Assert.True(FilesIn(outDir).Length == 0, "残骸: " + Describe(outDir));
        Assert.Empty(result.LeftoverPaths);
    }

    [Fact]
    public async Task TruncatedArchive_OverExistingFile_KeepsTheOriginal_AndLeavesNoTempFile()
    {
        var a = Source("a.bin", 1024 * 1024);
        var atc = await EncryptAsync("over.atc", a);
        var outDir = OutputFolder("out");
        var existing = Path.Combine(outDir, "a.bin");
        var original = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(existing, original);
        Truncate(atc, 256 * 1024);

        var result = await DecryptAsync(atc, outDir);

        Assert.False(result.Success);
        Assert.Equal(["a.bin"], FilesIn(outDir));
        Assert.Equal(original, File.ReadAllBytes(existing));
        Assert.Empty(result.LeftoverPaths);
    }

    [Fact]
    public async Task FinalNames_AppearOnlyAfterTheWholeBodyIsVerified()
    {
        var a = Source("a.bin", 256 * 1024, FileAttributes.ReadOnly);
        var b = Source("b.bin", 256 * 1024);
        var atc = await EncryptAsync("ok.atc", a, b);
        var outDir = OutputFolder("out");

        // 進捗の報告ごとに出力フォルダーの中身を控える。最後の報告（完了）より前は一時ファイルしか無いこと
        var snapshots = new List<string[]>();
        var result = await DecryptAsync(atc, outDir, new SyncProgress(_ => snapshots.Add(FilesIn(outDir))));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(snapshots.Count >= 2, "進捗の報告が少なすぎる");
        foreach (var files in snapshots[..^1])
            Assert.All(files, f => Assert.EndsWith(".attmp", f));
        Assert.Contains(snapshots[..^1], files => files.Length > 0);

        // 検証後は最終名だけになり、内容と属性（読み取り専用）は移動後も保たれる
        Assert.Equal(["a.bin", "b.bin"], FilesIn(outDir));
        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(Path.Combine(outDir, "a.bin")));
        Assert.Equal(File.ReadAllBytes(b), File.ReadAllBytes(Path.Combine(outDir, "b.bin")));
        Assert.True(File.GetAttributes(Path.Combine(outDir, "a.bin")).HasFlag(FileAttributes.ReadOnly));
        Assert.False(File.GetAttributes(Path.Combine(outDir, "b.bin")).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public async Task FileNameNearTheLengthLimit_StillDecrypts()
    {
        // 最終名がファイル名長の上限（255）に近い項目。一時ファイル名を最終名から作る
        // （最終名 + "." + GUID + ".attmp" = 39 文字増）と、最終名では作れるのに一時ファイルが作れず
        // 復号に失敗する（CodeRabbit PR#21）。一時ファイル名は最終名に依存しないこと
        var longName = new string('x', 240) + ".bin";
        var a = Source(longName, 16 * 1024);
        var atc = await EncryptAsync("long.atc", a);
        var outDir = OutputFolder("out");

        var result = await DecryptAsync(atc, outDir);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal([longName], FilesIn(outDir));
        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(Path.Combine(outDir, longName)));
    }

    [Fact]
    public async Task Cancellation_LeavesNoPlaintext()
    {
        var a = Source("a.bin", 1024 * 1024);
        var b = Source("b.bin", 1024 * 1024);
        var atc = await EncryptAsync("cancel.atc", a, b);
        var outDir = OutputFolder("out");

        // 最初のファイルの進捗が報告された時点（一時ファイルがある）で中止する
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p =>
        {
            if (p.CurrentFilePath.EndsWith("a.bin", StringComparison.Ordinal)) cts.Cancel();
        });

        var result = await DecryptAsync(atc, outDir, progress, cts.Token);

        Assert.True(result.WasCancelled);
        Assert.True(FilesIn(outDir).Length == 0, "残骸: " + Describe(outDir));
        Assert.Empty(result.LeftoverPaths);
    }

    [WindowsFact]
    public async Task LeftoverThatCouldNotBeRemoved_IsReportedInTheResult()
    {
        var a = Source("a.bin", 64 * 1024);
        var b = Source("b.bin", 64 * 1024);
        var atc = await EncryptAsync("held.atc", a, b);
        TamperTag(atc);
        var outDir = OutputFolder("out");

        // 1 つ目の一時ファイルを別のハンドルで掴んだまま失敗させる（ウイルス対策や
        // プレビューが掴んでいる状況の代わり）。片付けで消せず、結果に場所が載ること
        FileStream? held = null;
        var progress = new SyncProgress(_ =>
        {
            if (held != null) return;
            // 一時ファイルの名前は GUID だけ（最終名を含まない）。最初に現れるのは a.bin のもの
            var temp = Directory.EnumerateFiles(outDir, "*.attmp").FirstOrDefault();
            if (temp == null) return;
            try { held = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None); }
            catch (IOException) { /* 書き込み中ならまだ開けない。次の報告で試す */ }
        });

        try
        {
            var result = await DecryptAsync(atc, outDir, progress);

            Assert.False(result.Success);
            Assert.NotNull(held);
            var leftover = Assert.Single(result.LeftoverPaths);
            Assert.Equal(held!.Name, leftover);
            Assert.Contains(leftover, result.ErrorMessage);
            Assert.Equal([Path.GetFileName(leftover)], FilesIn(outDir));
        }
        finally
        {
            held?.Dispose();
        }
    }
}
