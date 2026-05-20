# AttacheCase ver.5 暗号化仕様 — Claude Code 指示書

> このドキュメントは Claude Code が AttacheCase ver.5 を実装・修正する際に**必ず最初に読む**仕様書です。
> 暗号方式、ファイルフォーマット、Post-PPAP 設計の根幹に関わる決定事項をまとめています。

---

## 1. プロジェクト概要

| 項目 | 内容 |
|------|------|
| プロジェクト名 | AttacheCase ver.5 |
| フレームワーク | .NET Framework 4.8 / WinForms |
| 言語 | C# (最新構文 OK) |
| 外部暗号ライブラリ | BouncyCastle.Cryptography v2.6.2 **のみ** |
| ファイルフォーマットバージョン | `150`（データバージョン定数） |
| アプリバージョン定数 | `5000` |

---

## 2. 暗号化モードは「完全に別物」— 混同厳禁

AttacheCase ver.5 には **2つの暗号化モード** があり、それぞれ設計・データ構造・鍵管理がまったく異なります。

```
┌──────────────────────────────────────────────────────┐
│  モード 0x00: パスワード方式                           │
│  - 鍵導出: Argon2id                                  │
│  - 対称暗号: AES-256-GCM                             │
│  - キーサーバ: 不使用                                 │
└──────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────┐
│  モード 0x01: Post-PPAP 公開鍵方式 ← 別物            │
│  - 鍵保護: RSA-4096-OAEP-SHA256                      │
│             + ML-KEM-768（耐量子ハイブリッド）★       │
│  - 対称暗号: AES-256-GCM（共通）                     │
│  - キーサーバ: 使用（鍵登録・検索）                   │
└──────────────────────────────────────────────────────┘
```

**⚠ パスワード方式と公開鍵方式を同一ファイルに混在させてはならない（1ファイル1モード）。**

---

## 3. 公開鍵暗号アルゴリズム識別子（PkaAlgorithmId）

`PkaAlgorithmId` 定数クラスで定義。ファイルヘッダ offset 26 の 1 バイトに格納される。

```csharp
public static class PkaAlgorithmId
{
    public const byte None            = 0x00; // パスワード方式（公開鍵なし）
    public const byte Rsa2048         = 0x01; // RSA-2048 ★後方互換読み込み専用・非推奨
    public const byte Rsa4096         = 0x02; // RSA-4096 単体
    public const byte MlKem768        = 0x10; // ML-KEM-768 単体（将来予約）
    public const byte Rsa2048MlKem768 = 0x11; // RSA-2048 + ML-KEM-768 ハイブリッド
    public const byte Rsa4096MlKem768 = 0x12; // RSA-4096 + ML-KEM-768 ★推奨デフォルト
}
```

### ver.5 での運用ルール（重要）

| 操作 | 使用アルゴリズム | 理由 |
|------|----------------|------|
| **新規鍵生成** | `0x12`（RSA-4096 + ML-KEM-768） | Post-PPAP の売り＝耐量子暗号ハイブリッド |
| **新規暗号化** | `0x12` 固定 | 同上 |
| **復号（受信側）** | `0x01`〜`0x12` すべて対応 | 旧クライアントからのファイルを開けるようにする |
| **RSA-2048 の新規生成** | **禁止** | NIST が 2030 年以降廃止予定 |

```csharp
// 正しい実装
public const byte DefaultAlgorithmId = PkaAlgorithmId.Rsa4096MlKem768; // 0x12

// 間違い — やってはいけない
// public const int RsaKeySize = 2048;  ← 新規生成に 2048 は使わない
```

---

## 4. Post-PPAP とは何か（設計思想）

### 背景: PPAP 問題

日本のビジネス慣行「PPAP」= 暗号化ファイルを送り、別メールでパスワードを送る。
→ 盗聴されればパスワードも傍受される。意味がない。

### Post-PPAP の解決策

受信者の公開鍵でコモンキー（共通鍵）を暗号化して送る。
パスワードを通信路に流さないため PPAP の欠陥を根本解決する。

```
送信者:
  1. ランダムなコモンキー（32 bytes）を生成
  2. 受信者の RSA-4096 公開鍵 + ML-KEM-768 公開鍵でコモンキーを暗号化
  3. 暗号化されたコモンキーをファイルヘッダに埋め込む
  4. コモンキーで本体を AES-256-GCM 暗号化

受信者:
  1. 自分の RSA-4096 秘密鍵 + ML-KEM-768 秘密鍵でコモンキーを復号
  2. コモンキーで本体を復号
  → パスワードのやり取りは一切不要
```

### ハイブリッド方式（RSA + ML-KEM）を採用する理由

```
安全性 = RSA の安全性 AND ML-KEM の安全性
```

- RSA: 実績があるが、量子コンピュータで将来破られる可能性
- ML-KEM: 量子耐性があるが、2024 年標準化されたばかりで実績が浅い
- **両方同時に破られない限りコモンキーは守られる**

NIST IR 8413 も移行期間中のハイブリッド運用を推奨している。
これは言い訳でなく、標準化機関公式の推奨事項である。

---

## 5. キーサーバに格納する内容（ver.5 vs 旧設計）

### 旧設計（RSA のみ）

```json
{
  "email": "user@example.com",
  "modulus": "<RSA公開鍵モジュラス Base64>",
  "exponent": "<公開指数 Base64>",
  "fingerprint": "<SHA-256先頭8バイト hex>"
}
```

### ver.5 新設計（ハイブリッド対応）

```json
{
  "email": "user@example.com",
  "modulus": "<RSA-4096公開鍵モジュラス Base64>",
  "exponent": "<公開指数 Base64>",
  "fingerprint": "<SHA-256(RSAモジュラス || ML-KEM公開鍵) 先頭8バイト hex>",
  "algorithmId": 18,
  "mlkemPublicKey": "<ML-KEM-768公開鍵 Base64（1184 bytes）>"
}
```

**⚠ キーサーバ API も `algorithmId` と `mlkemPublicKey` を受け入れ・返却できるよう更新が必要。**
旧 API が未対応の間は、`LookupPublicKeyAsync` は `algorithmId = 0x02`（RSA-4096）にフォールバックする。

---

## 6. フィンガープリント生成規則

フィンガープリント = 受信者識別子。ファイル内にメールアドレスの平文は格納しない。

```csharp
// RSA 単体 (0x01, 0x02)
fingerprint = SHA-256(RSA modulus)[0..7] → 16文字の hex

// ハイブリッド (0x11, 0x12) ← ver.5 デフォルト
fingerprint = SHA-256(RSA modulus || ML-KEM public key)[0..7] → 16文字の hex
```

**⚠ 旧実装のバグ: 先頭 16 バイト（32 文字）を使っていた。正しくは先頭 8 バイト（16 文字）。**

---

## 7. 秘密鍵の保存・復元（BouncyCastle 固有の注意点）

### RSA 秘密鍵（.NET 標準の CSP Blob 形式）

```csharp
// 保存（生成直後）
byte[] blob = rsa.ExportCspBlob(true);
byte[] encrypted = ProtectedData.Protect(blob, null, DataProtectionScope.CurrentUser);
account.EncryptedPrivateKey = Convert.ToBase64String(encrypted);
Array.Clear(blob, 0, blob.Length);

// 復元
byte[] encrypted = Convert.FromBase64String(account.EncryptedPrivateKey);
byte[] blob = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
var rsa = new RSACryptoServiceProvider();
rsa.PersistKeyInCsp = false;
rsa.ImportCspBlob(blob);
Array.Clear(blob, 0, blob.Length);
```

### ML-KEM-768 秘密鍵（PKCS#8 DER 形式 — CSP Blob は使えない）

```csharp
// 保存（生成直後）
// ⚠ GetEncoded() は生バイトで不完全。必ず PKCS#8 DER 形式を使うこと
byte[] der = PrivateKeyInfoFactory.CreatePrivateKeyInfo(mlkemPriv).GetDerEncoded();
byte[] encrypted = ProtectedData.Protect(der, null, DataProtectionScope.CurrentUser);
account.MlKemEncryptedPrivateKey = Convert.ToBase64String(encrypted);
Array.Clear(der, 0, der.Length);

// 復元
byte[] encrypted = Convert.FromBase64String(account.MlKemEncryptedPrivateKey);
byte[] der = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
var mlkemPriv = (MLKemPrivateKeyParameters)PqcPrivateKeyFactory.CreateKey(der);
Array.Clear(der, 0, der.Length);
```

| 鍵の種類 | 保存形式 | 復元方法 |
|---------|---------|---------|
| RSA | CSP Blob (`ExportCspBlob`) | `ImportCspBlob` |
| ML-KEM-768 | PKCS#8 DER (`PrivateKeyInfoFactory`) | `PqcPrivateKeyFactory.CreateKey` |

**`new MLKemPrivateKeyParameters(params, bytes)` というコンストラクターは存在しない。絶対に使わないこと。**

---

## 8. 名前空間（using）の正しいセット

```csharp
// ML-KEM を使う場合に必要な using
using Org.BouncyCastle.Crypto.Generators;       // MLKemKeyPairGenerator
using Org.BouncyCastle.Crypto.Kems;             // MLKemKemGenerator, MLKemKemExtractor
using Org.BouncyCastle.Crypto.Parameters;       // MLKemParameters, MLKemKeyGenerationParameters
                                                //   MLKemPublicKeyParameters, MLKemPrivateKeyParameters
using Org.BouncyCastle.Pkcs;                    // PrivateKeyInfoFactory
using Org.BouncyCastle.Pqc.Crypto.Utilities;   // PqcPrivateKeyFactory
using Org.BouncyCastle.Security;                // SecureRandom
```

**⚠ `Org.BouncyCastle.Pqc.Crypto.MLKem` という名前空間は C# には存在しない（Java の名前空間）。**

### MLKemParameters 定数名（大文字小文字に注意）

```csharp
// 正しい（小文字・アンダースコア区切り）
MLKemParameters.ml_kem_512
MLKemParameters.ml_kem_768   // ← ver.5 で使用
MLKemParameters.ml_kem_1024

// 間違い（Java 風の命名。C# では存在しない）
// MLKemParameters.ML_KEM_768  ← コンパイルエラー
```

---

## 9. 鍵サイズ・暗号文サイズ一覧（参考）

| アルゴリズム | 公開鍵 | 秘密鍵 | 暗号文 |
|------------|-------|-------|-------|
| RSA-2048 | 256 bytes | 〜1192 bytes | 256 bytes |
| RSA-4096 | 512 bytes | 〜2350 bytes | 512 bytes |
| ML-KEM-768 | 1184 bytes | 2400 bytes | 1088 bytes |

### ファイルヘッダ内の鍵エントリサイズ（S フィールド）

```
0x01  RSA-2048:               S = 256 bytes
0x02  RSA-4096:               S = 512 bytes
0x11  RSA-2048 + ML-KEM-768:  S = 256 + 1088 = 1344 bytes
0x12  RSA-4096 + ML-KEM-768:  S = 512 + 1088 = 1600 bytes  ★デフォルト
```

---

## 10. ファイルバイナリ構造（ver.5 / データバージョン 150）

### 共通平文ヘッダ（offset 0〜35、両モード共通）

| offset | size | 内容 |
|--------|------|------|
| 0 | 2 | アプリバージョン（`5000`） |
| 2 | 1 | ミスタイプ回数制限 |
| 3 | 1 | ファイル破壊フラグ |
| 4 | 16 | シグニチャ `"_AttacheCaseData"` 固定 |
| 20 | 4 | データバージョン = `150` |
| 24 | 1 | 暗号化モード識別子（`0x00` or `0x01`） |
| 25 | 1 | 対称暗号アルゴリズム（`0x01` = AES-256-GCM） |
| 26 | 1 | 公開鍵暗号アルゴリズム識別子（PkaAlgorithmId） |
| 27 | 1 | KDF アルゴリズム識別子（`0x00` = なし / `0x01` = Argon2id） |
| 28 | 4 | 予約領域（0x00 パディング） |
| 32 | 4 | 暗号化ヘッダデータサイズ |

### 鍵保護ブロック（公開鍵モード、offset 36〜）

```
[2 bytes] 鍵ブロック数 N
× N 繰り返し:
  [8 bytes] フィンガープリント（受信者識別子）
  [2 bytes] 暗号化コモンキーのサイズ S
  [S bytes] 暗号化コモンキー
```

### 暗号化本体（両モード共通）

```
[12 bytes] Nonce（ヘッダ用）
[可変]     AES-256-GCM 暗号化ヘッダ
[16 bytes] GCM 認証タグ（ヘッダ用）
[12 bytes] Nonce（本体用）
[可変]     AES-256-GCM + DeflateStream 暗号化本体
[16 bytes] GCM 認証タグ（本体用）
```

**ヘッダ用と本体用の Nonce は必ず別々に生成すること（同じ Nonce を使い回さない）。**

---

## 11. 後方互換（v4 / v3 ファイルの読み込み）

| データバージョン | 対応 |
|--------------|------|
| 105 (ver.2) | 読み込み対応（任意） |
| 130 (ver.3) | 読み込み対応 |
| 140 (ver.4) | 読み込み対応 |
| 150 (ver.5) | 読み書き対応 |

ver.4（データバージョン 140）では RSA モードの識別にシグニチャ文字列 `"_AttacheCase_Rsa"` を使っていた。
ver.5 ではシグニチャは `"_AttacheCaseData"` に統一し、モード識別は offset 24 のモードバイトで行う。

**復号時の分岐（FileDecrypt.cs）は必ずデータバージョンで分岐させること。**

```csharp
switch (dataVersion)
{
    case 130: DecryptV3(...); break;
    case 140: DecryptV4(...); break;
    case 150: DecryptV5(...); break;  // パスワード / 公開鍵はモードバイトでさらに分岐
    default:  throw new NotSupportedException(...);
}
```

---

## 12. よくある間違いと禁止事項

### ❌ やってはいけないこと

```csharp
// 1. RSA-2048 で新規鍵生成
new RSACryptoServiceProvider(2048);  // ← ver.5 では禁止。4096 のみ

// 2. ML-KEM 秘密鍵を GetEncoded() で保存
mlkemPriv.GetEncoded();  // ← 不完全。PKCS#8 DER を使うこと

// 3. ML-KEM 秘密鍵を byte[] から直接復元
new MLKemPrivateKeyParameters(MLKemParameters.ml_kem_768, blob);  // ← コンストラクター存在しない

// 4-b. ML-KEM 公開鍵を new で復元しようとする
new MLKemPublicKeyParameters(MLKemParameters.ml_kem_768, bytes);  // ← コンストラクター存在しない
// → 正しくは MLKemPublicKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, bytes)

// 4. 存在しない名前空間を using
using Org.BouncyCastle.Pqc.Crypto.MLKem;  // ← C# には存在しない

// 5. パスワードモードと公開鍵モードの混在
// → 1ファイルは必ず1モード

// 6. ヘッダ用と本体用で同じ Nonce を使い回す
// → GCM では Nonce の再利用は致命的な脆弱性

// 7. IV/Nonce を固定値や決定論的な値で生成
// → 必ず CSPRNG（SecureRandom）で生成すること

// 8. フィンガープリントを 32 文字（16 バイト）で生成
// → 正しくは 16 文字（8 バイト）
```

### ✅ 正しいパターン

```csharp
// RSA 鍵生成は 4096 固定
new RSACryptoServiceProvider(4096);

// ML-KEM 秘密鍵の保存
PrivateKeyInfoFactory.CreatePrivateKeyInfo(mlkemPriv).GetDerEncoded();

// ML-KEM 秘密鍵の復元
(MLKemPrivateKeyParameters)PqcPrivateKeyFactory.CreateKey(derBytes);

// フィンガープリント（8 バイト = 16 文字）
for (var i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));

// Nonce は毎回独立して生成
var nonceHeader = new byte[12];
var nonceBody   = new byte[12];
new SecureRandom().NextBytes(nonceHeader);
new SecureRandom().NextBytes(nonceBody);
```

---

## 13. PostPpapKeyInfo クラスの必須フィールド

`PostPpapKeyInfo.cs` には以下のフィールドがすべて存在すること：

```csharp
public string Email                    { get; set; }
public string EmailHash                { get; set; }  // SHA-256(正規化email) 全64文字
public string Modulus                  { get; set; }  // RSA公開鍵モジュラス Base64
public string Exponent                 { get; set; }  // RSA公開指数 Base64
public string Fingerprint              { get; set; }  // 16文字 hex（8バイト）
public byte   AlgorithmId              { get; set; }  // PkaAlgorithmId 定数値
public string MlKemPublicKey           { get; set; }  // ML-KEM-768公開鍵 Base64（ハイブリッド時）
public string EncryptedPrivateKey      { get; set; }  // DPAPI暗号化済み RSA秘密鍵 CSP Blob
public string MlKemEncryptedPrivateKey { get; set; }  // DPAPI暗号化済み ML-KEM秘密鍵 PKCS#8 DER
public DateTime RegisteredAt           { get; set; }
```

`MlKemPublicKey` と `MlKemEncryptedPrivateKey` はハイブリッドモード (`0x11`, `0x12`) のみ使用。
`LookupPublicKeyAsync` で取得した他者の鍵には `MlKemEncryptedPrivateKey` は含まれない（null）。

---

## 14. キーサーバ API エンドポイント

```
POST /api/register/initiate   鍵登録開始（algorithmId, mlkemPublicKey を含む）
GET  /api/register/status/:token  メール確認ポーリング
GET  /api/keys/:emailHash     公開鍵検索（algorithmId, mlkemPublicKey を返す）
```

キーサーバ側が `algorithmId` / `mlkemPublicKey` に未対応の場合でも、
クライアント側は `algorithmId = 0x02`（RSA-4096）にフォールバックして動作を継続すること。

---

## 15. 主要ソースファイルと責務

| ファイル | 責務 |
|---------|------|
| `PkaAlgorithmId.cs` | アルゴリズム識別子定数 |
| `PostPpapKeyInfo.cs` | 鍵情報モデル |
| `PostPpapManager.cs` | 鍵生成・登録・検索・DPAPI保存 |
| `CryptoHelper5.cs` | BouncyCastle ラッパー（AES-GCM, Argon2id, RSA-OAEP, ML-KEM） |
| `FileEncrypt5.cs` | ver.5 暗号化エンジン（バイナリフォーマット書き込み） |
| `FileDecrypt5.cs` | ver.5 復号エンジン（バイナリフォーマット読み込み・後方互換） |

---

*最終更新: 2026年3月 / AttacheCase ver.5 仕様確定版*
