# AttacheCase ver.5 — プロジェクト指示書(Claude Code 用)

このファイルはセッション開始時に Claude Code が自動的に読み込みます。
作業に着手する前に、以下を順に行ってください。

1. このファイル(`CLAUDE.md`)を読む
2. `TODO.md` を読み、進行中タスクと保留事項を把握する
3. 必要に応じて `CLAUDE_PostPPAP_Spec.md` / `CLAUDE_PostPPAP_Status.md` / `post-ppap-security-hardening.md` / `_doc\データ構造.xlsx` を参照する
4. プロジェクトフォルダ全体の構造を把握する
5. その後、ユーザーの次の指示を待つ

---

## 🧠 記憶ポリシー(最重要・常時遵守)

**このプロジェクトにおける「記憶の源泉」は `TODO.md` ただ一つです。**

セッション内のあなた(Claude Code)の内部状態は、セッション終了で完全に失われます。一方 `TODO.md` は Dropbox 経由で全 PC(自宅／シェアオフィス)に同期され、次回セッションでも読み込まれます。したがって:

### ルール

1. **「自分の頭の中にメモする」ことを禁止する**。永続化したい事柄は **必ず `TODO.md` に書き出す**
2. 以下のような発言をする場面では、**同じターン内で実際に `TODO.md` を編集する**こと。発言だけで終わらせない:
   - 「記憶しておきます」「覚えておきます」「念のため記録しておきます」
   - 「次のバージョンアップまでの課題としましょう」「保留にしておきます」
   - 「あとで検討します」「将来的に対応します」
   - 「TODO に追加しておきます」「タスクとして残しておきます」
   - 「ここは引き続き観察します」「次回の作業で確認します」
3. ユーザーが「これは覚えておいて」「TODO に追加して」と明示した場合も同様
4. 完了したタスク、確定した判断、撤回された決定も `TODO.md` に反映する(「完了済みだが記録しておきたい判断」セクションに移動／該当行を更新)

### 追記フォーマット

`TODO.md` の **「検討待ち / 次バージョンまでに決める」** セクションに追記する場合:

```
- [ ] (YYYY-MM-DD) 〔課題の見出し〕
      - 文脈: 関連するファイル名／関数名／クラス名
      - 内容: 何を保留にしたか、何を決める必要があるか(1〜3 行)
```

**完了済み判断** に移動する場合は「完了済みだが記録しておきたい判断」セクションへ、根拠を 1 行添えて記載。

### 禁止行為

- ❌ 「記憶しておきます」とだけ言って、ファイルを更新しない
- ❌ 内部状態だけで保留事項を保持する
- ❌ `TODO.md` を読まずに過去の保留事項を語る(必ず実ファイルを参照する)

---

## 開発者プロフィール

- **Mitsuhiro Hibara** — AttacheCase5 メイン開発者
- C# / .NET Framework 4.8 / WinForms
- 日本語でコミュニケーション
- UI/UX のデザインにこだわりがあり、色の微調整を重視
- 技術的制約の **WHY** を知りたがる(理由まで説明すること)
- Visual Studio 2022(2026)、Windows 11 環境

## プロジェクト概要

- **名称**: AttacheCase ver.5
- **ライセンス**: GPLv3 + 商用ライセンスのデュアル
- **用途**: Windows 向けファイル暗号化ツール
- **状態**: 開発中、UI リフレッシュ進行中
- **公開**: GitHub(透明性・信頼性の担保のため)
- ファイル形式バージョン `DATA_FILE_VERSION = 150`(固定)
- アプリバージョン `5000`(インクリメント)
- v4(形式バージョン `140`)の **復号互換は必須**

## プロジェクト構造

```
AttacheCase5/                        ← プロジェクトルート(Dropbox)
├── CLAUDE.md                        ← このファイル
├── TODO.md                          ← 「記憶の源泉」
├── CLAUDE_PostPPAP_Spec.md          ← 暗号設計の根幹仕様(2026年3月版・保存版)
├── CLAUDE_PostPPAP_Status.md        ← 実装ステータス(随時更新)
├── post-ppap-security-hardening.md  ← サーバセキュリティ強化記録(2026-04-07)
├── AttacheCase/                     ← クライアント本体(C# / WinForms)
└── attachecase-key-server/          ← キーサーバ(Cloudflare Workers / TypeScript)
```

## 技術スタック

- **.NET Framework 4.8 + WinForms**
- C# は **最新言語バージョン** を使用
- IDE: Visual Studio 2022〜2026
- 暗号ライブラリ: **BouncyCastle.Cryptography 2.6.2**(唯一の外部暗号依存)
- バージョン管理: Git + GitKraken(リポジトリは Dropbox 外の実パスに配置)
- キーサーバ: Cloudflare Workers + Hono + TypeScript

### ⚠️ 言語・フレームワーク上の厳禁事項

- C# 言語は最新版でよいが、**`.NET 10` の API を `.NET Framework 4.8` に混ぜないこと**
- 標準ライブラリのクラス／メソッド／オーバーロードを呼ぶ前に、.NET Framework 4.8 で利用可能か必ず確認
- 不確実な場合は質問する／使わない

## コード提示のルール(厳守)

- **部分修正**: 変更前後を明示し、修正箇所の前後コンテキストを添える
- **複雑または広範囲の修正**: 部分提示せず、**関数全体または該当ソース全体** を提示
- **途中省略はしない**: 省略するくらいなら全体を出す(「省略するなら全部省略」)

---

## 2 つの公開鍵暗号機能の区別

両方とも「公開鍵」を使うが**仕組みは完全に別物**。話題にするときは「従来の RSA」「Post-PPAP」と明示すること。

### 従来の RSA

- 前バージョンから存在するレガシー機能
- ユーザー同士が RSA 公開鍵／秘密鍵ファイルを **直接やりとり** して暗号化・復号
- **サーバーを介さない**、ローカル完結の鍵交換
- UI: `panelRsa`、`panelRsaKey`

### Post-PPAP

- AttacheCase5 で新規追加された機能
- メールアドレスと公開鍵を紐付け、**サーバーを介して** データをやりとり
- キーサーバー API を利用
- UI: `panelPostPpapMain`

## 暗号アーキテクチャ(不変条件)

> 詳細は `CLAUDE_PostPPAP_Spec.md` を参照。以下は核となる不変条件のみ。

- 既定アルゴリズム ID: **`0x12`(RSA-4096 + ML-KEM-768 ハイブリッド)**
- RSA-2048(`0x01`)は **復号互換のみ**。新規鍵生成では禁止
- 対称暗号: **AES-256-GCM**
- KDF: **Argon2id**(メモリコスト 64 MB、3 イテレーション、並列度 4)
- 公開鍵暗号: **RSA-OAEP-SHA256**
- ML-KEM は **FIPS 203** 準拠。ハイブリッド構成は **NIST IR 8413** に沿った正しい設計
- **CRC32**(IEEE 802.3、ルックアップテーブル、リトルエンディアン、`byte[]` 返却)は **破損検出専用**。改ざん検出は AES-GCM の認証タグが担当
- SHA-256 フィンガープリント: **先頭 8 バイト(16 進 16 文字)**
- **Post-PPAP モードはパスワードベース暗号化と完全に独立した別系統**。決して混在させない

### BouncyCastle 2.6.2 の作法

- ML-KEM 名前空間: `Crypto.Generators` / `Crypto.Kems` / `Crypto.Parameters` / `Pkcs` / `Pqc.Crypto.Utilities`
  - ❌ `Pqc.Crypto.MLKem` は使わない
- **存在しない API(よくある誤用)**:
  - ❌ `new MLKemPublicKeyParameters(MLKemParameters, byte[])` → ✅ `MLKemPublicKeyParameters.FromEncoding(MLKemParameters, byte[])`
  - ❌ `new MLKemPrivateKeyParameters(MLKemParameters, byte[])` → ✅ `PqcPrivateKeyFactory.CreateKey(pkcs8DerBytes)`
- ML-KEM 公開鍵: `GetEncoded()` で raw bytes 取得 → `FromEncoding()` で復元
- ML-KEM 秘密鍵: **PKCS#8 DER** で save/restore
  - 保存: `PrivateKeyInfoFactory.CreatePrivateKeyInfo(...).GetDerEncoded()`
  - 復元: `PqcPrivateKeyFactory.CreateKey(pkcs8DerBytes)`
  - ❌ `GetEncoded()` での raw 保存は不可
- RSA 鍵: `ExportCspBlob` / `ImportCspBlob`(CSP Blob 形式)

### レガシー復号

- `FileDecrypt4.cs` は v140 形式用に **変更しない**
- `DATA_FILE_VERSION` を見るバージョンディスパッチャが `FileDecrypt4` または `FileDecrypt5` にルーティング

---

## WinForms パネルレイアウトの鉄則(`Form1.cs`)

1. **`Dock=Fill` は `Designer.cs` に直書き**(resx はデザイナー再生成で消える)
2. **`VisibleChanged` では `BeginInvoke` で `LayoutActivePanel()` を呼ぶ**(`Dock=Fill` のサイズ確定後に実行)
3. **パネル切替時は `Visible = true` を最後に設定**(`VisibleChanged` 発火時に他パネルがまだ Visible だと分岐が誤る)
4. **`Anchor` は `Top,Left` に統一**(手動レイアウトと `Anchor` が競合するとチラつき・無限再描画の原因)
5. **`Height`/`Width` を変更してから `Top`/`Left` を計算**(古いサイズ値で位置計算すると初回だけずれる)

## UI リフレッシュ方針

### アイコン

- **黒(#000)シルエットの PNG 1 枚** をベースに、ランタイムで **`ColorMatrix` の第 5 行(offset 加算)** で着色
- RGB 乗算は使わない(黒 × 何でも = 0 になるため)

### タイトルバー

- `DwmSetWindowAttribute`(`DWMWA_CAPTION_COLOR` / `DWMWA_TEXT_COLOR`、Windows 11 Build 22000+)
- Windows 10 はフォールバック

### メニュー

- `ToolStripProfessionalRenderer` + `ProfessionalColorTable` のサブクラスでカスタマイズ

### 半透明

- **ダークモード**: `SetWindowCompositionAttribute` + `ACCENT_ENABLE_ACRYLICBLURBEHIND`
- **ライトモード**: `Form.Opacity`(均一半透明)+ 青みの乳白色背景(純白回避)
  - 理由: GDI はピクセル単位のアルファチャンネルを持てないため、ライトテーマで Acrylic は使えない
- 独自 `TransparentPanel`(`WS_EX_TRANSPARENT`、`ControlStyles` フラグ、`GraphicsPath` の角丸矩形)
- 現状の配色(暫定値)は `TODO.md` の「現状の配色」を参照

### マウスサイドボタン(XButton1)

- `IMessageFilter`(`WM_XBUTTONUP`)でアプリ全体に対応
- 基底フォーム `BackNavigableForm` が登録／解除を自動化
- `FormClosed` での `RemoveMessageFilter` は **必須**(メモリリーク防止)
- 既存の `CancelButton` パターンと統合済み
- 新規フォームは `BackNavigableForm` を継承することで自動的にマウス戻るボタン対応を獲得

---

## ローカライズの留意点

- `MessageBox` のボタン文言(はい/いいえ vs Yes/No)は `Thread.CurrentThread.CurrentUICulture` だけでは制御できない
  - 理由: ボタンラベルは user32.dll のシステムリソースで、.NET の culture 設定では切り替えられない
  - 解決策: CBT フック(`SetWindowsHookEx` ラッパー)またはカスタムダイアログフォーム
- ロケール別 `.resx`(例: `Resources.ja-JP.resx`)には **`ResXFileCodeGenerator` を割り当てない**
  - `Designer.cs` を自動生成するのは既定の `Resources.resx` だけにする

## Dropbox スマートシンク対策

Dropbox スマートシンクが `obj/Debug` ディレクトリを削除してビルドが壊れる問題がある。
**`Directory.Build.props` で中間出力を `%TEMP%\AttacheCase5_build\` にリダイレクト済み**。
**この設定を絶対に削除しないこと。**

---

## 設計原則

- **後方互換は不可侵な制約** として尊重。新形式コードは積極的に近代化してよい(ただし .NET Framework 4.8 の範囲内で)
- **ランタイム変換 > アセット増殖**(テーマ別・状態別の PNG を量産しない。1 枚 + `ColorMatrix`)
- **基底クラスで挙動を伝播**(`BackNavigableForm` 方式で UI 共通機能を一元化)
- 仕様書(`CLAUDE_PostPPAP_Spec.md`、`_doc\データ構造.xlsx`)に従う。実装と仕様の食い違いに気づいたら必ず指摘する
- AES-GCM が改ざん検出を担うので、SHA-256 への置き換えは過剰(CRC32 で破損検出のみが正しい設計)

## コミュニケーション

- 直截な技術判断を返す。曖昧に和らげる必要はない
- 過去判断との矛盾や、仕様と実装のズレを発見したら指摘する
- 技術的制約の **WHY** を説明する(開発者が理由を知りたがるため)
- 言語は日本語

## 参考ファイル

- `CLAUDE_PostPPAP_Spec.md` — Post-PPAP の暗号方式・ファイルフォーマット・設計の根幹仕様(2026年3月版・保存版、変更は慎重に)
- `CLAUDE_PostPPAP_Status.md` — 実装ステータス・サーバ議論用引き継ぎドキュメント
- `post-ppap-security-hardening.md` — サーバセキュリティ強化作業の記録(2026-04-07)
- `_doc\データ構造.xlsx` — ファイルフォーマットのオフセット定義(色分けセクション、ヘッダーバー付き)
- `TODO.md` — 直近の課題と保留事項(**起動時に必ず確認**。「記憶ポリシー」の対象ファイル)
