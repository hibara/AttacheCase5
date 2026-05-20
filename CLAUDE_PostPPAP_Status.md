# Post-PPAP 実装ステータス & 引き継ぎドキュメント

> **目的**: このドキュメントは Post-PPAP 機能の現在の実装状態と、
> 次のセッションで「サーバ側のレート制限・データ保持ポリシー・運用設計」の議論を
> 途切れなく開始できるようにするための引き継ぎ資料です。
>
> **関連仕様書**:
> - `CLAUDE_PostPPAP_Spec.md` — 暗号方式・バイナリフォーマット・鍵管理の技術仕様
> - `post-ppap-security-hardening.md` — 2026-04-07 のセキュリティ強化作業の記録
>
> **最終更新**: 2026-05-11

---

## 1. 実装完了している機能

### 1.1 クライアント(AttacheCase ver.5 / C# WinForms / .NET 4.8)

| 機能 | 状態 | 主要ファイル |
|------|------|------------|
| RSA-4096 + ML-KEM-768 鍵ペア生成 | **完了** | `PostPpapManager.cs` L163-208 |
| キーサーバへの登録(メール認証フロー) | **完了** | `PostPpapManager.cs` L163-258 |
| キーサーバからの公開鍵検索 | **完了** | `PostPpapManager.cs` L288-362 |
| ハイブリッド暗号化(RSA-4096-OAEP + ML-KEM-768 KEM) | **完了** | `FileEncrypt5.cs`, `CryptoHelper5.cs` |
| ハイブリッド復号 | **完了** | `FileDecrypt5.cs`, `CryptoHelper5.cs` |
| 秘密鍵のローカル保存(DPAPI 暗号化) | **完了** | `PostPpapManager.cs` L223-235 |
| GUI からの登録・暗号化・復号 | **完了** | `Form1.cs` |
| コマンドライン暗号化 (`/p-ppap=email`) | **完了** | `AppSettings.cs`, `Form1.cs` |
| コマンドライン RSA 暗号化 (`/pubkey=path`) | **完了** | `AppSettings.cs`, `Form1.cs` |
| コマンドライン RSA 復号 (`/privkey=path`) | **完了** | `AppSettings.cs`, `Form1.cs` |
| コマンドライン RSA 鍵ペア生成 (`/genrsa=dir`) | **完了** | `AppSettings.cs`, `Form1.cs` |
| Post-PPAP はハイブリッド必須(RSA-only フォールバック禁止) | **完了** | `PostPpapManager.cs` L342-348 |
| **deviceToken による認証** | **完了**(2026-04-07) | `PostPpapKeyInfo.cs`, `PostPpapManager.cs` |
| **検索 API への lastSeen 自動更新ヘッダ送信** | **完了**(2026-04-07) | `PostPpapManager.cs` |
| **`atc5` トークン廃止**(GCM 認証タグで完結) | **完了**(2026-04-13) | `FileEncrypt5.cs`, `FileDecrypt5.cs` |
| **`/p=` パスワード指定時の確認画面停止バグ修正** | **完了**(2026-04-13) | `Form1.cs` |
| **`RoundtripTest.cs`** (DEBUG 専用、`--roundtrip-test`) | **完了**(2026-04-13) | `RoundtripTest.cs` |

### 1.2 キーサーバ(Cloudflare Workers / Hono + KV)

| 機能 | 状態 | 場所 |
|------|------|------|
| 登録開始 API (`POST /api/register/initiate`) | **完了** | `index.ts` L9-72 |
| メール認証 (`GET /verify/:token`) | **完了** | `index.ts` L74-108 |
| ポーリング API (`GET /api/register/status/:pollToken`) | **完了** | `index.ts` L110-119 |
| 公開鍵検索 API (`GET /api/keys/:emailHash`) | **完了** | `index.ts` L121-141 |
| `algorithmId` / `mlkemPublicKey` の保存・返却 | **完了** | `types.ts`, `index.ts` |
| メール送信(Resend API) | **完了** | `index.ts` L157-189 |
| デプロイ済み | **完了** | `attachecase-key-server.m-229.workers.dev` |
| **3 段階レート制限**(メール 10分 / 日次 50通 / IP 1時間 5回) | **完了**(2026-04-07) | `post-ppap-security-hardening.md` §1 |
| **入力バリデーション**(fingerprint、modulus、exponent、algorithmId、mlkemPublicKey) | **完了**(2026-04-07) | `post-ppap-security-hardening.md` §2 |
| **検索 API の 1 秒遅延**(列挙攻撃 / タイミング攻撃対策) | **完了**(2026-04-07) | `post-ppap-security-hardening.md` §1.2 |
| **lastSeen 自動更新 + ハートビート API** | **完了**(2026-04-07) | `post-ppap-security-hardening.md` §3 |
| **鍵削除 API** (`DELETE /api/keys/:emailHash/:fingerprint`) | **完了**(2026-04-07) | `post-ppap-security-hardening.md` §4 |

### 1.3 テスト結果(2026-04-06 確認済み)

| テスト | 結果 |
|--------|------|
| RSA 鍵ペア生成 (`/genrsa`) | **PASS** |
| RSA v5 暗号化 (`/pubkey`) | **PASS** |
| RSA v5 復号 (`/privkey`) | **PASS** |
| RSA v4 復号(後方互換) | **PASS** |
| Post-PPAP 暗号化 (`/p-ppap=m@hibara.org`) | **PASS** |
| Post-PPAP GUI 登録 → メール認証 → 暗号化 | **PASS** |

---

## 2. 未実装・要議論の項目(旧 2026-04-06 時点)

> **注**: 以下の議論ポイントは 2026-04-06 時点で「未解決」だったもの。
> その後 2026-04-07 のセキュリティ強化作業でほぼすべて解決された。
> 詳細は `post-ppap-security-hardening.md` を参照。

### 2.1 サーバ側レート制限(最重要)

**✅ 2026-04-07 解決済み → `post-ppap-security-hardening.md` §1 参照**

実装内容:
- 同一メール: 10分に1回(`ratelimit:email:` キー、TTL 600秒)
- 日次グローバル: 50通/日(Resend 100通/日の安全マージン)
- 同一 IP: 1時間に5回(`cf-connecting-ip` ヘッダー使用)
- 検索 API: 1秒の意図的遅延(setTimeout、CPU 時間にカウントされない)

### 2.2 データ保持ポリシー

**🟡 部分解決(2026-04-07) → 完全な自動削除ポリシーは未策定**

実装内容:
- `lastSeen` 自動更新の基盤あり(検索 API 経由、24時間に1回まで)
- 上限 100 鍵/メールアドレスは既存仕様
- **未策定**: 古い鍵の自動削除 TTL、鍵の有効期限、ユーザー通知

### 2.3 鍵の削除・更新 API

**✅ 2026-04-07 解決済み → `post-ppap-security-hardening.md` §4 参照**

実装内容:
- `DELETE /api/keys/:emailHash/:fingerprint`(deviceToken 認証)
- 同一アカウントのいずれかの鍵の deviceToken で認証可能(別 PC からの操作対応)
- 全鍵削除時は KV エントリ自体を削除
- **未実装**: クライアント UI(デバイス一覧・削除ボタン)。設計資料では「フェーズ2: 上級者向け設定の奥」に分類

### 2.4 セキュリティ強化

**✅ 2026-04-07 解決済み → `post-ppap-security-hardening.md` §1, §2 参照**

| 項目 | 旧状態 | 現状(2026-04-07 以降) |
|------|------|------|
| CORS | `origin: '*'` | デスクトップアプリのため `*` を継続採用 |
| 入力バリデーション | 最低限 | fingerprint/modulus/exponent/algorithmId/mlkemPublicKey すべて検証 |
| HMAC_SECRET | 環境変数に予約のみ | 引き続き予約(deviceToken 方式で代替) |
| リクエストサイズ制限 | なし | バリデーションでフィールド単位に上限を設定 |
| メール送信結果の確認 | fire-and-forget | レート制限カウンターは送信成功時のみ記録 |

### 2.5 運用・監視

**🟡 部分対応**

- ✅ Cloudflare Workers のログ: `console.log` でレート制限ヒット記録、Workers Logs 有効化済み
- 🟡 Resend API のメール送信状況監視: 日次 50 通上限で間接的に制御
- 🟡 KV ストレージ使用量の監視: 未自動化
- ❌ エラー通知(登録失敗、メール送信失敗など): 未実装

---

## 3. アーキテクチャ概要図

```
┌─────────────────────────────────────┐
│  AttacheCase ver.5 (WinForms)       │
│                                     │
│  PostPpapManager.cs                 │
│    ├─ StartRegistrationAsync()      │──── POST /api/register/initiate ────┐
│    ├─ AwaitVerificationAsync()      │──── GET  /api/register/status/:t ───┤
│    └─ LookupPublicKeyAsync()        │──── GET  /api/keys/:hash ──────────┤
│       (X-My-* ヘッダ付き)            │                                     │
│                                     │                                     │
│  accounts.json (DPAPI encrypted)    │                                     │
│    ├─ RSA-4096 秘密鍵 (CSP Blob)   │                                     │
│    ├─ ML-KEM-768 秘密鍵 (PKCS#8)   │                                     │
│    └─ DeviceToken (認証用)          │                                     │
└─────────────────────────────────────┘                                     │
                                                                            ▼
                                      ┌─────────────────────────────────────┐
                                      │  Cloudflare Workers                 │
                                      │  attachecase-key-server             │
                                      │  (Hono + TypeScript)                │
                                      │                                     │
                                      │  KEYS_KV:                           │
                                      │    key = SHA-256(email)             │
                                      │    val = PublicKeyEntry[] JSON      │
                                      │      { modulus, exponent,           │
                                      │        fingerprint, algorithmId,    │
                                      │        mlkemPublicKey,              │
                                      │        registeredAt, lastSeen,      │
                                      │        deviceToken (内部のみ)        │
                                      │      }                              │
                                      │                                     │
                                      │  PENDING_KV:                        │
                                      │    verify:{token} → PendingEntry    │
                                      │    poll:{token} → { status,         │
                                      │                     deviceToken }   │
                                      │    ratelimit:email:{hash}           │
                                      │    ratelimit:daily:{date}           │
                                      │    ratelimit:ip:{ip}                │
                                      │    TTL = 24h / 5min / 10min /       │
                                      │          24h / 1h                   │
                                      │                                     │
                                      │  Secrets:                           │
                                      │    RESEND_API_KEY                   │
                                      │    HMAC_SECRET (未使用・予約)       │
                                      │                                     │
                                      │  メール送信: Resend API             │
                                      │    from: noreply@support.hibara.jp  │
                                      └─────────────────────────────────────┘
```

---

## 4. 登録フロー詳細

```
ユーザー(GUI)                  クライアント                   サーバ                    メール
    │                              │                           │                        │
    │ メアド入力                    │                           │                        │
    │──────────────────────────────>│                           │                        │
    │                              │ RSA-4096 鍵ペア生成       │                        │
    │                              │ ML-KEM-768 鍵ペア生成     │                        │
    │                              │ fingerprint 計算          │                        │
    │                              │                           │                        │
    │                              │ POST /register/initiate   │                        │
    │                              │ { email, modulus,          │                        │
    │                              │   exponent, fingerprint,   │                        │
    │                              │   algorithmId: 0x12,       │                        │
    │                              │   mlkemPublicKey }         │                        │
    │                              │──────────────────────────>│                        │
    │                              │                           │ レート制限チェック     │
    │                              │                           │ 入力バリデーション     │
    │                              │                           │ verifyToken 生成       │
    │                              │                           │ pollToken 生成         │
    │                              │                           │ PENDING_KV に保存      │
    │                              │                           │───────────────────────>│
    │                              │                           │  確認メール送信        │
    │                              │    { pollToken }          │                        │
    │                              │<──────────────────────────│                        │
    │                              │                           │                        │
    │                              │ 秘密鍵を DPAPI 暗号化    │                        │
    │                              │ accounts.json に保存      │                        │
    │                              │                           │                        │
    │                              │ GET /status/:pollToken    │                        │
    │                              │──────────────────────────>│                        │
    │                              │   { status: "pending" }   │                        │
    │                              │<──────────────────────────│                        │
    │                              │    (3秒間隔でポーリング)   │                        │
    │                              │                           │                        │
    │                              │                           │   ユーザーがリンク     │
    │                              │                           │<──────────────────────│
    │                              │                           │   GET /verify/:token   │
    │                              │                           │   → deviceToken 生成   │
    │                              │                           │   → KEYS_KV に公開鍵保存│
    │                              │                           │   → poll status =      │
    │                              │                           │       "verified" +     │
    │                              │                           │       deviceToken      │
    │                              │                           │                        │
    │                              │ GET /status/:pollToken    │                        │
    │                              │──────────────────────────>│                        │
    │                              │   { status: "verified",   │                        │
    │                              │     deviceToken }         │                        │
    │                              │<──────────────────────────│                        │
    │                              │ deviceToken を            │                        │
    │                              │ accounts.json に保存      │                        │
    │   登録完了                    │                           │                        │
    │<──────────────────────────────│                           │                        │
```

---

## 5. コマンドライン引数一覧

```
AttacheCase.exe [options] [files...]

パスワード暗号化:
  /e          暗号化モード
  /d          復号モード
  /p=PASSWORD パスワード指定

RSA 暗号化(従来の RSA 鍵交換):
  /genrsa=DIR        RSA-4096 鍵ペアを DIR に生成(.atcpub / .atcpvt)
  /pubkey=FILE.atcpub  公開鍵 XML ファイルを指定して暗号化
  /privkey=FILE.atcpvt 秘密鍵 XML ファイルを指定して復号

Post-PPAP 暗号化(ハイブリッド: RSA-4096 + ML-KEM-768):
  /p-ppap=EMAIL      宛先メールアドレスを指定(キーサーバから公開鍵を取得→暗号化)

DEBUG ビルド専用:
  --roundtrip-test   ラウンドトリップテスト実行(2026-04-13 追加)

共通:
  /exit=1     処理完了後に自動終了

注意:
  - Git Bash / MSYS 環境では MSYS_NO_PATHCONV=1 を付けること
  - /p-ppap による登録はコマンドラインからは不可(GUI のみ)
```

---

## 6. 技術的な注意事項(セッション中に発見したもの)

### 6.1 BouncyCastle 2.6.2 の API 注意点

```csharp
// ML-KEM 公開鍵の復元: FromEncoding 静的メソッドを使う
// ❌ new MLKemPublicKeyParameters(MLKemParameters.ml_kem_768, bytes) → コンストラクタ不在
// ✅ MLKemPublicKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, bytes)

// ML-KEM 秘密鍵の復元: PqcPrivateKeyFactory.CreateKey を使う
// ❌ new MLKemPrivateKeyParameters(...) → コンストラクタ不在
// ✅ (MLKemPrivateKeyParameters)PqcPrivateKeyFactory.CreateKey(pkcs8DerBytes)
```

この情報は `CLAUDE_PostPPAP_Spec.md` のセクション 12 にも追記すべき。

### 6.2 ML-KEM 公開鍵の保存形式

- **`GetEncoded()`**: raw bytes (1184 bytes for ML-KEM-768)
- **サーバには `GetEncoded()` の Base64 で保存**(SubjectPublicKeyInfo DER ではない)
- **復元時は `FromEncoding()` で raw bytes から直接復元**
- 秘密鍵は `PrivateKeyInfoFactory.CreatePrivateKeyInfo().GetDerEncoded()` で PKCS#8 DER 形式

### 6.3 コマンドライン引数パーサーの修正履歴

- `IndexOf("/") == -1` → `!StartsWith("/")` に修正(ファイルパス中の `/` 誤判定防止)
- `FileType` 配列サイズ: 4 → 7 に拡張(`CheckFileType` が 0-6 を返すため)
- 2026-04-13: `/p=` パスワード指定時に確認画面で止まるバグ修正(`fMemPasswordExe` 依存解消)

---

## 7. サーバ側の議論を始めるためのコンテキスト

### 7.1 現在のインフラ構成

| 項目 | 値 |
|------|-----|
| ホスティング | Cloudflare Workers (無料プラン) |
| ストレージ | Cloudflare KV × 2 (KEYS_KV, PENDING_KV) |
| メール送信 | Resend API (無料: 100通/日、内 50通/日まで使用許可) |
| フレームワーク | Hono v4.6 + TypeScript |
| ドメイン | attachecase-key-server.m-229.workers.dev |
| デプロイ | `npx wrangler deploy` |
| ソース | `attachecase-key-server/` ディレクトリ |

### 7.2 Cloudflare 無料プランの制限

| リソース | 制限 |
|---------|------|
| Workers リクエスト | 100,000/日 |
| KV 読み取り | 100,000/日 |
| KV 書き込み | 1,000/日 |
| KV ストレージ | 1 GB |
| KV 値サイズ | 25 MB |
| Worker CPU 時間 | 10ms/リクエスト |

**特に KV 書き込み 1,000/日 が登録のボトルネックになりうる。**
登録1回で最低 6 回の KV 書き込み(PENDING_KV, KEYS_KV, レート制限カウンタ × 3)。
→ 1日あたり最大 ~150 件の新規登録が物理上限。日次 50通制限はこの安全マージン。

### 7.3 今後の議論で決めるべきこと(優先順)

1. **鍵の有効期限と自動削除**(残る最大の議論ポイント)
   - KV の `expirationTtl` を活用するか、アプリロジックで管理するか
   - lastSeen ベースの TTL(例: 1年無更新で自動削除)
   - ユーザー通知(鍵期限切れ前の警告メール?)

2. **秘密鍵のバックアップ・移行**
   - 現在は `%APPDATA%\AttacheCase\PostPPAP\accounts.json` に DPAPI 暗号化で保存
   - DPAPI は同一 Windows ユーザーでのみ復号可能 → PC 移行不可
   - エクスポート機能(パスワード保護付き PKCS#12?)の要否

3. **クライアント側 UI の追加**
   - 鍵削除 API はサーバ側実装済みだがクライアント UI 未実装
   - 「上級者向け設定の奥」に配置予定

4. **abuse 対策の追加層**
   - 存在しないメールアドレスへの登録スパムは レート制限で抑止済み
   - 悪意ある公開鍵の登録(正当な所有者以外による) → メール認証で抑止済み
   - 追加対策の要否

---

## 8. ファイル一覧(次のセッションで読むべきファイル)

### クライアント側(優先度順)
```
AttacheCase/PostPpapManager.cs       ← 鍵管理の中核。最初に読む
AttacheCase/PostPpapKeyInfo.cs       ← データモデル(DeviceToken 含む)
AttacheCase/CryptoHelper5.cs         ← 暗号プリミティブ
AttacheCase/FileEncrypt5.cs          ← 暗号化エンジン
AttacheCase/FileDecrypt5.cs          ← 復号エンジン
AttacheCase/AppSettings.cs           ← コマンドライン引数
AttacheCase/RoundtripTest.cs         ← ラウンドトリップテスト(DEBUG)
CLAUDE_PostPPAP_Spec.md              ← 暗号仕様書(プロジェクトルート)
post-ppap-security-hardening.md      ← セキュリティ強化記録(プロジェクトルート)
```

### サーバ側
```
attachecase-key-server/src/index.ts  ← API 全実装
attachecase-key-server/src/types.ts  ← 型定義
attachecase-key-server/wrangler.toml ← Workers 設定
attachecase-key-server/package.json  ← 依存関係
```

---

## 9. 2026-04-07 以降の主要変更

### 9.1 サーバ側セキュリティ強化(2026-04-07)
詳細は `post-ppap-security-hardening.md` 参照。要点:
- 3 段階レート制限(メール 10分 / 日次 50通 / IP 1時間 5回)
- 入力バリデーション(全フィールド)
- 検索 API の 1 秒遅延(列挙・タイミング攻撃対策)
- deviceToken による認証(他者の lastSeen 改竄防止)
- lastSeen 自動更新(検索 API 統合、24時間に1回)
- 独立ハートビート API(副経路)
- 鍵削除 API(同一アカウントの deviceToken 認証)

### 9.2 ファイルフォーマットの簡素化(2026-04-13)
- `atc5` トークンを v5 暗号化ヘッダから廃止(GCM 認証タグで正誤判定が完結)
- `FileEncrypt5.cs`: `ATC_ENCRYPTED_TOKEN` 定数と 4byte 書き込みを削除
- `FileDecrypt5.cs`: `ParseDecryptedHeader()` の `"atc5"` チェックを削除

### 9.3 コマンドライン修正(2026-04-13)
- `/p=` パスワード指定時に確認画面で止まるバグ修正(`fMemPasswordExe` 依存解消)
- `RoundtripTest.cs` 追加(DEBUG 専用、`--roundtrip-test`)

### 9.4 ドキュメント整理(2026-05-11)
- プロジェクトのドキュメント運用ルールを確立(`CLAUDE.md` 内「🧠 記憶ポリシー」)
- 記憶の源泉は `TODO.md` に集約
- 仕様書を `attachecase-key-server/doc/` からプロジェクトルートに移動

---

## 10. 残る未解決事項(2026-05-11 時点)

### 10.1 AES-GCM の AAD に平文ヘッダを含めるかの設計判断

- **文脈**: `FileEncrypt5.cs` / `FileDecrypt5.cs`。現状 AES-GCM の AAD は使用していない
- **問題**: 平文ヘッダ(offset 24 のモードバイト、offset 26 の `PkaAlgorithmId` 等)が認証範囲外
  - 攻撃者がヘッダを書き換えても GCM 認証タグでは検出できない
  - 例: モードバイトを書き換えて別アルゴリズムでの復号を試みさせる攻撃の可能性
- **判断**: 含めない選択は意図的か、見直すべきか確定する必要がある

### 10.2 大容量ファイル復号時のメモリ消費

- **文脈**: `FileDecrypt5.Decrypt` が `compressedBody` を一度全部メモリ展開する
- **問題**: 数 GB のファイルでメモリ消費が問題になる可能性
- **解決方向**: 逐次 Deflate 解凍 + 逐次 GCM 検証によるストリーム化(設計検討要)

### 10.3 鍵の有効期限・自動削除ポリシー(再掲)

- **文脈**: §7.3 の最優先議論項目
- **基盤**: lastSeen 自動更新は実装済み(§2.2 部分解決)
- **未策定**: 具体的な TTL 値、ユーザー通知方法

### 10.4 秘密鍵のバックアップ・PC 移行機能(再掲)

- **文脈**: §7.3 の優先議論項目
- **問題**: DPAPI のマシン依存性、別 PC で `accounts.json` を復号不可
- **未策定**: エクスポート/インポート機構の設計、復元時の安全性確保

### 10.5 鍵削除 UI

- **文脈**: §2.3 の続き
- **基盤**: サーバ側 `DELETE /api/keys/:emailHash/:fingerprint` 実装済み
- **未実装**: クライアント UI(デバイス一覧・削除ボタン)。「フェーズ2: 上級者向け設定」配置予定

---

*このドキュメントは Claude Code セッション間の引き継ぎ用に作成されました。*
*2026-05-11: 2026-04-07 のセキュリティ強化、2026-04-13 のフォーマット簡素化、残る未解決事項を反映。*
