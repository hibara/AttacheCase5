#!/bin/bash

# =============================================================================
# サイトに載せるリリース情報（<版>.md）を作る
# =============================================================================
# hibara.jp は Astro で、リリース情報を
#   www/src/content/releases/attachecase/mac/<版>.md
# に置いている。拡張子は .md だが、中身は YAML（フロントマター）。
#
# 必要な材料はビルドの成果物と変更点のファイルに全部そろっているので、
# 手で書き写さない。ハッシュを 6 個も手で写すのは、取り違えて当然の作業。
#
# 使用法: ./site_release_entry.sh [オプション]
#
# オプション:
#   --out PATH       書き出し先（既定: 標準出力）
#   --date YYYY-MM-DD  公開日（既定: 今日）
#   --version LABEL  version 欄の表記（既定: DMG 名から決める。b 付きなら 5.0.0β）
#   --in DIR         成果物のある場所（既定: bin/Publish）
#
# 出力先を指定しないのは、既にある .md を黙って上書きしないため。
# 中身を見て、よければ自分で置き換えること。
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

OUT_FILE=""
REL_DATE="$(date +%Y-%m-%d)"
VERSION_LABEL=""
IN_DIR="$SCRIPT_DIR/bin/Publish"

while [ $# -gt 0 ]; do
    case "$1" in
        --out)     OUT_FILE="$2"; shift 2 ;;
        --date)    REL_DATE="$2"; shift 2 ;;
        --version) VERSION_LABEL="$2"; shift 2 ;;
        --in)      IN_DIR="$2"; shift 2 ;;
        --help|-h) sed -n '3,25p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "不明なオプション: $1" >&2; exit 1 ;;
    esac
done

# -----------------------------------------------------------------------------
# 変更点を YAML の changes に開く
#
# release_notes.<lang>.txt の見出しは new / improvements / fixes。
# サイト側の enum は new / improve / fix なので、ここで対応させる
# （合わない値を書くと Astro のビルドが落ちる）
# -----------------------------------------------------------------------------
build_changes() {
    local file="$1" indent="$2"

    if [ ! -f "$file" ]; then
        echo "警告: 変更点のファイルがありません: $(basename "$file")" >&2
        return 0
    fi

    /usr/bin/awk -v indent="$indent" '
        function esc(s) {
            gsub(/\\/, "\\\\", s)
            gsub(/"/, "\\\"", s)
            return s
        }
        /^[[:space:]]*#/ { next }
        /^[[:space:]]*$/ { next }
        /^[[:space:]]*\[.*\][[:space:]]*$/ {
            kind = $0
            gsub(/^[[:space:]]*\[|\][[:space:]]*$/, "", kind)
            # サイト側の言い方に直す
            if (kind == "improvements") kind = "improve"
            else if (kind == "fixes")   kind = "fix"
            next
        }
        {
            if (kind == "") next
            line = $0
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", line)
            if (line == "") next
            printf "%s- type: %s\n%s  text: \"%s\"\n", indent, kind, indent, esc(line)
        }
    ' "$file"
}

# -----------------------------------------------------------------------------
# 成果物から、版・置き場・配布物を読む
#
# 版は .app の Info.plist、置き場と並びは DMG の名前が正。
# appcast_generate.sh と同じ採り方にそろえてある（食い違わせない）
# -----------------------------------------------------------------------------
VERSION=""
FOLDER=""
ARCHIVES=""

for rid_dir in "$IN_DIR"/osx-*; do
    [ -d "$rid_dir" ] || continue

    dmg_count=$(find "$rid_dir" -maxdepth 1 -type f -name "*.dmg" | wc -l | tr -d ' ')
    if [ "$dmg_count" -gt 1 ]; then
        echo "エラー: ${rid_dir} に DMG が ${dmg_count} 個あります。古いものを消してください。" >&2
        exit 1
    fi
    [ "$dmg_count" -eq 1 ] || continue

    dmg=$(find "$rid_dir" -maxdepth 1 -type f -name "*.dmg")
    app="$rid_dir/AttacheCase.app"
    [ -d "$app" ] || continue

    app_version=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" \
        "$app/Contents/Info.plist" 2>/dev/null || echo "")
    if [ -z "$app_version" ]; then
        echo "エラー: バージョンを取得できません: $app" >&2
        exit 1
    fi

    if [ -z "$VERSION" ]; then
        VERSION="$app_version"
    elif [ "$VERSION" != "$app_version" ]; then
        echo "エラー: アーキテクチャ間でバージョンが違います（${VERSION} と ${app_version}）。" >&2
        exit 1
    fi

    name=$(basename "$dmg")

    # 置き場は DMG の名前から。AtcMac500b-arm64.dmg → 500b
    dmg_folder=$(echo "$name" | sed -n 's/^[A-Za-z]*\([0-9][0-9A-Za-z]*\)-.*\.dmg$/\1/p')
    if [ -z "$FOLDER" ]; then
        FOLDER="$dmg_folder"
    elif [ "$FOLDER" != "$dmg_folder" ]; then
        echo "エラー: アーキテクチャ間で DMG の名前が食い違います（${FOLDER} と ${dmg_folder}）。" >&2
        exit 1
    fi

    case "$name" in
        *-arm64.dmg) arch="arm64"; title_ja="Apple Silicon版 (DMG)"; title_en="Apple Silicon (DMG)" ;;
        *-x64.dmg)   arch="x64";   title_ja="Intel版 (DMG)";        title_en="Intel (DMG)" ;;
        *) echo "警告: アーキテクチャを判定できません。飛ばします: $name" >&2; continue ;;
    esac

    echo "  ${arch}: ${name}" >&2

    ARCHIVES="${ARCHIVES}- title:
    ja: \"${title_ja}\"
    en: \"${title_en}\"
  filename: \"${name}\"
  arch: ${arch}
  md5: \"$(md5 -q "$dmg")\"
  sha1: \"$(shasum -a 1 "$dmg" | awk '{print $1}')\"
  sha256: \"$(shasum -a 256 "$dmg" | awk '{print $1}')\"
"
done

if [ -z "$VERSION" ] || [ -z "$ARCHIVES" ]; then
    echo "エラー: 配布物が見つかりません: $IN_DIR/osx-*/" >&2
    echo "       先に ./build_and_release.sh --dmg を実行してください。" >&2
    exit 1
fi

# ピリオドを抜いた数字。ファイル名（500.md）と vernum になる
VERNUM="$(echo "$VERSION" | tr -d '.')"

# version 欄の表記。置き場に印（b）が付いていればβとして見せる。
# --version で明示されていればそちらを使う
if [ -z "$VERSION_LABEL" ]; then
    case "$FOLDER" in
        "$VERNUM") VERSION_LABEL="$VERSION" ;;
        "$VERNUM"b) VERSION_LABEL="${VERSION}β" ;;
        *) VERSION_LABEL="$VERSION" ;;
    esac
fi

# コマンド置換は末尾の改行を落とすので、行の継ぎ目はテンプレート側で補っている
CHANGES_JA="$(build_changes "$SCRIPT_DIR/release_notes.ja.txt" "    ")"
CHANGES_EN="$(build_changes "$SCRIPT_DIR/release_notes.en.txt" "    ")"

ENTRY="---
version: ${VERSION_LABEL}
vernum: ${VERNUM}
date: '${REL_DATE}'
platform: mac
folder: '${FOLDER}'
i18n:
  ja:
    changes:
${CHANGES_JA}
  en:
    changes:
${CHANGES_EN}
archives:
${ARCHIVES}---"

if [ -n "$OUT_FILE" ]; then
    if [ -s "$OUT_FILE" ]; then
        echo "エラー: すでに中身のあるファイルです: $OUT_FILE" >&2
        echo "       消してから実行するか、標準出力で受け取って中身を見比べてください。" >&2
        exit 1
    fi
    printf '%s\n' "$ENTRY" > "$OUT_FILE"
    echo "" >&2
    echo "書き出しました: $OUT_FILE" >&2
    echo "置き場所: www/src/content/releases/attachecase/mac/${VERNUM}.md" >&2
else
    printf '%s\n' "$ENTRY"
    echo "" >&2
    echo "置き場所: www/src/content/releases/attachecase/mac/${VERNUM}.md" >&2
fi
