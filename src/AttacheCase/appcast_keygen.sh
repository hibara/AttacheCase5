#!/bin/bash

# =============================================================================
# 自動更新マニフェスト（appcast）に署名する Ed25519 鍵ペアを作る
# =============================================================================
# 一度だけ実行する。作った秘密鍵は二度と作り直せない前提で扱うこと
# （作り直すと、旧鍵で署名されたマニフェストを見ている利用者が更新できなくなる）。
#
# 使用法: ./appcast_keygen.sh [--backup] [出力先の秘密鍵パス]
#
#   --backup   予備の鍵として作る。貼り付け先が
#              UpdateBackupPublicKeyBase64 になる
#
# 既定の出力先は release.env の隣（appcast_ed25519.pem / appcast_ed25519_backup.pem）。
# gitignore 済み。
#
# 予備の鍵について
# -----------------------------------------------------------------------------
# 公開鍵はアプリに焼き込まれるので、配ってしまうと足せない。現用の鍵しか
# 知らない版を配ると、その利用者は秘密鍵が漏れても失われても二度と更新を
# 受け取れない。はじめから 2 本を信頼させておけば、予備の鍵で署名し直すだけで
# 済む。
#
# 予備の秘密鍵は、作ったらこのディレクトリに置いたままにしないこと。
# 現用と同じ場所にあると、片方が漏れる状況では両方漏れる。
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

IS_BACKUP=false
KEY_FILE=""

while [ $# -gt 0 ]; do
    case "$1" in
        --backup) IS_BACKUP=true; shift ;;
        --help|-h)
            sed -n '3,22p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        -*) echo "不明なオプション: $1" >&2; exit 1 ;;
        *)  KEY_FILE="$1"; shift ;;
    esac
done

if [ -z "$KEY_FILE" ]; then
    if [ "$IS_BACKUP" = true ]; then
        KEY_FILE="$SCRIPT_DIR/appcast_ed25519_backup.pem"
    else
        KEY_FILE="$SCRIPT_DIR/appcast_ed25519.pem"
    fi
fi

if [ "$IS_BACKUP" = true ]; then
    CONST_NAME="UpdateBackupPublicKeyBase64"
else
    CONST_NAME="UpdatePublicKeyBase64"
fi

# -----------------------------------------------------------------------------
# Ed25519 を扱える openssl を探す
#
# macOS 標準の /usr/bin/openssl は LibreSSL で、Ed25519 に対応していない。
# create-dmg と同じく Homebrew の OpenSSL を使う。
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
    echo "       macOS 標準の openssl は LibreSSL で Ed25519 に対応していません。" >&2
    echo "       brew install openssl@3 を実行するか、環境変数 OPENSSL で場所を指定してください。" >&2
    exit 1
fi

echo "使用する openssl: $OPENSSL_BIN ($("$OPENSSL_BIN" version))"
echo ""

# -----------------------------------------------------------------------------
# 取り返しがつかないので、既存の鍵は絶対に上書きしない
# -----------------------------------------------------------------------------
if [ -e "$KEY_FILE" ]; then
    echo "エラー: すでに鍵があります: $KEY_FILE" >&2
    echo "       上書きすると、旧鍵で署名したマニフェストを見ている利用者が" >&2
    echo "       更新できなくなります。作り直す場合は手動で退避してください。" >&2
    exit 1
fi

umask 077
"$OPENSSL_BIN" genpkey -algorithm ed25519 -out "$KEY_FILE"
chmod 600 "$KEY_FILE"

# 公開鍵の生の 32 バイトを取り出す。
# DER の SubjectPublicKeyInfo は 44 バイトで、末尾 32 バイトが鍵本体
PUBLIC_B64=$("$OPENSSL_BIN" pkey -in "$KEY_FILE" -pubout -outform DER \
    | tail -c 32 | base64)

echo "秘密鍵を作成しました: $KEY_FILE"
echo "  （gitignore 済み。バックアップは各自の責任で安全な場所へ）"
echo ""
echo "────────────────────────────────────────────────────────────────"
echo "以下の Base64 を Services/UpdateService.cs の"
echo "${CONST_NAME} に貼り付けてください。"
echo ""
echo "    public const string ${CONST_NAME} ="
echo "        \"$PUBLIC_B64\";"
echo "────────────────────────────────────────────────────────────────"

if [ "$IS_BACKUP" = true ]; then
    echo ""
    echo "予備の鍵は、貼り付けたあとこの場所から移してください。"
    echo "  現用: $SCRIPT_DIR/appcast_ed25519.pem"
    echo "  予備: $KEY_FILE"
    echo ""
    echo "同じフォルダーに並べて置くと、片方が漏れる状況では両方漏れます。"
    echo "別の媒体・別の保管場所へ移し、ここからは消してください。"
    echo ""
    echo "普段の署名には現用の鍵を使います。予備を使うのは、"
    echo "現用が漏れたか失われたときだけです。"
    echo "  ./appcast_generate.sh --key <予備の鍵のパス>"
fi
