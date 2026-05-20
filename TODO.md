# AttacheCase v5 — 課題と保留事項

最終更新: 2026-05-19

このファイルは「**記憶の源泉**」です。詳細な運用ルールは `CLAUDE.md` の「🧠 記憶ポリシー」を参照。

---

## 進行中

- [ ] **UI リフレッシュ**(ライト／ダーク切り替え、ホバー／選択状態)
- [ ] **MessageBox 多言語対応** の方式最終決定(CBT フック vs カスタムダイアログ)
- [ ] **panelPostPpapMain** に登録済みアカウント情報を表示
- [ ] **アイコンの配置し直し**
- [ ] **全体の色調整**(ライトテーマ・ダークテーマ両方)
- [ ] **鍵削除 UI の実装**
  - 文脈: サーバ側 `DELETE /api/keys/:emailHash/:fingerprint` は実装済み(`post-ppap-security-hardening.md` §4)
  - 内容: クライアント側のデバイス一覧画面・削除ボタン。「上級者向け設定の奥」に配置予定

## 検討待ち / 次バージョンまでに決める

- [ ] **データ保持ポリシー**(鍵の有効期限、自動削除のタイミング)
  - 文脈: `lastSeen` 自動更新は実装済み。具体的な TTL 値とユーザー通知方法が未策定
  - 内容: 1年? 2年? ユーザー通知メールは出すか
- [ ] **秘密鍵のバックアップ・PC 移行機能**
  - 文脈: DPAPI はマシン依存のため、別 PC で復号できない問題
  - 内容: エクスポート／インポート機構の設計(パスワード保護付き PKCS#12 等)、復元時の安全性確保
- [ ] **(2026-05-11) AES-GCM の AAD に平文ヘッダを含めるかの設計判断**
  - 文脈: `FileEncrypt5.cs` / `FileDecrypt5.cs`。現状は AAD を使用していない
  - 内容: 平文ヘッダ(モードバイト、`PkaAlgorithmId` 等)が認証範囲外。攻撃者が書き換えても GCM タグは検出しない可能性。意図的な判断か、見直すべきかを確定する
- [ ] **(2026-05-13) INI 一時設定モードでの商用ライセンス削除挙動**
  - 文脈: `Form2.ToolStripMenuItemDeleteLicense_Click` は `LicenseRegister.DeleteLicense()` でレジストリのみを対象にしている
  - 内容: INI モードで開いている時に「削除」が押された場合、`AppSettings.Instance.RegistrationCodeString` (ステージング値) も併せてクリアし、次の INI 保存で `[Registration]` セクションも空書きする扱いにするか、それとも UI を無効化するか、を決める
- [ ] **(2026-05-13) アカデミックライセンス(`AcademicUse`)の INI 経由入出力**
  - 文脈: `LicenseRegister.SetAcademicLicense()` / `GetAcademicLicense()` は AC5 固有機能だが、現状 `_AtcCase.ini` への保存・読み込み対象は `RegistrationCode` のみ
  - 内容: 商用ライセンスと同じく `[Registration] AcademicUse=1` のような形で INI に出力するかを判断
- [ ] **(2026-05-13) `.approved` マーカーの鍵導出方式の見直し**
  - 文脈: 現状の `IniSecurityManager.HmacKey` はバイナリ埋め込みの固定値。OSS のためソース閲覧で抽出可能
  - 内容: より強固にしたい場合は (a) `MachineGuid` 等から導出 — ポータブル運用と両立しない / (b) RSA 署名方式 — INI 発行者が限定される、のトレードオフを評価して採用可否を判断
- [ ] **(2026-05-19) `Program.cs` への `AttachConsole` 追加(任意)**
  - 文脈: `--roundtrip-test` 等の DEBUG CLI 機能で `Console.WriteLine` 出力が WinExe のため見えない問題
  - 内容: `Program.cs` の `--roundtrip-test` ブロックで `AttachConsole(ATTACH_PARENT_PROCESS)` を呼ぶ小修正。今回は終了コードで合否判定して済ませたが、次回テスト時に見栄えが欲しければ実施

(以降、会話の流れで出た保留事項を書き溜めていく)

---

## 現状の配色(ライトテーマ・暫定)

UI リフレッシュ完了まで暫定の数値。**確定したら `CLAUDE.md` 側に昇格させること**。

| 用途 | 値 |
|---|---|
| `LightBaseColor`(フォーム・パネル背景) | `#EEF3F8` |
| `LightSidebarColor`(サイドバー) | `#D8E1ED` |
| `LightContentColor`(コンテンツ領域) | `#F5F8FC` |
| `LightHoverColor`(マウスホバー) | `#E8EFF5` |
| `LightDragColor`(ドラッグ時) | `#E0F0E4` |
| `LightOpacity` | `0.93` |
| タイトルバー(ダーク) | `#070f1b` |

---

## 完了済みだが記録しておきたい判断

### 2026-05-19

- **v5 暗号化・復号の大容量対応(ストリームパイプライン化)**
  - 症状: 旧実装は `FileEncrypt5.cs` で圧縮データを `MemoryStream` に全蓄積してから AES-GCM に渡す構造。`MemoryStream` の内部 `byte[]` が `int.MaxValue`(約 2 GiB)を超えると `IOException("Stream was too long.")`(日本語表示「ストリームが長すぎます」)で失敗。コメント「GCM はストリーム全体を処理する必要があるため」は誤解で、`AesGcmEncryptStream` 自身は `ProcessBytes` + `DoFinal` で本来ストリーム処理可能
  - 実機確認: 旧版で 3 GB ファイル暗号化 → 「ストリームが長すぎます」エラーで再現確認
  - 修正:
    - `CryptoStream5.cs` 新規追加(`GcmEncryptingStream` / `GcmDecryptingStream`)。BouncyCastle の `GcmBlockCipher` を `Stream` インターフェイスでラップし、Write/Read 毎に `ProcessBytes`、Dispose または `DrainAndVerify()` で `DoFinal` 実行
    - `FileEncrypt5.cs` 本体処理を `FileStream(outfs) ← GcmEncryptingStream ← DeflateStream ← 入力ファイル` の 3 段ストリームパイプライン化
    - `FileDecrypt5.cs` 本体処理を `FileStream(fs) → GcmDecryptingStream → DeflateStream → ExtractFiles` の 3 段ストリームパイプライン化
    - `AttacheCase.csproj` に `CryptoStream5.cs` を追加
  - 検証:
    - `--roundtrip-test`(小サイズ) → Exit=0、SHA-1 一致、誤パスワード正しく拒否
    - 新版で 3 GB 暗号化→復号 → SHA-1 一致
    - 旧版↔新版 相互運用(2 GiB 以下) → 双方向で復号 OK(フォーマット完全互換)
  - フォーマット互換: 変更なし(`DATA_FILE_VERSION = 150` のまま、出力バイト列は理論上完全同一)
  - 設計上のポイント:
    - `using` の入れ子順序により `DeflateStream` が先に Dispose されて圧縮終端マーカーが flush、その後 `GcmEncryptingStream.Dispose` で 16 byte 認証タグが書き出される
    - 復号側は `DeflateStream` が deflate 終端で読み取りを止めるため、`GcmDecryptingStream.DrainAndVerify()` を明示的に呼んで GCM タグ検証を保証する
    - `leaveOpen: true` を各層に指定して基底ストリームの早期クローズを防ぐ
  - 影響: バックアップ用途で TB クラスのファイルが定数メモリ(100 MB 程度)で扱えるようになる。これまで現実的に使えなかったユースケースが可能に

### 2026-05-13

- **レジストレーションコード管理と `_AtcCase.ini` セキュリティ強化を AC4 から AC5 へ反映**(詳細は `NOTES_REGISTRATION_AND_INI_SECURITY.md`)
  - `LicenseRegister.cs`: コンストラクタの「サブキー未存在時に `CreateSubKey`」フォールバックを撤去(これが INI 一時設定モードでのレジストリ汚染と、`RegCopyTo` による AC4 → AC5 一括移行のトリガー失敗の原因だった)。`RegistrationCode` プロパティに getter 追加。`DeleteLicense` の `DeleteSubKey` を例外を出さない形式に変更
  - `AppSettings.cs`: `_RegistrationCodeString` ステージングフィールドを追加し、`_AtcCase.ini` 経由でのレジストレーションコード入出力を実装。INI への書き出しは改行除去 + `[Registration]` セクション全削除→再作成で旧 INI の孤児行も掃除。レジストリへの反映は「一時的な設定を現在の設定に置き換える」操作経由(`SaveOptionsToRegistry`)でのみ発生する。`ReadIniFile` に maxSize 指定オーバーロード追加(512 文字超のコード対応)
  - `Form2.cs`: `Form2_Load` のライセンス表示を「レジストリ → INI ステージング」の 2 段フォールバックに変更。INI 一時設定モードでもレジストリを汚染せずに商用ライセンスを表示できる
  - `IniSecurityManager.cs`: `.approved` マーカーを SHA-256 → **HMAC-SHA256(32 byte バイナリ埋め込み鍵)** に変更し、公開情報のみでの偽造を不可に。`NO_DEVICE_ID` バイパスを撤廃。フォーマットを `{HMAC}|{deviceID1}|{deviceID2}|...` に拡張し、**承認済みデバイスを複数保持**(USB ポータブル運用で PC を行き来しても初回承認 1 回だけで以降は無警告)。判定順を「ハッシュ → デバイス」に反転(より重要な内容改ざんを優先報告)。`ReadStoredDeviceIds()` ヘルパー追加
  - トレードオフ: 承認済みデバイス間は相互信頼モデル。ある PC で INI を書き換えても他承認済み PC では警告が出ない。OSS のため HMAC 鍵はソースから抽出可能(攻撃ハードルを一段上げる UX 強化として位置付け)
  - 既存ユーザーへの影響: 旧フォーマット `.approved` は無効化されるため初回起動で 1 回だけ `ModifiedFile` 警告 → 承認で自動マイグレーション

### 2026-05-11

- **ドキュメント運用ルールを確立**
  - `CLAUDE.md` を完了履歴と進捗情報から分離し、恒久的な設計判断と技術ノウハウのみに整理
  - `TODO.md` を新設し「記憶の源泉」と位置付け
  - `CLAUDE.md` に「🧠 記憶ポリシー」セクションを追加、「記憶しておきます」類の発言時に Claude が自動で `TODO.md` を編集するルールを明文化
- **仕様書をプロジェクトルートに集約**
  - `attachecase-key-server/doc/` 配下にあった `CLAUDE_PostPPAP_Spec.md`、`CLAUDE_PostPPAP_Status.md`、`post-ppap-security-hardening.md` を `AttacheCase5/` 直下に移動
  - クライアントとサーバ両方のドキュメントを単一の場所で管理
  - クライアント側で Claude Code が実装から逆引きして再生成していた `Spec.md` / `Status.md` は不要となり破棄(本物の `Spec.md` は 2026年3月版の保存版、`Status.md` は本物に最新化を統合)

### 2026-04-13

- **v5 暗号化ヘッダから `"atc5"` トークン廃止**
  - 理由: GCM 認証タグで正誤判定が完結するため不要
  - `FileEncrypt5.cs`: `ATC_ENCRYPTED_TOKEN` 定数と 4byte 書き込みを削除
  - `FileDecrypt5.cs`: `ParseDecryptedHeader()` の `"atc5"` チェックを削除
- コマンドライン `/p=` パスワード指定時に確認画面で止まるバグ修正(`fMemPasswordExe` 依存の解消)
- `RoundtripTest.cs` 追加(DEBUG ビルド専用、`--roundtrip-test` 引数で実行)
- `_doc/` データ構造 xlsx を 3 ファイルから 1 ファイル(`データ構造.xlsx`)に統合、トークン記述も削除

### 2026-04-07

- **サーバセキュリティ強化作業を実施**(詳細は `post-ppap-security-hardening.md`)
  - 3 段階レート制限(メール 10分 / 日次 50通 / IP 1時間 5回)
  - 全フィールドの入力バリデーション(fingerprint、modulus、exponent、algorithmId、mlkemPublicKey)
  - 検索 API の 1 秒遅延(列挙攻撃 / タイミング攻撃対策)
  - `deviceToken` による認証導入(他者の `lastSeen` 改竄防止)
  - `lastSeen` 自動更新(検索 API 統合、24時間に1回まで)
  - 独立ハートビート API(副経路として保持)
  - 鍵削除 API(`DELETE /api/keys/:emailHash/:fingerprint`、同一アカウントの `deviceToken` で認証)
- Form3 デザイナーエラー修正: `using System.Windows.Documents;`(WPF 名前空間)削除
- カスタムコントロール整理: `SplitButton` / `TaskbarProgress` を `CustomControl/` に統一、ルート側の重複を csproj から除外
- `Form3.Designer.cs` / `Form4.Designer.cs`: `AttacheCase.SplitButton` → `AttacheCase.CustomControl.SplitButton`
- `IniSecurityManager.MarkAsApproved()`: `.approved` ファイル未存在時の `FileNotFoundException` 修正
- `WriteOptionToIniFile()`: `securityManager` 未初期化時の `NullReferenceException` 修正
- **記憶パスワード暗号化の刷新**:
  - レジストリ: DPAPI(`ProtectedData`)に移行(旧方式からの自動フォールバック付き)
  - INI (ver >= 5000): AES-256-GCM + PBKDF2 600,000 回 + SHA-256(BouncyCastle `GcmBlockCipher`)
  - INI (ver < 5000): 旧 AES-256-CBC で復号(後方互換、読み取り専用)
  - 鍵素材に Windows SID を追加

### 2026-04-06

- コマンドライン引数: RSA 暗号化(`/pubkey`)、復号(`/privkey`)、鍵生成(`/genrsa`) 動作確認
- コマンドライン引数: Post-PPAP 暗号化(`/p-ppap=email`) 実装・動作確認
- `AppSettings` 引数パーサーバグ修正
- キーサーバ `algorithmId` / `mlkemPublicKey` 保存対応(デプロイ済み)
- ML-KEM 公開鍵復元: BC 2.6.2 の `FromEncoding()` 使用に修正
- **Post-PPAP ハイブリッド必須化**(RSA-only フォールバック禁止)

### 2026-03-30

- レイアウト処理を `Paint` → `VisibleChanged` + `Resize` に移行
- `panelRsa` チラつき修正(`Anchor` 競合が原因)
- `Dock=Fill` を `Designer.cs` 直書きに変更(全 7 パネル)
- `VisibleChanged` で `BeginInvoke` パターン適用(全 6 パネル)
- `labelDragAndDrop` センタリング修正
- `textBoxPassword` の `Designer.cs` 初期化復元

### 2026-03-27

- zxcvbn パスワード強度メーター実装
- `AcceptButton` 全撤廃 → 個別 `KeyDown` ハンドラ + `SuppressKeyPress`
- Post-PPAP 公開鍵検索 → 暗号化フロー実装
- 公開鍵暗号ファイルの自動復号フロー(`TryAutoDecryptWithPrivateKey`)
- RSA-4096 + ML-KEM-768 ハイブリッド暗号対応
- `panel1`〜`panel9` → 意味のある名前にリネーム
- サイドバー／ヘッダ色のテーマ統一

### 2026-03-26

- Post-PPAP 登録 UI 統合、条件付きナビゲーション
- 統合ストレージ `accounts.json`(DPAPI 暗号化秘密鍵ペア)
- WinForms デザイナーエラー修正、アイコン割り当てバグ修正

### 2026-03-24

- **CRC32 移行**: SHA-256 → CRC32 に変更完了
  - 理由: AES-GCM 認証タグが改ざん検出を担うので、ファイル単位ハッシュは破損検出専用でよい
- Dropbox スマートシンク対策: `Directory.Build.props` で `obj` リダイレクト
- `DelayTextBox` デザイナーエラー修正
- タイトルバー色 `#070f1b`(Windows 11 DWM)
- 半透明効果: ダーク=Acrylic、ライト=`Form.Opacity` 0.93

---

## 運用メモ

- 「これは覚えておきたい」と思ったら、`CLAUDE.md` の「🧠 記憶ポリシー」に従って Claude が自動的にここへ書き出す
  - もし Claude が書き出しを忘れたら **「TODO.md は?」** と一声かければ即座に追記される
- このファイルは Dropbox 経由で全 PC(自宅／シェアオフィス)に自動同期される
- 完了したタスクは「完了済みだが記録しておきたい判断」へ移動、または根拠だけ残して削除
- 古くなった項目は遠慮なく整理する
- 暫定値(現状の配色など)が確定したら、`CLAUDE.md` に昇格させる
