# アタッシェケース5（クロスプラットフォーム統合版）

Windows / macOS で動く、ファイル・フォルダー暗号化ソフト「アタッシェケース5」です。
.NET 10 + Avalonia UI で、Windows 版（WinForms）と macOS 版の機能を単一のコードベースに統合しています。

> **これはオープンソースソフトウェアではありません。**
> ソースコードは、実装の透明性とセキュリティ検証のために公開しています。
> 業務・商用利用には別途ライセンスが必要です。詳細は [LICENSE.md](LICENSE.md) と
> [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md) をご覧ください。

## 機能

- **パスワード方式の暗号化 / 復号** — AES-256-GCM + Argon2id（AC5 形式 / v150）
- **Post-PPAP** — 鍵サーバ経由で相手の公開鍵を取得し、パスワードを共有せずに送る。
  RSA-4096 と ML-KEM-768 のハイブリッド（耐量子）。秘密鍵は macOS キーチェーン /
  Windows DPAPI に保管
- **旧形式の復号** — v105（AC2）/ v130（AC3）/ v140（AC4）も開けます。
  RSA 鍵ファイル（`.atcpvt`）方式のものも復号できます
- **自己実行形式（.exe）出力** — Windows 専用。受信者はアプリ無しで復号できます
- **パスワード付き ZIP** の作成 / 展開（ZipCrypto / AES-128 / AES-256）
- **コマンドライン** — GUI 内蔵（引数付き起動）と、コンソール専用の
  `AttacheCase.Cli` の両方。書式は旧 Windows 版と互換（`--help` で一覧）
- **商用ライセンス登録**、パスワード強度メーター（zxcvbn）、
  ファイル関連付け（Windows）、ポータブル設定（`_AtcCase.json`）

暗号化して書き出す形式は AC5（v150）のみです。旧形式は読み込み専用です。

## ビルド

.NET 10 SDK が必要です。

```bash
dotnet build          # ビルド（Windows では SFX スタブ・AtcSetup も同梱される）
dotnet test           # 単体テスト
dotnet run --project src/AttacheCase    # GUI を実行
```

回帰テスト（Windows、旧版との互換検証を含む 49 ケース）:

```powershell
powershell -File dogfooding\RegressionTest\run-tests.ps1 -NoGui
```

### 自己実行形式（SFX）スタブの埋め込み（Windows）

`.exe` 出力は、復号スタブ `Exeout.exe` をバイト配列として本体に埋め込む方式です。
埋め込み済みソース `src/AttacheCase.Core/Exeout48.cs` は生成物なので、リポジトリには
空の雛形 `Exeout48.template.cs` しかありません。雛形のままビルドすると
本体は動きますが `.exe` 出力だけが使えません。有効にするには次の順で生成します。

```powershell
# 1. スタブを Release でビルド（net48、Visual Studio の MSBuild）
msbuild src\Exeout\Exeout.csproj -p:Configuration=Release
# 2. 雛形を複製し、ExeToHex でスタブのバイト列を差し込む
copy src\AttacheCase.Core\Exeout48.template.cs src\AttacheCase.Core\Exeout48.cs
tools\ExeToHex\bin\Release\ExeToHex.exe src\AttacheCase.Core\Exeout48.cs src\Exeout\bin\Release\Exeout.exe
# 3. 本体をビルド（Exeout48.cs があればそれが使われる）
dotnet build
```

Windows のインストーラ（Inno Setup 6）、コード署名、公証、自動更新の配信は
ライセンサーの非公開スクリプトで行っており、このリポジトリには含みません。
同様に、商用ライセンスの登録コードを検証する鍵もリポジトリには含みません
（空の雛形 `LicenseRegister.Secret.template.cs` でビルドでき、その場合は常にフリー版として動きます）。

## 構成

| ディレクトリ | 内容 |
|---|---|
| `src/AttacheCase.Core/` | UI 非依存の中核。暗号処理・形式（v105〜v150）・Headless 実行・CLI 解析・設定 |
| `src/AttacheCase/` | GUI（Avalonia / MVVM）。`Platform/Windows|MacOS/` に OS 固有実装 |
| `src/AttacheCase.Cli/` | コンソール専用の実行ファイル（Avalonia を読み込まない） |
| `src/Exeout/` + `src/ExeOutShared/` | 自己実行形式（SFX）の復号スタブ（net48、Windows 専用） |
| `src/AtcSetup/` | 関連付けの昇格ヘルパー（net48、Windows 専用） |
| `tests/AttacheCase.Tests/` | 単体テスト・互換性テスト（xunit）。旧版実物で生成した検体を含む |
| `dogfooding/` | CLI 終了コード駆動の回帰スイート |
| `_installer/` | Windows インストーラ（Inno Setup、非公開・git 追跡外） |

## 互換性の約束

- ATC ファイル形式は旧 Windows 版・旧 macOS 版と相互に読み書きできます
  （v150 書き込み、v105〜v150 読み込み）。形式の正は `data_structure.pdf`
- CLI の終了コード（`AtcExitCode`）は凍結された公開契約です。値は変更しません

## ライセンス

AttacheCase Source Available License Version 1.0

- [LICENSE.md](LICENSE.md) — 利用条件
- [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md) — 業務・商用利用のライセンス区分
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) — 第三者ライブラリの表示

Copyright © 2016-2026 HiBARA Software, LLC.
