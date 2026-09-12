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
// 差し替え処理のテスト。
//
// ここだけが利用者のインストール済みアプリを実際に動かす。壊れ方が致命的なので、
// シェルスクリプトそのものを走らせて確かめる。
//
// 本番のスクリプトは /usr/bin/open を絶対パスで呼ぶ（PATH を差し替えて別のものを
// 実行させられないようにするため）。そのままだと、テスト用の中身の無い .app を
// 起動しようとして macOS のエラーになるので、テストの中でだけ open を
// /usr/bin/true に置き換えた写しを使う。置き換えるのは起動の一手だけで、
// 退避・配置・巻き戻しの流れはそのまま確かめている。
//----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AttacheCase.Services;
using Xunit;

namespace AttacheCase.Tests;

[Trait("Category", "Update")]
public class UpdateInstallerTests
{
    // =================================================================
    // 差し替えてよい組み合わせか
    // =================================================================

    private static (string Root, string Target, string New) NewLayout()
    {
        var root = Directory.CreateTempSubdirectory("atc-install").FullName;
        var target = Path.Combine(root, "Applications", "AttacheCase.app");
        var newApp = Path.Combine(root, "work", "AttacheCase.app");

        Directory.CreateDirectory(Path.Combine(target, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(target, "Contents", "MacOS", "AttacheCase"), "old");
        Directory.CreateDirectory(Path.Combine(newApp, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(newApp, "Contents", "MacOS", "AttacheCase"), "new");

        // スクリプトは Info.plist の CFBundleExecutable から実行ファイル名を読む
        foreach (var app in new[] { target, newApp })
        {
            var plist =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<plist version=\"1.0\"><dict>\n" +
                "  <key>CFBundleExecutable</key><string>AttacheCase</string>\n" +
                "</dict></plist>\n";
            File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), plist);
        }

        return (root, target, newApp);
    }

    [MacOsFact]
    public void 正しい組み合わせは受け入れる()
    {
        var (root, target, newApp) = NewLayout();
        try
        {
            UpdateInstaller.Validate(newApp, target);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [MacOsTheory]
    [InlineData("", "/Applications/AttacheCase.app")]
    [InlineData("/tmp/new.app", "")]
    public void パスが空なら受け付けない(string source, string target)
    {
        Assert.Throws<ArgumentException>(() => UpdateInstaller.Validate(source, target));
    }

    [MacOsFact]
    public void app以外は差し替えの対象にしない()
    {
        var (root, target, _) = NewLayout();
        try
        {
            Assert.Throws<ArgumentException>(
                () => UpdateInstaller.Validate(root, target));
            Assert.Throws<ArgumentException>(
                () => UpdateInstaller.Validate(target, root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [MacOsFact]
    public void 元と先が同じなら受け付けない()
    {
        var (root, target, _) = NewLayout();
        try
        {
            Assert.Throws<ArgumentException>(() => UpdateInstaller.Validate(target, target));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [MacOsFact]
    public void 新しいappが差し替え先の中にあれば受け付けない()
    {
        // 退避した時点で新しいほうも一緒に動いてしまう
        var (root, target, _) = NewLayout();
        try
        {
            var inside = Path.Combine(target, "Contents", "New.app");
            Directory.CreateDirectory(inside);

            Assert.Throws<ArgumentException>(() => UpdateInstaller.Validate(inside, target));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [MacOsFact]
    public void 実在しないパスは受け付けない()
    {
        var (root, target, newApp) = NewLayout();
        try
        {
            Assert.Throws<DirectoryNotFoundException>(
                () => UpdateInstaller.Validate(Path.Combine(root, "missing.app"), target));
            Assert.Throws<DirectoryNotFoundException>(
                () => UpdateInstaller.Validate(newApp, Path.Combine(root, "missing.app")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // =================================================================
    // 作業場所
    //
    // スクリプトは最後にここを rm -rf する。差し替え先と重なっていると、
    // 配置したばかりの新しいアプリを自分で消すことになる。
    // =================================================================

    private const string Target = "/Applications/AttacheCase.app";

    [MacOsFact]
    public void 重なっていない作業場所は受け入れる()
    {
        UpdateInstaller.ValidateWorkDirectory("/tmp/attachecase-update-abc", Target);
    }

    [MacOsTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void 作業場所が空なら受け付けない(string workDirectory)
    {
        Assert.Throws<ArgumentException>(
            () => UpdateInstaller.ValidateWorkDirectory(workDirectory, Target));
    }

    [MacOsFact]
    public void 作業場所が差し替え先と同じなら受け付けない()
    {
        Assert.Throws<ArgumentException>(
            () => UpdateInstaller.ValidateWorkDirectory(Target, Target));
    }

    [MacOsTheory]
    [InlineData("/Applications")]     // 親。渡すと /Applications ごと消える
    [InlineData("/")]                 // 根。最悪の場合
    public void 作業場所が差し替え先を含むなら受け付けない(string workDirectory)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => UpdateInstaller.ValidateWorkDirectory(workDirectory, Target));

        Assert.Contains("含んでいます", ex.Message);
    }

    [MacOsTheory]
    [InlineData("/Applications/AttacheCase.app/Contents")]
    [InlineData("/Applications/AttacheCase.app/Contents/MacOS/work")]
    public void 作業場所が差し替え先の中なら受け付けない(string workDirectory)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => UpdateInstaller.ValidateWorkDirectory(workDirectory, Target));

        Assert.Contains("中にあります", ex.Message);
    }

    [MacOsFact]
    public void 末尾のスラッシュや相対表記に惑わされない()
    {
        // 正規化しないと、同じ場所を別物として通してしまう
        Assert.Throws<ArgumentException>(
            () => UpdateInstaller.ValidateWorkDirectory("/Applications/AttacheCase.app/", Target));

        Assert.Throws<ArgumentException>(
            () => UpdateInstaller.ValidateWorkDirectory(
                "/Applications/AttacheCase.app/Contents/..", Target));
    }

    [MacOsFact]
    public void 名前が前方一致するだけの別ディレクトリは受け入れる()
    {
        // AttacheCase.app と AttacheCase.app.backup を取り違えない
        UpdateInstaller.ValidateWorkDirectory("/Applications/AttacheCase.app.backup", Target);
    }

    // =================================================================
    // 退避先の名前
    // =================================================================

    [MacOsFact]
    public void 退避先は同じ階層に作る()
    {
        // 別のボリュームへ mv すると実体の複製になり、一瞬で終わらず失敗もしやすい
        var backup = UpdateInstaller.BuildBackupPath("/Applications/AttacheCase.app");

        Assert.Equal("/Applications", Path.GetDirectoryName(backup));
        Assert.NotEqual("/Applications/AttacheCase.app", backup);
    }

    [MacOsFact]
    public void 退避先はFinderに出ない名前にする()
    {
        var backup = UpdateInstaller.BuildBackupPath("/Applications/AttacheCase.app");

        Assert.StartsWith(".", Path.GetFileName(backup));
        Assert.DoesNotContain(".app/", backup + "/");
    }

    // =================================================================
    // スクリプトを実際に走らせる
    // =================================================================

    /// <summary>
    /// 本番のスクリプトを、起動の一手（open）だけ差し替えて走らせる。
    /// </summary>
    private static async Task<(int ExitCode, string Log, int? LaunchedPid)> RunScriptAsync(
        string waitPid, string newApp, string target, string backup, string workDir,
        bool launchForReal = false)
    {
        var scriptDir = Directory.CreateTempSubdirectory("atc-script").FullName;
        var scriptPath = Path.Combine(scriptDir, "install.sh");

        // 起こしたプロセスの PID を控える場所。scriptDir は成功時にスクリプト自身が
        // 消すので、その外に置く
        var pidDir = Directory.CreateTempSubdirectory("atc-pid").FullName;
        var pidFile = Path.Combine(pidDir, "pid");

        // 起動の一手だけ差し替える。ほかの手はそのまま。
        //   launchForReal = false … 何もしない（起動できなかった場合を作る）
        //   launchForReal = true  … 配置された実行ファイルを実際に起こす
        var openReplacement = "/usr/bin/true";
        if (launchForReal)
        {
            openReplacement = Path.Combine(scriptDir, "open.sh");
            // 起こした PID を控える。後始末では、このテストが起こしたものだけを止める
            var stub =
                "#!/bin/bash\n" +
                "\"$1/Contents/MacOS/AttacheCase\" 60 &\n" +
                "echo $! > \"" + pidFile + "\"\n";
            await File.WriteAllTextAsync(openReplacement, stub);
            TestFile.MakeExecutable(openReplacement);
        }

        // 署名の確認も差し替える。テストの .app は本物の署名を持てないので、
        // 本物の codesign では差し替える直前の再検証を必ず落ちてしまう。
        //
        // 代役は各 .app の "team" ファイルを署名者と見なす。書かなければ
        // 両方 TESTTEAM で一致し、片方だけ書き換えれば「途中ですり替えられた」
        // 状況を作れる。"VERIFY_FAIL" を置けば署名検証そのものが落ちる
        var codesignReplacement = Path.Combine(scriptDir, "codesign.sh");
        await File.WriteAllTextAsync(codesignReplacement,
            "#!/bin/bash\n" +
            "mode=\"$1\"; shift\n" +
            "if [ \"$mode\" = \"--verify\" ]; then\n" +
            "  for path; do :; done\n" +
            "  [ -e \"$path/VERIFY_FAIL\" ] && exit 1\n" +
            "  exit 0\n" +
            "fi\n" +
            "if [ \"$mode\" = \"-dvv\" ]; then\n" +
            "  team=TESTTEAM\n" +
            "  [ -f \"$1/team\" ] && team=\"$(cat \"$1/team\")\"\n" +
            "  echo \"TeamIdentifier=$team\" >&2\n" +
            "  exit 0\n" +
            "fi\n" +
            "exit 0\n");
        TestFile.MakeExecutable(codesignReplacement);

        await File.WriteAllTextAsync(
            scriptPath, UpdateInstaller.InstallScript
                .Replace("/usr/bin/open", openReplacement)
                .Replace("/usr/bin/codesign", codesignReplacement));


        var runner = new ProcessRunner();
        var result = await runner.RunAsync(
            "/bin/bash",
            new[] { scriptPath, waitPid, newApp, target, backup, workDir },
            TimeSpan.FromSeconds(90));

        var logPath = Path.Combine(scriptDir, "install.log");
        var log = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : string.Empty;

        int? launchedPid = null;
        if (File.Exists(pidFile) &&
            int.TryParse((await File.ReadAllTextAsync(pidFile)).Trim(), out var pid))
        {
            launchedPid = pid;
        }

        try { Directory.Delete(scriptDir, true); } catch (Exception) { /* 残っても実害なし */ }
        try { Directory.Delete(pidDir, true); } catch (Exception) { /* 同上 */ }

        return (result.ExitCode, log, launchedPid);
    }

    /// <summary>
    /// このテストが起こしたプロセスだけを止める。
    ///
    /// 名前で探して止めると、開発中に動かしているアタッシェケースまで巻き添えになる。
    /// </summary>
    private static void KillIfRunning(int? pid)
    {
        if (pid is not int value) return;

        try
        {
            using var process = Process.GetProcessById(value);
            process.Kill();
        }
        catch (Exception)
        {
            // すでに終了している
        }
    }

    /// <summary>終了済みのプロセス ID を作る。</summary>
    private static async Task<string> DeadPidAsync()
    {
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/true"))!;
        await process.WaitForExitAsync();
        return process.Id.ToString();
    }

    [MacOsFact]
    public async Task 呼び出し元が生きているあいだは何も触らない()
    {
        // 処理中のファイルがあるかもしれないので、強制終了もしない
        var (root, target, newApp) = NewLayout();
        var backup = UpdateInstaller.BuildBackupPath(target);
        try
        {
            var (exitCode, log, _) = await RunScriptAsync(
                Environment.ProcessId.ToString(), newApp, target, backup, Path.Combine(root, "work"));

            Assert.NotEqual(0, exitCode);
            Assert.Contains("終了しないため中止", log);

            // 差し替え先はそのまま
            Assert.Equal("old",
                await File.ReadAllTextAsync(Path.Combine(target, "Contents", "MacOS", "AttacheCase")));
            Assert.False(Directory.Exists(backup));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }


    /// <summary>
    /// 起動確認に使う実行ファイルをその場で作る。
    ///
    /// Apple Silicon では署名の無いバイナリは起動できない。/bin/sleep の写しは
    /// プラットフォームバイナリの写しとして署名が無効になり、実行すると SIGKILL
    /// される（アドホック再署名も拒否される）。小さなものを作るのが確実。
    /// </summary>
    private static async Task<bool> TryBuildSleeperAsync(string destination)
    {
        var source = Path.Combine(Path.GetDirectoryName(destination)!, "sleeper.c");
        await File.WriteAllTextAsync(source,
            "#include <unistd.h>\nint main(void){ sleep(60); return 0; }\n");

        try
        {
            var result = await new ProcessRunner().RunAsync(
                "/usr/bin/clang",
                new[] { "-o", destination, source },
                TimeSpan.FromSeconds(60));

            File.Delete(source);
            return result.Succeeded && File.Exists(destination);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [MacOsFact]
    public async Task 起動確認は無関係なプロセスに騙されない()
    {
        // 対象パスをコマンドラインに含むだけの無関係なプロセス（ターミナルで
        // ls を打った、バックアップソフトが走っている、など）を「起動した」と
        // 誤認すると、実際には起動していないのに退避分を消し、巻き戻せなくなる。
        //
        // pgrep -f はコマンドライン全体を照合するのでこれに一致してしまう。
        // ps -o comm= の完全一致なら一致しない。
        var (root, target, newApp) = NewLayout();
        var backup = UpdateInstaller.BuildBackupPath(target);
        var targetExe = Path.Combine(target, "Contents", "MacOS", "AttacheCase");

        using var decoy = Process.Start(new ProcessStartInfo("/bin/bash")
        {
            ArgumentList = { "-c", "sleep 60; :", targetExe },
        })!;

        try
        {
            var (exitCode, log, _) = await RunScriptAsync(
                await DeadPidAsync(), newApp, target, backup, Path.Combine(root, "work"));

            // 騙されず、起動していないと判断して巻き戻すこと
            Assert.NotEqual(0, exitCode);
            Assert.Contains("起動を確認できませんでした", log);
            Assert.Equal("old", await File.ReadAllTextAsync(targetExe));
            Assert.False(Directory.Exists(backup));
        }
        finally
        {
            try { decoy.Kill(entireProcessTree: true); } catch (Exception) { /* すでに終了 */ }
            Directory.Delete(root, true);
        }
    }

    [MacOsFact]
    public async Task 差し替える直前に署名者が変わっていたら何もしない()
    {
        // アプリ側で検証してからここへ来るまでには間がある（「再起動します」の
        // 画面を読んでいる時間と、終了を待つ時間）。その隙に作業場所の .app を
        // 差し替えられても、検証を通したつもりの別物を配置しないこと
        var (root, target, newApp) = NewLayout();
        var backup = UpdateInstaller.BuildBackupPath(target);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(newApp, "team"), "ATTACKER");

            var (exitCode, log, _) = await RunScriptAsync(
                await DeadPidAsync(), newApp, target, backup, Path.Combine(root, "work"));

            Assert.NotEqual(0, exitCode);
            Assert.Contains("署名者が違います", log);

            // 退避もしていない。何も起きなかったのと同じ状態で終わる
            Assert.Equal("old",
                await File.ReadAllTextAsync(Path.Combine(target, "Contents", "MacOS", "AttacheCase")));
            Assert.False(Directory.Exists(backup));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [MacOsFact]
    public async Task 差し替える直前の署名検証に失敗したら何もしない()
    {
        var (root, target, newApp) = NewLayout();
        var backup = UpdateInstaller.BuildBackupPath(target);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(newApp, "VERIFY_FAIL"), "");

            var (exitCode, log, _) = await RunScriptAsync(
                await DeadPidAsync(), newApp, target, backup, Path.Combine(root, "work"));

            Assert.NotEqual(0, exitCode);
            Assert.Contains("署名検証に失敗", log);
            Assert.Equal("old",
                await File.ReadAllTextAsync(Path.Combine(target, "Contents", "MacOS", "AttacheCase")));
            Assert.False(Directory.Exists(backup));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [MacOsFact]
    public async Task 差し替えが通れば新しい中身になり退避分は消える()
    {
        var (root, target, newApp) = NewLayout();
        var backup = UpdateInstaller.BuildBackupPath(target);
        var workDir = Path.Combine(root, "work");

        // 起動確認は ps -o comm= の完全一致で行うので、その実行ファイルが実際に
        // 動いていなければ通らない。中身を本物の実行ファイル（sleep の写し）にして、
        // テスト用の open がそれを起こす
        var newExe = Path.Combine(newApp, "Contents", "MacOS", "AttacheCase");
        if (!await TryBuildSleeperAsync(newExe)) return;   // clang が無ければ確認しない

        int? launchedPid = null;

        // 実行ファイルは sleep の写しになったので、差し替わったかどうかは
        // 別に置いた目印で確かめる
        var marker = Path.Combine("Contents", "Resources", "which.txt");
        Directory.CreateDirectory(Path.Combine(newApp, "Contents", "Resources"));
        Directory.CreateDirectory(Path.Combine(target, "Contents", "Resources"));
        await File.WriteAllTextAsync(Path.Combine(newApp, marker), "new");
        await File.WriteAllTextAsync(Path.Combine(target, marker), "old");

        try
        {
            var (exitCode, log, pid) = await RunScriptAsync(
                await DeadPidAsync(), newApp, target, backup, workDir, launchForReal: true);
            launchedPid = pid;

            Assert.Equal(0, exitCode);
            Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(target, marker)));

            // 起動を見届けてから退避分を消す
            Assert.False(Directory.Exists(backup));

            // 作業場所も片付ける
            Assert.False(Directory.Exists(workDir));

            // 成功したときはスクリプトが自分の置き場ごと消えるので、ログは残らない。
            // 失敗時はその前に抜けるため、調べたいときには必ず残っている
            Assert.Equal(string.Empty, log);
        }
        finally
        {
            // このテストが起こしたものだけを止める。名前で探すと、開発中に
            // 動かしているアタッシェケースまで巻き添えになる
            KillIfRunning(launchedPid);
            Directory.Delete(root, true);
        }
    }

    [MacOsFact]
    public async Task 起動を確認できなければ元に戻す()
    {
        // 配置はできたが新しいアプリが立ち上がらない場合。
        // 利用者を「動かないアプリだけが残った」状態にしない
        var (root, target, newApp) = NewLayout();
        var backup = UpdateInstaller.BuildBackupPath(target);
        try
        {
            var (exitCode, log, _) = await RunScriptAsync(
                await DeadPidAsync(), newApp, target, backup, Path.Combine(root, "work"));

            Assert.NotEqual(0, exitCode);
            Assert.Contains("起動を確認できませんでした", log);

            // 旧版が戻っていること
            Assert.Equal("old",
                await File.ReadAllTextAsync(Path.Combine(target, "Contents", "MacOS", "AttacheCase")));
            Assert.False(Directory.Exists(backup));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [MacOsFact]
    public async Task 配置に失敗したら元に戻す()
    {
        // 新しい .app を消してから走らせ、ditto を失敗させる
        var (root, target, newApp) = NewLayout();
        var backup = UpdateInstaller.BuildBackupPath(target);
        try
        {
            Directory.Delete(newApp, true);

            var (exitCode, log, _) = await RunScriptAsync(
                await DeadPidAsync(), newApp, target, backup, Path.Combine(root, "work"));

            Assert.NotEqual(0, exitCode);
            Assert.Contains("元に戻します", log);

            // 旧版が戻っていること。ここが崩れると利用者はアプリを失う
            Assert.True(Directory.Exists(target));
            Assert.Equal("old",
                await File.ReadAllTextAsync(Path.Combine(target, "Contents", "MacOS", "AttacheCase")));
            Assert.False(Directory.Exists(backup));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [MacOsFact]
    public async Task 差し替え先が無ければ何もせず終わる()
    {
        var (root, target, newApp) = NewLayout();
        var backup = UpdateInstaller.BuildBackupPath(target);
        try
        {
            Directory.Delete(target, true);

            var (exitCode, log, _) = await RunScriptAsync(
                await DeadPidAsync(), newApp, target, backup, Path.Combine(root, "work"));

            Assert.NotEqual(0, exitCode);
            Assert.Contains("退避を完了できませんでした", log);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // =================================================================
    // やり残しの片付け
    // =================================================================

    [MacOsFact]
    public void 古い作業ディレクトリだけを片付ける()
    {
        var temp = Path.GetTempPath();
        var old = Path.Combine(temp, $"attachecase-update-{Guid.NewGuid():N}");
        var recent = Path.Combine(temp, $"attachecase-install-{Guid.NewGuid():N}");
        var unrelated = Path.Combine(temp, $"something-else-{Guid.NewGuid():N}");

        Directory.CreateDirectory(old);
        Directory.CreateDirectory(recent);
        Directory.CreateDirectory(unrelated);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
        Directory.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-3));

        try
        {
            UpdateInstaller.CleanUpStaleWorkDirectories(TimeSpan.FromDays(1));

            Assert.False(Directory.Exists(old));        // 古いので消す
            Assert.True(Directory.Exists(recent));      // 新しいので残す
            Assert.True(Directory.Exists(unrelated));   // 無関係なので触らない
        }
        finally
        {
            foreach (var d in new[] { old, recent, unrelated })
            {
                if (Directory.Exists(d)) Directory.Delete(d, true);
            }
        }
    }

    [Fact]
    public void InstallScript_試験用のマニフェストURLを新しいアプリへ引き継がない()
    {
        // 環境変数は 子プロセス（このスクリプト）→ open で起こす新しいアプリ、と
        // 引き継がれる。外さないと、更新後も試験用 URL を見て 999.0.0 の更新が
        // 何度でも出る（実機で確認）。名前は UpdateService の定数と一致させる
        Assert.Contains("unset " + UpdateService.ManifestUrlEnvironmentVariable, UpdateInstaller.InstallScript);
    }
}
