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
// 検証済みの .app への差し替え。
//
// 自分自身を置き換えるので、アプリの中では完結できない。切り離したシェル
// スクリプトを起こし、こちらが終了したのを見届けてから作業させる。
//
// 順序は「直前にもう一度検証 → 旧を退避 → 新を配置 → 起動を確認 → 退避分を削除」。
// 先に消さないので、途中で電源が落ちても新旧どちらかは必ず残る。
// 配置や起動に失敗したら退避分を戻す。
//
// 差し替える直前にもう一度署名を見るのは、アプリ側の検証からここまでに間が
// あるため。その隙に差し替えられると、検証を通したつもりの別物を配置してしまう。
//
// スクリプトにパスを埋め込まず、すべて引数で渡す。埋め込むと、パスに空白や
// 引用符が含まれる場合に壊れる。
//----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AttacheCase.Services;

public class UpdateInstaller
{
    /// <summary>
    /// 差し替えを行うスクリプト。引数は次の 5 つ。
    ///   $1 待つプロセス ID  $2 新しい .app  $3 差し替え先  $4 退避先  $5 消してよい作業場所
    /// </summary>
    // 元の文字列にはソースファイルの改行がそのまま入る（Windows で CRLF 保存された
    // 作業ツリーを Mac でビルドすると CRLF になる）。bash は CR を受け付けず、
    // 冒頭の exec（ログの振り向け）で止まるので、使う前に必ず LF へ揃える
    internal static readonly string InstallScript = InstallScriptSource.ReplaceLineEndings("\n");

    private const string InstallScriptSource = """
        #!/bin/bash
        # アタッシェケースの自動更新。アプリ本体から起こされる。
        # 直接実行しないこと。
        set -u

        WAIT_PID="$1"
        NEW_APP="$2"
        TARGET_APP="$3"
        BACKUP_APP="$4"
        WORK_DIR="$5"

        SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
        exec >>"$SCRIPT_DIR/install.log" 2>&1
        echo "=== $(date) 差し替えを開始 ==="

        # 試験用の差し替え（ATTACHECASE_UPDATE_MANIFEST_URL）は、元のアプリの
        # 環境からこのスクリプトへ、さらに open で起こす新しいアプリへと
        # 引き継がれる。新しいアプリまで試験用 URL を見続けると、版 999.0.0 の
        # 更新が何度でも出る（実機で確認）。差し替えは 1 回の起動だけに
        # 効かせたいので、ここで外す
        unset ATTACHECASE_UPDATE_MANIFEST_URL

        # mv は、宛先が直前に（別プロセスに）ディレクトリとして作り直されて
        # いると、移動元をその中へ（入れ子で）移して成功してしまう。
        # 事前チェックの追加では窓が残るだけで、相手はこちらのロックにも
        # 従わないため、排他ではなく「移動後の配置の検査」で守る。
        # 入れ子を検出したら元の場所へ引き出し（この引き出しも結果を検査する）、
        # 1 を返す。呼び出し元は移動未完了として扱うこと。実際の所在は
        # どの経路でもログに残る
        move_strict() {
            local SRC="$1"
            local DST="$2"
            if ! /bin/mv "$SRC" "$DST"; then
                return 1
            fi
            local NESTED="$DST/$(/usr/bin/basename "$SRC")"
            if [ -e "$NESTED" ]; then
                echo "宛先が作り直されていたため入れ子を検出: $NESTED"
                if /bin/mv "$NESTED" "$SRC" && [ -e "$SRC" ] && [ ! -e "$NESTED" ]; then
                    echo "入れ子を解消し、元の場所へ戻しました: $SRC"
                else
                    echo "入れ子を解消できませんでした。所在: $NESTED"
                fi
                return 1
            fi
            if [ ! -e "$DST" ] || [ -e "$SRC" ]; then
                echo "移動後の配置が想定と一致しません: $SRC -> $DST"
                return 1
            fi
            return 0
        }

        rollback() {
            echo "配置に失敗したため元に戻します"
            # 復元先を空ける。消し切れていないのに mv すると、退避分が
            # $TARGET_APP の中へ（サブディレクトリとして）移って行方不明になる
            /bin/rm -rf "$TARGET_APP"
            if [ -e "$TARGET_APP" ]; then
                echo "復元先を空けられませんでした。退避分を残します: $BACKUP_APP"
                exit 1
            fi
            # 復元を完了できなかったら、退避分を消さずに残して終わる。
            # 存在しない $TARGET_APP を open しても仕方がないし、退避分さえ
            # 残っていれば利用者の手で戻せる（所在は move_strict がログに出す）
            if ! move_strict "$BACKUP_APP" "$TARGET_APP"; then
                echo "復元を完了できませんでした。退避分を残します: $BACKUP_APP"
                exit 1
            fi
            /usr/bin/open "$TARGET_APP"
        }

        # 1) 呼び出し元が終わるのを待つ。最大 60 秒
        for _ in $(seq 1 120); do
            /bin/kill -0 "$WAIT_PID" 2>/dev/null || break
            /bin/sleep 0.5
        done

        # まだ動いているなら何もしない。強制終了はしない
        # （処理中のファイルがあるかもしれない）
        if /bin/kill -0 "$WAIT_PID" 2>/dev/null; then
            echo "呼び出し元が終了しないため中止しました"
            exit 1
        fi

        # 2) 差し替える直前に、もう一度署名を確かめる
        #
        #    アプリ側で検証してから、ここへ来るまでには間がある。利用者が
        #    「再起動します」の画面を読んでいる時間と、終了を待つ時間で、
        #    どちらも何分にもなりうる。その間に $NEW_APP を差し替えられると、
        #    検証を通したつもりの別物を配置してしまう。
        #
        #    作業場所は $TMPDIR の下で本人しか入れないが、同じ利用者として
        #    動く別のプロセスは書き込める。
        #
        #    ここで見るのは署名と署名者の 2 つ。公証まで見なくても、
        #    こちらの Developer ID で署名し直すことは攻撃者にはできない。
        #    まだ何も動かしていないので、通らなければ何もせずに終わる
        EXPECTED_TEAM="$(/usr/bin/codesign -dvv "$TARGET_APP" 2>&1 \
            | /usr/bin/sed -n 's/^TeamIdentifier=//p')"
        NEW_TEAM="$(/usr/bin/codesign -dvv "$NEW_APP" 2>&1 \
            | /usr/bin/sed -n 's/^TeamIdentifier=//p')"

        if ! /usr/bin/codesign --verify --deep --strict "$NEW_APP"; then
            echo "差し替える直前の署名検証に失敗しました。中止します"
            exit 1
        fi

        if [ -z "$EXPECTED_TEAM" ] || [ "$EXPECTED_TEAM" = "not set" ]; then
            echo "動作中のアプリの署名者を読み取れません。中止します"
            exit 1
        fi

        if [ "$NEW_TEAM" != "$EXPECTED_TEAM" ]; then
            # 変数は {} で囲む。直後が全角文字だと、bash がそこまで変数名として
            # 読んでしまい "unbound variable" で落ちる
            echo "署名者が違います（期待: ${EXPECTED_TEAM} / 実際: ${NEW_TEAM}）。中止します"
            exit 1
        fi

        # 3) 旧版を退避する。まだ消さない
        #    退避先が作り直されていた場合は move_strict が旧版を元へ戻し、
        #    まだ何も壊していない状態で中止できる
        /bin/rm -rf "$BACKUP_APP"
        if ! move_strict "$TARGET_APP" "$BACKUP_APP"; then
            echo "退避を完了できませんでした。中止します"
            exit 1
        fi

        # 4) 新版を配置する。ditto は拡張属性を保つ
        if ! /usr/bin/ditto "$NEW_APP" "$TARGET_APP"; then
            rollback
            exit 1
        fi

        # 5) 起動する
        if ! /usr/bin/open "$TARGET_APP"; then
            rollback
            exit 1
        fi

        # 6) 立ち上がったのを見届けてから退避分を消す
        #
        #    pgrep -f はコマンドライン全体を正規表現で照合するため、対象パスを
        #    引数に含むだけの無関係なプロセス（ターミナルの ls など）にも一致する。
        #    それを「起動した」と誤認すると、実際には起動していないのに退避分を
        #    消してしまい、巻き戻せなくなる。
        #
        #    ps -o comm= は実行ファイルのパスそのものを返す。固定文字列の行単位
        #    一致（grep -Fxq）で見れば、その実行ファイルが動いている場合しか通らない。
        EXE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' \
            "$TARGET_APP/Contents/Info.plist" 2>/dev/null)"
        if [ -z "$EXE_NAME" ]; then
            # Info.plist から読めない場合はバンドル名から推す
            EXE_NAME="$(/usr/bin/basename "$TARGET_APP" .app)"
        fi
        TARGET_EXE="$TARGET_APP/Contents/MacOS/$EXE_NAME"
        echo "起動を待ちます: $TARGET_EXE"

        STARTED=0
        for _ in $(seq 1 20); do
            /bin/sleep 1
            if /bin/ps -A -o comm= | /usr/bin/grep -Fxq "$TARGET_EXE"; then
                STARTED=1
                break
            fi
        done

        if [ "$STARTED" -eq 1 ]; then
            echo "起動を確認しました。退避分を削除します"
            /bin/rm -rf "$BACKUP_APP"
        else
            echo "起動を確認できませんでした"
            rollback
            exit 1
        fi

        # 7) 後始末。作業場所を消したあと、自分のいる場所ごと消えて終わる。
        #    exec なので、これ以降スクリプトを読み直すことはない
        /bin/rm -rf "$WORK_DIR"
        echo "=== 完了 ==="
        exec /bin/rm -rf "$SCRIPT_DIR"

        """;

    /// <summary>
    /// 差し替えを開始する。呼び出したあと、アプリは速やかに終了すること。
    /// </summary>
    /// <param name="verifiedAppPath">検証を通した新しい .app。</param>
    /// <param name="targetBundlePath">差し替え先（いま動いている .app）。</param>
    /// <param name="workDirectory">
    /// 差し替え後に消してよい作業場所（DMG や取り出した .app を置いた所）。
    /// </param>
    /// <returns>起こしたスクリプトのプロセス。</returns>
    public Process Start(string verifiedAppPath, string targetBundlePath, string workDirectory)
    {
        Validate(verifiedAppPath, targetBundlePath);
        ValidateWorkDirectory(workDirectory, targetBundlePath);

        // スクリプトは作業場所の外に置く。作業場所ごと消すため
        var scriptDirectory = Path.Combine(
            Path.GetTempPath(), $"attachecase-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDirectory);

        var scriptPath = Path.Combine(scriptDirectory, "install.sh");
        File.WriteAllText(scriptPath, InstallScript);

        // 所有者だけが読み書き実行できるようにする。
        // 対象は macOS だけだが、解析器のために明示的に分岐しておく
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var backupPath = BuildBackupPath(targetBundlePath);

        var info = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add(scriptPath);
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        info.ArgumentList.Add(verifiedAppPath);
        info.ArgumentList.Add(targetBundlePath);
        info.ArgumentList.Add(backupPath);
        info.ArgumentList.Add(workDirectory);

        return Process.Start(info)
               ?? throw new InvalidOperationException("差し替えの処理を開始できませんでした。");
    }

    /// <summary>
    /// 退避先の名前。差し替え先と同じ階層に作る（別のボリュームへ mv すると
    /// 実体の複製になり、一瞬で終わらないうえ失敗もしやすい）。
    /// </summary>
    internal static string BuildBackupPath(string targetBundlePath)
    {
        var directory = Path.GetDirectoryName(targetBundlePath)
                        ?? throw new ArgumentException(
                            "差し替え先の親ディレクトリを求められません。", nameof(targetBundlePath));

        var name = Path.GetFileName(targetBundlePath);
        return Path.Combine(directory, $".{name}.updating");
    }

    /// <summary>
    /// 差し替えてよい組み合わせかを確かめる。ここを緩めると、更新のつもりで
    /// 無関係な場所を壊すことになる。
    /// </summary>
    internal static void Validate(string verifiedAppPath, string targetBundlePath)
    {
        if (string.IsNullOrWhiteSpace(verifiedAppPath))
        {
            throw new ArgumentException("新しい .app のパスが空です。", nameof(verifiedAppPath));
        }

        if (string.IsNullOrWhiteSpace(targetBundlePath))
        {
            throw new ArgumentException("差し替え先のパスが空です。", nameof(targetBundlePath));
        }

        var source = Path.GetFullPath(verifiedAppPath).TrimEnd('/');
        var target = Path.GetFullPath(targetBundlePath).TrimEnd('/');

        if (!source.EndsWith(".app", StringComparison.Ordinal) ||
            !target.EndsWith(".app", StringComparison.Ordinal))
        {
            throw new ArgumentException(".app 以外を差し替えの対象にはできません。");
        }

        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            throw new ArgumentException("差し替え元と先が同じです。");
        }

        // 新しい .app が差し替え先の中にあると、退避した時点で消える
        if (source.StartsWith(target + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException("新しい .app が差し替え先の中にあります。");
        }

        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"新しい .app がありません: {source}");
        }

        if (!Directory.Exists(target))
        {
            throw new DirectoryNotFoundException($"差し替え先がありません: {target}");
        }
    }

    /// <summary>
    /// 作業場所として渡してよいパスかを確かめる。
    ///
    /// スクリプトは最後にここを rm -rf する。差し替え先と重なっていると、
    /// せっかく配置した新しいアプリを、その直後に自分で消すことになる。
    /// 親を指していれば被害はもっと広い（/Applications を渡せば全部消える）。
    ///
    /// いまの呼び出し経路では一時ディレクトリしか渡らないが、破壊的な操作の
    /// 門番なので、渡されうるものは全部見る。
    /// </summary>
    internal static void ValidateWorkDirectory(string workDirectory, string targetBundlePath)
    {
        if (string.IsNullOrWhiteSpace(workDirectory))
        {
            throw new ArgumentException("作業場所のパスが空です。", nameof(workDirectory));
        }

        if (string.IsNullOrWhiteSpace(targetBundlePath))
        {
            throw new ArgumentException("差し替え先のパスが空です。", nameof(targetBundlePath));
        }

        var work = Path.GetFullPath(workDirectory).TrimEnd('/');
        var target = Path.GetFullPath(targetBundlePath).TrimEnd('/');

        if (string.Equals(work, target, StringComparison.Ordinal))
        {
            throw new ArgumentException("作業場所と差し替え先が同じです。", nameof(workDirectory));
        }

        // 作業場所が差し替え先の親（"/" を渡した場合もここで捕まる）
        if (target.StartsWith(work + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "作業場所が差し替え先を含んでいます。", nameof(workDirectory));
        }

        // 作業場所が差し替え先の中
        if (work.StartsWith(target + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "作業場所が差し替え先の中にあります。", nameof(workDirectory));
        }
    }

    /// <summary>
    /// 前回の更新でやり残した一時ディレクトリを片付ける。起動時に呼ぶ。
    /// 途中で電源が落ちた場合などに残る。
    /// </summary>
    public static void CleanUpStaleWorkDirectories(TimeSpan olderThan)
    {
        try
        {
            var temp = Path.GetTempPath();
            var cutoff = DateTime.UtcNow - olderThan;

            foreach (var pattern in new[]
                     { "attachecase-install-*", "attachecase-update-*", "attachecase-mount-*" })
            {
                foreach (var directory in Directory.GetDirectories(temp, pattern))
                {
                    try
                    {
                        if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                        {
                            Directory.Delete(directory, recursive: true);
                        }
                    }
                    catch (Exception)
                    {
                        // 消せないものは次の機会に
                    }
                }
            }
        }
        catch (Exception)
        {
            // 後片付けの失敗で起動を妨げない
        }
    }
}
