# AttacheCase5 - Claude Code Project Instructions

## 開発者プロフィール
- AttacheCase5 のメイン開発者（Mitsuhiro Hibara）
- C# / .NET Framework 4.8 / WinForms
- ファイル暗号化ソフトウェアの開発
- 日本語でコミュニケーション
- UI/UX のデザインにこだわりがあり、色の微調整を重視する
- 技術的な制約についても深く理解しようとする（WHY を知りたがる）
- Visual Studio 2022 (2026) を使用、Windows 11 環境

## 重要な仕様書
- `CLAUDE_PostPPAP_Spec.md`: 暗号方式・ファイルフォーマット・Post-PPAP 設計の根幹仕様。実装時は必ず最初に参照すること
- `CLAUDE_PostPPAP_Status.md`: 実装ステータス・サーバ議論用引き継ぎドキュメント
- デフォルトアルゴリズム: RSA-4096 + ML-KEM-768 ハイブリッド (0x12)
- RSA-2048 の新規生成禁止
- フィンガープリント: SHA-256 先頭 8 バイト（16 hex 文字）
- ML-KEM 秘密鍵保存: PKCS#8 DER 形式（GetEncoded() は不可）

## 2つの公開鍵暗号機能の区別

### 従来のRSA
- 前バージョンから存在するレガシー機能
- ユーザー同士がRSA公開鍵/秘密鍵ファイルを直接やりとりして暗号化・復号する
- サーバーを介さない、ローカル完結の鍵交換方式
- UI: panelRsa, panelRsaKey

### Post-PPAP
- AttacheCase5 で新規追加された機能
- メールアドレスと公開鍵を紐付け、サーバーを介してデータをやりとりする
- キーサーバー API を利用（従来のRSAとはまったく異なるフロー）
- UI: panelPostPpapMain

両方とも「公開鍵」を使うが仕組みは完全に別物。話題にするときは「従来のRSA」「Post-PPAP」と明示すること。

## BouncyCastle 2.6.2 API の注意点
- `new MLKemPublicKeyParameters(MLKemParameters, byte[])` は存在しない → `MLKemPublicKeyParameters.FromEncoding(MLKemParameters, byte[])` を使う
- `new MLKemPrivateKeyParameters(MLKemParameters, byte[])` は存在しない → `PqcPrivateKeyFactory.CreateKey(pkcs8DerBytes)` を使う
- 公開鍵: `GetEncoded()` で raw bytes 取得 → `FromEncoding()` で復元
- 秘密鍵: `PrivateKeyInfoFactory.CreatePrivateKeyInfo().GetDerEncoded()` で PKCS#8 DER 保存 → `PqcPrivateKeyFactory.CreateKey()` で復元

## Dropbox スマートシンク対策
Dropbox スマートシンクが obj/Debug ディレクトリを削除してビルドが壊れる問題がある。
`Directory.Build.props` で中間出力を `%TEMP%\AttacheCase5_build\` にリダイレクト済み。**この設定を削除しないこと。**

## WinForms パネルレイアウトの鉄則（Form1.cs）
1. **Dock=Fill は Designer.cs に直書き**（resx はデザイナー再生成で消える）
2. **VisibleChanged では BeginInvoke で LayoutActivePanel() を呼ぶ**（Dock=Fill のサイズ確定後に実行）
3. **パネル切替時は `Visible = true` を最後に設定**（VisibleChanged 発火時に他パネルがまだ Visible だと分岐が誤る）
4. **Anchor は Top,Left に統一**（手動レイアウトと Anchor が競合するとチラつき・無限再描画の原因）
5. **Height/Width を変更してから Top/Left を計算**（古いサイズ値で位置計算すると初回だけずれる）

## 半透明実装
- **ダークモード**: SetWindowCompositionAttribute + ACCENT_ENABLE_ACRYLICBLURBEHIND
- **ライトモード**: Form.Opacity（均一半透明）+ 青みの乳白色背景（純白回避）
- GDI はピクセル単位のアルファチャンネルを持てないため、ライトテーマで Acrylic は使えない

### ライトテーマ配色
- LightBaseColor: #EEF3F8（フォーム・パネル背景）
- LightSidebarColor: #D8E1ED（サイドバー）
- LightContentColor: #F5F8FC（コンテンツ領域）
- LightHoverColor: #E8EFF5（マウスホバー）
- LightDragColor: #E0F0E4（ドラッグ時）
- LightOpacity: 0.93

## 完了済みの作業

### 2026-03-24
- CRC32 移行: SHA-256 → CRC32 に変更完了
- Dropbox スマートシンク対策: Directory.Build.props で obj リダイレクト
- DelayTextBox デザイナーエラー修正
- タイトルバー色 #070f1b（Windows 11 DWM）
- 半透明効果: ダーク=Acrylic、ライト=Form.Opacity 0.93

### 2026-03-26
- Post-PPAP 登録UI統合、条件付きナビゲーション
- 統合ストレージ accounts.json（DPAPI暗号化秘密鍵ペア）
- WinForms デザイナーエラー修正、アイコン割り当てバグ修正

### 2026-03-27
- zxcvbn パスワード強度メーター実装
- AcceptButton 全撤廃 → 個別 KeyDown ハンドラ + SuppressKeyPress
- Post-PPAP 公開鍵検索 → 暗号化フロー実装
- 公開鍵暗号ファイルの自動復号フロー（TryAutoDecryptWithPrivateKey）
- RSA-4096 + ML-KEM-768 ハイブリッド暗号対応
- panel1-9 → 意味のある名前にリネーム
- サイドバー/ヘッダ色のテーマ統一

### 2026-03-30
- レイアウト処理を Paint → VisibleChanged + Resize に移行
- panelRsa チラつき修正（Anchor 競合が原因）
- Dock=Fill を Designer.cs 直書きに変更（全7パネル）
- VisibleChanged で BeginInvoke パターン適用（全6パネル）
- labelDragAndDrop センタリング修正
- textBoxPassword の Designer.cs 初期化復元

### 2026-04-06
- コマンドライン引数: RSA 暗号化(/pubkey)、復号(/privkey)、鍵生成(/genrsa) 動作確認
- コマンドライン引数: Post-PPAP 暗号化(/p-ppap=email) 実装・動作確認
- AppSettings 引数パーサーバグ修正
- キーサーバ algorithmId / mlkemPublicKey 保存対応（デプロイ済み）
- ML-KEM 公開鍵復元: BC 2.6.2 の FromEncoding() 使用に修正
- Post-PPAP ハイブリッド必須化（RSA-only フォールバック禁止）

### 2026-04-07
- Form3 デザイナーエラー修正: `using System.Windows.Documents;`（WPF名前空間）削除
- カスタムコントロール整理: SplitButton/TaskbarProgress を CustomControl/ に統一、ルート側の重複を csproj から除外
- Form3.Designer.cs / Form4.Designer.cs: `AttacheCase.SplitButton` → `AttacheCase.CustomControl.SplitButton`
- IniSecurityManager.MarkAsApproved(): .approved ファイル未存在時の FileNotFoundException 修正
- WriteOptionToIniFile(): securityManager 未初期化時の NullReferenceException 修正
- 記憶パスワード暗号化の刷新:
  - レジストリ: DPAPI (ProtectedData) に移行（旧方式からの自動フォールバック付き）
  - INI (ver >= 5000): AES-256-GCM + PBKDF2 600,000回 + SHA-256（BouncyCastle GcmBlockCipher）
  - INI (ver < 5000): 旧 AES-256-CBC で復号（後方互換、読み取り専用）
  - 鍵素材に Windows SID を追加

### 2026-04-13
- v5 暗号化ヘッダから `"atc5"` トークン廃止（GCM 認証タグで正誤判定が完結するため不要）
  - FileEncrypt5.cs: `ATC_ENCRYPTED_TOKEN` 定数と 4byte 書き込みを削除
  - FileDecrypt5.cs: `ParseDecryptedHeader()` の `"atc5"` チェックを削除
- コマンドライン `/p=` パスワード指定時に確認画面で止まるバグ修正（`fMemPasswordExe` に依存していた）
- `RoundtripTest.cs` 追加（DEBUG ビルド専用、`--roundtrip-test` 引数で実行）
- `_doc/` データ構造 xlsx を 3 ファイルから 1 ファイル（`データ構造.xlsx`）に統合、トークン記述も削除

## 今後の課題
- データ保持ポリシー（鍵の有効期限、自動削除）
- 秘密鍵のバックアップ・PC移行機能（DPAPI はマシン依存）
- panelPostPpapMain の登録済みアカウント情報表示
- アイコンの配置し直し
- 全体の色調整（ライトテーマ・ダークテーマ両方）
