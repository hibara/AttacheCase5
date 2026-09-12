#!/bin/bash

# =============================================================================
# 自動更新マニフェスト（appcast.json）を作り、Ed25519 で署名する
# =============================================================================
# build_and_release.sh で DMG を作ったあとに実行する。
# bin/Publish/<rid>/ にある DMG を読み、サイズと SHA-256 を書き込む。
#
# 使用法: ./appcast_generate.sh [オプション]
#
# オプション:
#   --base-url URL           ファイルを実際に置く場所（既定: DEFAULT_BASE_URL）
#   --download-base-url URL  マニフェストに書く DMG の URL の基点。
#                            ダウンロード数の計測を経由させるため設置先とは別
#   --key PATH       署名に使う秘密鍵（既定: appcast_ed25519.pem）
#   --out DIR        出力先ディレクトリ（既定: bin/Publish）
#
# 出力:
#   appcast.json      配信マニフェスト
#   appcast.json.sig  その分離署名（Ed25519、生の 64 バイト）
#
# この 2 つと DMG を同じ場所に置くこと。
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ファイルを実際に置く場所
DEFAULT_BASE_URL="https://hibara.jp/dl/attachecase/mac"

# マニフェストに書く DMG の URL の基点。
# サイト側でダウンロード数を数えているため、配布物は計測を経由させる
# （/d/... が 302 で /dl/... へ返す。アプリは https のまま 1 段だけ追従する）。
#
# マニフェスト自体は計測を通さない。1 日 1 回とはいえ全利用者が毎日叩くので、
# 通すとダウンロード数が実態とかけ離れる
DEFAULT_DOWNLOAD_BASE_URL="https://hibara.jp/d/attachecase/mac"
DEFAULT_NOTES_URL="https://hibara.jp/software/attachecase/"

BASE_URL="$DEFAULT_BASE_URL"
DOWNLOAD_BASE_URL="$DEFAULT_DOWNLOAD_BASE_URL"
KEY_FILE="$SCRIPT_DIR/appcast_ed25519.pem"
OUT_DIR="$SCRIPT_DIR/bin/Publish"

while [ $# -gt 0 ]; do
    case "$1" in
        --base-url)          BASE_URL="$2"; shift 2 ;;
        --download-base-url) DOWNLOAD_BASE_URL="$2"; shift 2 ;;
        --key)      KEY_FILE="$2"; shift 2 ;;
        --out)      OUT_DIR="$2";  shift 2 ;;
        --help|-h)
            sed -n '3,25p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) echo "不明なオプション: $1" >&2; exit 1 ;;
    esac
done

BASE_URL="${BASE_URL%/}"
DOWNLOAD_BASE_URL="${DOWNLOAD_BASE_URL%/}"

# アプリ側は https 以外の URL を拒否する。ここで弾かないと、
# 「署名は正しいのに誰も更新できないマニフェスト」ができあがる
for _url in "$BASE_URL" "$DOWNLOAD_BASE_URL"; do
    case "$_url" in
        https://*) ;;
        *)
            echo "エラー: 配信の URL は https でなければなりません: ${_url}" >&2
            exit 1
            ;;
    esac
done
unset _url

# -----------------------------------------------------------------------------
# Ed25519 を扱える openssl を探す（appcast_keygen.sh と同じ理由）
# -----------------------------------------------------------------------------
find_openssl() {
    local candidate
    for candidate in \
        "${OPENSSL:-}" \
        /opt/homebrew/opt/openssl@3/bin/openssl \
        /usr/local/opt/openssl@3/bin/openssl \
        "$(command -v openssl 2>/dev/null || true)"
    do
        [ -n "$candidate" ] && [ -x "$candidate" ] || continue
        if "$candidate" genpkey -algorithm ed25519 -out /dev/null >/dev/null 2>&1; then
            echo "$candidate"
            return 0
        fi
    done
    return 1
}

if ! OPENSSL_BIN=$(find_openssl); then
    echo "エラー: Ed25519 を扱える openssl が見つかりません。" >&2
    echo "       brew install openssl@3 を実行してください。" >&2
    exit 1
fi

if [ ! -f "$KEY_FILE" ]; then
    echo "エラー: 署名鍵がありません: $KEY_FILE" >&2
    echo "       先に ./appcast_keygen.sh を実行してください。" >&2
    exit 1
fi

# -----------------------------------------------------------------------------
# 変更点（release_notes.<lang>.txt）を JSON の配列に組み立てる
#
#   [new]            → { "kind": "new", "items": [ ... ] }
#   行頭 # と空行は無視
#
# 見出しに出す語はアプリ側が持つ。ここでは種類だけを渡す
# -----------------------------------------------------------------------------
build_release_notes() {
    local file="$1"
    # 片方だけ無いと、その言語の利用者にだけ空の変更点を配ることになる。
    # 全体が無いときの警告（下方）はここでは効かないので、個別に言う
    if [ ! -f "$file" ]; then
        echo "警告: 変更点のファイルがありません: $(basename "$file")" >&2
        echo "       この言語の利用者には、更新画面に何も出ません。" >&2
        return 0
    fi

    /usr/bin/awk '
        function esc(s) {
            gsub(/\\/, "\\\\", s); gsub(/"/, "\\\"", s)
            gsub(/\t/, " ", s)
            return s
        }
        function close_section() {
            if (kind == "") return
            if (count == 0) { kind = ""; return }
            if (out != "") out = out ","
            out = out "\n      { \"kind\": \"" kind "\", \"items\": [" items "\n        ] }"
            kind = ""; items = ""; count = 0
        }
        /^[[:space:]]*#/ { next }
        /^[[:space:]]*$/ { next }
        /^[[:space:]]*\[.*\][[:space:]]*$/ {
            close_section()
            kind = $0
            gsub(/^[[:space:]]*\[|\][[:space:]]*$/, "", kind)
            next
        }
        {
            if (kind == "") next
            line = $0
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", line)
            if (line == "") next
            if (count > 0) items = items ","
            items = items "\n          \"" esc(line) "\""
            count++
        }
        END { close_section(); printf "%s", out }
    ' "$file"
}

# -----------------------------------------------------------------------------
# DMG を集める。アーキテクチャは .app の実行ファイルから判定する
# （ファイル名から推測すると、名前を変えたときに黙って壊れる）
# -----------------------------------------------------------------------------
VERSION=""
MIN_OS=""
RELEASE_DIR=""
ENTRIES=""

# エラー文で例を見せるためだけの語。判定には使わない
DMG_PREFIX_HINT="AtcMac"

for rid_dir in "$OUT_DIR"/osx-*; do
    [ -d "$rid_dir" ] || continue

    # -type f を付けるのは、末尾が .dmg のディレクトリを拾わないため。
    # 拾ってしまうと、正しい DMG が 1 つでも「2 個ある」と誤判定して止まる。
    #
    # DMG は 1 つに絞れなければならない。前回のビルドが残っていると、
    # バージョンは今の .app から採るのに URL とハッシュは古い DMG のもの、
    # という食い違ったマニフェストに署名してしまう
    # （--skip-build のときは出力先が掃除されないため実際に起こりうる）
    dmg_count=$(find "$rid_dir" -maxdepth 1 -type f -name "*.dmg" | wc -l | tr -d ' ')
    app="$rid_dir/AttacheCase.app"

    if [ "$dmg_count" -gt 1 ]; then
        echo "エラー: ${rid_dir} に DMG が ${dmg_count} 個あります。" >&2
        find "$rid_dir" -maxdepth 1 -type f -name "*.dmg" | sed 's/^/       /' >&2
        echo "       古いものを消してから作り直してください。" >&2
        exit 1
    fi

    [ "$dmg_count" -eq 1 ] || continue
    dmg=$(find "$rid_dir" -maxdepth 1 -type f -name "*.dmg")
    [ -d "$app" ] || continue

    exe="$app/Contents/MacOS/AttacheCase"
    if [ ! -f "$exe" ]; then
        echo "警告: 実行ファイルがありません。飛ばします: $app" >&2
        continue
    fi

    case "$(lipo -info "$exe" 2>/dev/null)" in
        *arm64*)  arch="arm64" ;;
        *x86_64*) arch="x64" ;;
        *) echo "警告: アーキテクチャを判定できません。飛ばします: $exe" >&2; continue ;;
    esac

    app_version=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" \
        "$app/Contents/Info.plist" 2>/dev/null || echo "")
    if [ -z "$app_version" ]; then
        echo "エラー: バージョンを取得できません: $app" >&2
        exit 1
    fi

    # 最低動作 OS は .app の Info.plist から採る。
    # ここに数字を直書きすると、Info.plist を上げたときに片方だけ古いままになり、
    # 「起動できない版を更新として配る」ことになる
    app_min_os=$(/usr/libexec/PlistBuddy -c "Print :LSMinimumSystemVersion" \
        "$app/Contents/Info.plist" 2>/dev/null || echo "")
    if [ -z "$app_min_os" ]; then
        echo "エラー: LSMinimumSystemVersion を取得できません: $app" >&2
        echo "       Info.plist に最低動作 OS を書いてください。" >&2
        exit 1
    fi

    if [ -z "$MIN_OS" ]; then
        MIN_OS="$app_min_os"
    elif [ "$MIN_OS" != "$app_min_os" ]; then
        echo "エラー: アーキテクチャ間で最低動作 OS が違います（${MIN_OS} と ${app_min_os}）。" >&2
        exit 1
    fi

    if [ -z "$VERSION" ]; then
        VERSION="$app_version"
    elif [ "$VERSION" != "$app_version" ]; then
        # 片方だけ古いビルドが残っていると、混ざったマニフェストができてしまう
        echo "エラー: アーキテクチャ間でバージョンが違います（${VERSION} と ${app_version}）。" >&2
        echo "       両方を同じ版でビルドし直してください。" >&2
        exit 1
    fi

    size=$(stat -f%z "$dmg")
    sha=$(shasum -a 256 "$dmg" | awk '{print $1}')
    name=$(basename "$dmg")

    # 版ごとのディレクトリ名を DMG の名前から取り出す。
    # AtcMac500b-arm64.dmg → 500b（先頭の英字を落とし、-アーキ以降を捨てる）
    dmg_release_dir=$(echo "$name" | sed -n 's/^[A-Za-z]*\([0-9][0-9A-Za-z]*\)-.*\.dmg$/\1/p')

    if [ -z "$RELEASE_DIR" ]; then
        RELEASE_DIR="$dmg_release_dir"
    elif [ "$RELEASE_DIR" != "$dmg_release_dir" ]; then
        # 片方だけ古い DMG が残っていると、置き場の違う 2 本を指すことになる
        echo "エラー: アーキテクチャ間で DMG の名前が食い違います（${RELEASE_DIR} と ${dmg_release_dir}）。" >&2
        exit 1
    fi

    echo "  ${arch}: ${name}（${size} バイト）"

    [ -n "$ENTRIES" ] && ENTRIES="$ENTRIES,"
    ENTRIES="$ENTRIES
    \"$arch\": {
      \"url\": \"$DOWNLOAD_BASE_URL/@RELEASE_DIR@/$name\",
      \"size\": $size,
      \"sha256\": \"$sha\"
    }"
done

if [ -z "$ENTRIES" ]; then
    echo "エラー: DMG が見つかりません: $OUT_DIR/osx-*/" >&2
    echo "       先に ./build_and_release.sh --dmg を実行してください。" >&2
    exit 1
fi

# -----------------------------------------------------------------------------
# マニフェストを書き出して署名する
# -----------------------------------------------------------------------------
# 版ごとのディレクトリ名は、DMG の名前から採る（AtcMac500b-arm64.dmg → 500b）。
#
# ここで別に組み立てると、DMG の名前と食い違ったマニフェストができる。
# 「β」かどうかは名前に b が付くかどうかの違いでしかなく、動きは変わらないので、
# 印を持たずに実物の名前へ従う
if [ -z "$RELEASE_DIR" ]; then
    echo "エラー: DMG の名前から版のディレクトリ名を読み取れません。" >&2
    echo "       想定する形: ${DMG_PREFIX_HINT}<版><任意の印>-<アーキ>.dmg" >&2
    exit 1
fi

# 読み違えていないかを確かめる。版の数字で始まっていなければ何かがおかしい
VERSION_DIGITS="$(echo "$VERSION" | tr -d '.')"
case "$RELEASE_DIR" in
    "$VERSION_DIGITS"*) ;;
    *)
        echo "エラー: DMG の名前と .app のバージョンが合いません。" >&2
        echo "       名前から読んだ版: ${RELEASE_DIR} / .app の版: ${VERSION}" >&2
        exit 1
        ;;
esac

ENTRIES="${ENTRIES//@RELEASE_DIR@/$RELEASE_DIR}"

MANIFEST="$OUT_DIR/appcast.json"
SIGNATURE="$MANIFEST.sig"
PUB_DATE=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

NOTES_JA="$(build_release_notes "$SCRIPT_DIR/release_notes.ja.txt")"
NOTES_EN="$(build_release_notes "$SCRIPT_DIR/release_notes.en.txt")"

NOTES_BLOCK=""
if [ -n "$NOTES_JA" ] || [ -n "$NOTES_EN" ]; then
    NOTES_BLOCK='
  "releaseNotes": {'
    _sep=""
    if [ -n "$NOTES_JA" ]; then
        NOTES_BLOCK="$NOTES_BLOCK
    \"ja\": [$NOTES_JA
    ]"
        _sep=","
    fi
    if [ -n "$NOTES_EN" ]; then
        NOTES_BLOCK="$NOTES_BLOCK$_sep
    \"en\": [$NOTES_EN
    ]"
    fi
    NOTES_BLOCK="$NOTES_BLOCK
  },"
    unset _sep
else
    echo "警告: 変更点のファイルが見つかりません。更新画面には何も出ません。" >&2
    echo "       release_notes.ja.txt / release_notes.en.txt を用意してください。" >&2
fi

cat > "$MANIFEST" << EOF
{
  "version": "$VERSION",
  "minimumSystemVersion": "$MIN_OS",
  "pubDate": "$PUB_DATE",
  "releaseNotesUrl": "$DEFAULT_NOTES_URL",$NOTES_BLOCK
  "builds": {$ENTRIES
  }
}
EOF

# アプリ側は「受け取ったバイト列そのもの」を検証するので、
# 署名したあとにこのファイルを整形し直してはいけない
"$OPENSSL_BIN" pkeyutl -sign -inkey "$KEY_FILE" -rawin \
    -in "$MANIFEST" -out "$SIGNATURE"

# 自分で検証してから終わる。壊れた署名を配ると全利用者が更新できなくなる
if ! "$OPENSSL_BIN" pkeyutl -verify -inkey "$KEY_FILE" -rawin \
        -in "$MANIFEST" -sigfile "$SIGNATURE" >/dev/null 2>&1; then
    echo "エラー: 署名の自己検証に失敗しました。" >&2
    rm -f "$SIGNATURE"
    exit 1
fi

echo ""
echo "作成しました:"
echo "  $MANIFEST"
echo "  $SIGNATURE  ($(wc -c < "$SIGNATURE" | tr -d ' ') バイト)"
echo ""
echo "バージョン: ${VERSION}"
echo "最低動作 OS: macOS ${MIN_OS}（.app の Info.plist から）"
echo ""
echo "置き場所（ファイルの設置先）:"
echo "  ${BASE_URL}/"
echo "      appcast.json / appcast.json.sig"
echo "  ${BASE_URL}/${RELEASE_DIR}/"
echo "      DMG 2 本"
echo ""
echo "マニフェストが指す DMG の URL（計測を経由）:"
echo "  ${DOWNLOAD_BASE_URL}/${RELEASE_DIR}/"
echo ""
echo "マニフェストは版ごとのディレクトリに置かないこと。"
echo "アプリは固定の URL を見るため、置き場が変わると次の版を見つけられない。"
