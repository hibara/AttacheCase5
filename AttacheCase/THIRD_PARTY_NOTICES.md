# Third-Party Notices / 第三者ライセンス表示

## 日本語

本ソフトウェアは第三者のソフトウェア/ライブラリを利用しています。
第三者部分には各ライセンスが適用され、本プロジェクトのGPLや商用ライセンスで上書きされません。

### NuGet パッケージ

| コンポーネント | バージョン | ライセンス | 用途 |
|---|---|---|---|
| [BouncyCastle.Cryptography](https://github.com/bcgit/bc-csharp) | 2.6.2 | MIT | RSA・ML-KEM 暗号処理 |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | 13.0.4 | MIT | JSON シリアライズ |
| [Costura.Fody](https://github.com/Fody/Costura) | 6.0.0 | MIT | DLL 埋め込み（ビルド時） |
| [Fody](https://github.com/Fody/Fody) | 6.9.3 | MIT | IL ウィーバー基盤（ビルド時） |
| [Resource.Embedder](https://github.com/Wikipedia/Resource.Embedder) | 2.2.0 | MIT | リソース埋め込み（ビルド時） |

### NuGet 推移的依存

| コンポーネント | ライセンス | 備考 |
|---|---|---|
| [Mono.Cecil](https://github.com/jbevain/cecil) | MIT | Fody の内部依存 |
| [System.ValueTuple](https://www.nuget.org/packages/System.ValueTuple/) | MIT | .NET Framework 4.x 互換 |

### 同梱ソースコード

| コンポーネント | ライセンス | ファイル |
|---|---|---|
| [DotNetZip (Zlib)](https://github.com/DinoChiesa/DotNetZip) | Microsoft Public License (Ms-PL) | `Zlib/*.cs`, `CommonSrc/CRC32.cs` |
| [jzlib](https://github.com/ymnk/jzlib) | BSD-style | Zlib の一部（`LICENSE.jzlib.txt` 参照） |
| [zlib](https://zlib.net/) | zlib License | Zlib の一部（`License.zlib.txt` 参照） |
| CRC16 (Kadzus) | パブリック（ブログ掲載コード） | `CRC16.cs` |

### ローカルライブラリ

| コンポーネント | ライセンス | 用途 |
|---|---|---|
| [zxcvbn-cs](https://github.com/trichards57/zxcvbn-cs) | MIT | パスワード強度推定 |

※ビルド環境・ターゲットによって依存関係が増減することがあります。
最終的なライセンス遵守は利用者側で確認してください。

---

## English

This software includes or depends on the following third-party components.
Third-party components remain under their respective licenses and are not overridden by this project's GPL or commercial license.

### NuGet Packages

| Component | Version | License | Purpose |
|---|---|---|---|
| [BouncyCastle.Cryptography](https://github.com/bcgit/bc-csharp) | 2.6.2 | MIT | RSA & ML-KEM cryptography |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | 13.0.4 | MIT | JSON serialization |
| [Costura.Fody](https://github.com/Fody/Costura) | 6.0.0 | MIT | DLL embedding (build-time) |
| [Fody](https://github.com/Fody/Fody) | 6.9.3 | MIT | IL weaver framework (build-time) |
| [Resource.Embedder](https://github.com/Wikipedia/Resource.Embedder) | 2.2.0 | MIT | Resource embedding (build-time) |

### NuGet Transitive Dependencies

| Component | License | Notes |
|---|---|---|
| [Mono.Cecil](https://github.com/jbevain/cecil) | MIT | Internal dependency of Fody |
| [System.ValueTuple](https://www.nuget.org/packages/System.ValueTuple/) | MIT | .NET Framework 4.x compat |

### Embedded Source Code

| Component | License | Files |
|---|---|---|
| [DotNetZip (Zlib)](https://github.com/DinoChiesa/DotNetZip) | Microsoft Public License (Ms-PL) | `Zlib/*.cs`, `CommonSrc/CRC32.cs` |
| [jzlib](https://github.com/ymnk/jzlib) | BSD-style | Part of Zlib (see `LICENSE.jzlib.txt`) |
| [zlib](https://zlib.net/) | zlib License | Part of Zlib (see `License.zlib.txt`) |
| CRC16 (Kadzus) | Public (blog-published code) | `CRC16.cs` |

### Local Libraries

| Component | License | Purpose |
|---|---|---|
| [zxcvbn-cs](https://github.com/trichards57/zxcvbn-cs) | MIT | Password strength estimation |

Note: Dependencies can vary by build/target. License compliance is the responsibility of the distributor.
