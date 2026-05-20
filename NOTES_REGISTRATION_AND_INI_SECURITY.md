# レジストレーションコード管理と `_AtcCase.ini` セキュリティ強化に関する設計メモ (AC5 版)

最終更新: 2026-05-13

本ドキュメントは、AC4 で行った同名作業を AC5 にも反映した記録です。AC4 側の同名ファイル(`AttacheCase4/NOTES_REGISTRATION_AND_INI_SECURITY.md`)に背景・設計判断の詳細があるので、本ドキュメントは AC5 固有の差分と、AC5 で「すでに対応済み」だった項目の整理を中心にまとめます。

---

## AC4 → AC5 反映状況

| AC4 で行った変更 | AC5 の現状 | 本作業での対応 |
| --- | --- | --- |
| 旧バージョン(AC3/AC4) → 現バージョン(AC4/AC5)の **設定移行** | `AppSettings.ReadOptionsFromRegistry` で `RegCopyTo(reg4, reg5)` による一括コピー実装済み(`reg5 == null` のときだけ走る) | 不要 |
| `WriteOptionToIniFile` の `securityManager` null 例外 | TODO.md (2026-04-07) で済 | 不要 |
| `IniSecurityManager.MarkAsApproved` の `File.Exists` ガード | TODO.md (2026-04-07) で済 | 不要 |
| `.csproj` のルート重複 `<Compile Include>` 削除 | コメントアウトで除外済み(物理ファイルは残るが csproj 不参照) | 触らない |
| `CustomControl\DelayTextBox.cs` の UTF-8 BOM 化 | 既に UTF-8 BOM | 不要 |
| `LicenseRegister` コンストラクタの空サブキー生成防止 | 未対応(`AttacheCase5\Registration` を無条件 `CreateSubKey`) | **適用** |
| `LicenseRegister.RegistrationCode` getter 追加 | 未対応(setter のみ) | **適用** |
| INI への `RegistrationCode` 書き出し/読み込み | 未対応 | **適用** |
| Form2 のステージング値フォールバック | 未対応 | **適用** |
| `IniSecurityManager` を HMAC-SHA256 + 複数デバイス + `NO_DEVICE_ID` バイパス撤廃 | 未対応(SHA-256 単一デバイス、`NO_DEVICE_ID` バイパスあり) | **適用** |

---

## 1. `LicenseRegister.cs`(AC5)

### 変更点
- `RegistrationCode` プロパティに getter を追加
- コンストラクタの「サブキー未存在時に `CreateSubKey`」フォールバックを撤去
- `Decrypt` 内の同種のフォールバック(`if (_registrationCode == "")` ブロックでの `CreateSubKey`)も撤去
- `DeleteLicense` の `DeleteSubKey("Registration")` を `DeleteSubKey("Registration", false)` に変更(サブキー未存在時の例外回避)

### AC5 固有の注意点
- AC5 では `AppSettings.ReadOptionsFromRegistry` が、`HKCU\Software\Hibara\AttacheCase5` 自体が存在しないことを条件に `RegCopyTo(AttacheCase4, AttacheCase5)` で **AC4 の設定一式を AC5 にコピー** する。`RegCopyTo` は再帰コピーなので、`AttacheCase4\Registration` も含めて移行される。
- 旧コードでは `new LicenseRegister()` が呼ばれた瞬間に空の `AttacheCase5\Registration` を作っていたため、上記の `reg5 == null` 条件が早期に崩れ、AC4 → AC5 の一括移行ロジックが走らなくなるバグがあった(Form2 を一度開いただけで AC5 ライセンスが「未登録」状態のまま固定される)。今回の修正でこのバグが解消される。
- AC4 では別途「AC3 → AC4 の targeted migration + AC3 側の `Registration` サブキー削除」を実装したが、AC5 では既存の `RegCopyTo` がその役割を兼ねるため、ターゲット移行ロジックは追加していない。AC4 のデータは AC5 一括移行後もそのまま残るのが現仕様。

### アカデミックライセンスとの関係
- AC5 固有機能の `SetAcademicLicense` / `GetAcademicLicense` / `GetCommercialLicense` は AC4 には無いが、ここでは触っていない。INI への書き出しは現状 `RegistrationCode` のみ対象としており、`AcademicUse` フラグは未対応(必要に応じて将来追加)。

---

## 2. `AppSettings.cs`(AC5)

### 変更点
- フィールド `_RegistrationCodeString` とプロパティ `RegistrationCodeString` を追加(他項目と同じ「メモリ上ステージング」パターン)
- `ReadIniFile` の 1024 文字長版オーバーロードを追加
- `ReadOptionFromIniFile` 末尾で `[Registration] RegistrationCode` を読み込んで `_RegistrationCodeString` に保持(レジストリには触れない)
- `WriteOptionToIniFile` で:
  - 通常モード時は `new LicenseRegister().RegistrationCode` でレジストリ現在値を再同期
  - 一時設定モード時は `_RegistrationCodeString` をそのまま出力
  - 改行を除去して 1 行に連結
  - `WritePrivateProfileString("Registration", null, null, ...)` で `[Registration]` セクション全体を削除して、孤児行を掃除
  - その後に `RegistrationCode` を書き出し
- `ReadOptionsFromRegistry` 末尾でレジストリの現在値を `_RegistrationCodeString` に同期
- `SaveOptionsToRegistry` 末尾でステージング値とレジストリ現在値を比較し、差分があれば `Decrypt(true)` 経由で適用

### AC5 固有の挙動
- `SettingDataLocation` enum、`SettingSource` プロパティは AC4 と同一構造で AC5 にも存在する。
- AC5 の既存実装には「`WriteOptionToIniFile` 末尾で `securityManager` が null なら `new IniSecurityManager(iniFilePath)` で生成してから `UpdateApprovalIfChanged()` を呼ぶ」というロジックがあり、これは AC4 の null ガードとは異なる方針だが、機能的には等価(エクスポート経路で例外が出ない)。今回はこのロジックを尊重し、Registration の処理だけを差し込んだ。

---

## 3. `Form2.cs`(AC5)

### 変更点
- `Form2_Load` のレジストレーションコードチェックを「レジストリ → INI ステージング」の 2 段フォールバックに変更
  1. `new LicenseRegister().GetCommercialLicense()` でレジストリ検証(従来通り)
  2. 失敗した場合、`AppSettings.Instance.RegistrationCodeString` で再度 `Decrypt(false)` 検証
- 2 段目もレジストリには書き込まない(`Decrypt(false)`)

### AC5 固有の注意点
- AC5 の Form2 はアカデミックライセンス表示分岐を持つ(`else if (lcr.GetAcademicLicense())`)。今回はその分岐の前段にステージングフォールバックを入れた。アカデミックライセンス側はレジストリ専用のままで、INI からの注入には対応していない(必要なら別途検討)。

---

## 4. `IniSecurityManager.cs`(AC5)

### 変更点
- `using System.Collections.Generic;` を追加
- 32 byte の `HmacKey` 定数を追加
- `CalculateFileHash` を `SHA256` → `HMACSHA256(HmacKey)` に差し替え
- `CheckWarning` で `.approved` のフォーマットを `{HMAC}|{deviceID1}|{deviceID2}|...` として解釈、判定順を「ハッシュ → デバイス」の順に変更、`NO_DEVICE_ID` バイパスを撤廃
- `MarkAsApproved` を「既存マーカーから承認済みデバイス一覧を読み出し → 現在のデバイスを追記して書き戻し」に変更(ポータブル USB 運用対応)
- ヘルパー `ReadStoredDeviceIds()` を追加

### 期待される挙動(USB ポータブル運用)
| 行為 | 結果 |
| --- | --- |
| PC#A 初回起動 | `NewFile` 警告 → 承認 → `.approved = [h, A]` |
| PC#A で設定変更して終了 | `UpdateApprovalIfChanged` 経由で `[h2, A]` |
| PC#B に挿して起動 | ハッシュ一致だが B 未登録 → `DifferentDevice` 警告 → 承認 → `[h2, A, B]` |
| PC#A に戻して起動 | ハッシュ一致 + A 登録済み → 無警告 |
| 攻撃者が INI を改ざん(鍵を知らない) | HMAC を再計算できないため、どの PC でも `ModifiedFile` 警告 |
| 知らない PC(PC#C)に挿して起動 | A/B はリストにあるが C は無い → `DifferentDevice` 警告 |

### トレードオフ
承認済みデバイス間の「相互信頼モデル」を採用しているため、ある PC で INI が書き換えられた場合に他の承認済み PC では `ModifiedFile` 警告が出ない。「ポータブル運用の利便性」と「個別 PC での再承認による厳密性」のトレードオフを前者寄りに振った結果。厳密寄りに振りたい場合は、`MarkAsApproved` 内で「既存ハッシュと現在ハッシュが異なるときはデバイス一覧をリセット」する分岐を入れるだけで対応可。

### OSS としての注意点
AC5 も GPL ベースで公開されているため、`HmacKey` のバイト列はソースを読めば誰でも入手可能。攻撃ハードルは「INI ディレクトリに書ける(USB スティック、共有フォルダ等)が、アプリのバイナリは持っていない」程度の攻撃者に対して上げる効果がある。より強固にしたい場合は、ユーザー/マシン固有の値から鍵を導出する設計に置き換える必要があるが、USB ポータブル運用と両立しなくなる点に留意。

### 既存ユーザーへの影響
旧フォーマット(SHA-256 ベース)の `.approved` マーカーは新フォーマット(HMAC ベース)では値が一致しないため、アップデート直後の初回起動で `ModifiedFile` 警告が一度だけ表示される。ユーザーが「はい」を押せば新フォーマットの値で `.approved` が書き直され、次回以降は通常通り無警告で読み込まれる(=実質ワンタイムマイグレーション)。

---

## ⚠️ Git 管理外ファイルの注意

`AttacheCase/LicenseRegister.cs` は **`.gitignore` 対象**(RSA 秘密鍵を含むため)で、本作業で変更を加えてもコミット差分には現れません。Dropbox 経由で他 PC に物理ファイルとして同期されますが、git で追跡されていないことを念頭に置く必要があります。

## 関連ファイル(変更箇所一覧)

- `AttacheCase/LicenseRegister.cs` ⚠️ **gitignore 対象** — `RegistrationCode` getter 追加、コンストラクタの空サブキー生成防止、`Decrypt` 内同種フォールバック撤去、`DeleteLicense` の `DeleteSubKey` フラグ修正
- `AttacheCase/AppSettings.cs` — `_RegistrationCodeString` フィールド/プロパティ、各読み書き経路への Registration 関連処理、`ReadIniFile` maxSize オーバーロード、`[Registration]` セクション再構築
- `AttacheCase/Form2.cs` — ライセンス表示のステージング値フォールバック
- `AttacheCase/IniSecurityManager.cs` — HMAC 化、複数デバイス対応、`NO_DEVICE_ID` バイパス撤廃、判定順反転、`ReadStoredDeviceIds` ヘルパー

---

## 参照

- AC4 側の同名ファイル: `..\AttacheCase4\NOTES_REGISTRATION_AND_INI_SECURITY.md`(背景や代替案の検討経緯はこちらに詳細)
- `TODO.md` の「完了済みだが記録しておきたい判断 / 2026-05-13」セクションに、本作業の要約を追加済み
