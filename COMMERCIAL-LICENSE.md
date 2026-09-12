# AttacheCase Commercial Licensing

AttacheCaseは、個人・非業務利用については無償で利用できます。

業務利用、システム連携、他製品への組み込みまたは再配布については、利用形態に応じたライセンスが必要です。

## ライセンス区分

| 利用形態 | 必要なライセンス |
|---|---|
| 個人・非業務でGUIを利用 | 無償 |
| 個人・非業務でCLIを利用 | 無償 |
| 会社等で1人のユーザーがGUIを利用 | Commercial License |
| 会社等で1人のユーザーがCLIを利用 | Commercial License |
| 1人のユーザー自身の業務をバッチ・スクリプトで自動化 | Commercial License |
| 複数ユーザーが利用する社内システムからAttacheCaseを呼び出す | Integration License または利用者全員分のCommercial License |
| AttacheCaseの内部DLLを社内システムから直接利用 | Integration License |
| 自社製品からAttacheCaseを呼び出して顧客へ提供 | OEM License |
| AttacheCaseを自社製品へ同梱・再配布 | OEM License |
| AttacheCaseの機能を顧客向けサービスとして提供 | OEM Licenseまたは個別契約 |

## Commercial License

Commercial Licenseは、**1ユーザーにつき1ライセンス**です。

「ユーザー」とは自然人1名を意味します。

1つのライセンスを複数人で共有することはできません。

同一ユーザーが自己の業務のために使用する場合、別途条件が定められていない限り、そのユーザーが専ら使用する複数の端末で利用できます。

### 例

社員Aが会社のPCでAttacheCaseを使用する場合:

```text
社員A
↓
Commercial License × 1
```

社員Aが会社PCと出張用ノートPCの両方を使用する場合:

```text
社員A
├─ Desktop
└─ Notebook

Commercial License × 1
```

同じPCを社員A、B、Cの3名が使用する場合:

```text
社員A → 1 License
社員B → 1 License
社員C → 1 License

合計3 Licenses
```

## CLIおよび自動化

CLI自体は有料専用機能ではありません。

個人・非業務ユーザーもCLIを無償で利用できます。

業務でCLIを利用する場合はCommercial Licenseが必要です。

Commercial Licenseを持つユーザーは、自分自身の業務のためにCLI、バッチファイル、PowerShell、スクリプト、タスクスケジューラ等を利用して処理を自動化できます。

## Integration License

複数のユーザーが利用する社内システムからAttacheCaseを自動的に呼び出す場合は、Integration Licenseの対象となります。

たとえば、

```text
社内業務システム
      ↓
AttacheCase CLI
      ↓
暗号化・復号
```

という構成で、複数の社員がその機能を利用する場合です。

サービスアカウントやサーバープロセスの数ではなく、実際にそのシステムを利用するユーザー数を基準とします。

利用者全員がそれぞれCommercial Licenseを保有している場合は、別途Integration Licenseを不要とすることがあります。

Integration Licenseの料金および条件は、利用規模に応じて別途定めます。

## OEM License

AttacheCaseを第三者へ提供する製品またはサービスの一部として利用する場合は、OEM Licenseが必要です。

対象例:

- 自社製ソフトウェアからAttacheCase CLIを呼び出す。
- AttacheCaseを自社製品に同梱する。
- AttacheCaseを顧客環境へ再配布する。
- AttacheCaseの内部DLLを自社製品から直接利用する。
- AttacheCaseの暗号化・復号機能を自社サービスの機能として顧客へ提供する。

通常のCommercial LicenseまたはIntegration Licenseには、OEM・再配布権は含まれません。

OEM Licenseは個別契約となります。

## Source Code

AttacheCaseは、暗号化処理を含む主要ソースコードを公開しています。

ソースコードを公開している目的は、実装内容の透明性および第三者による検証可能性を確保することです。

ソースコードの公開は、無制限な商用利用、再配布または製品への組み込みを許可するものではありません。

ソースコードには `LICENSE.md` に記載されたAttacheCase Source Available Licenseが適用されます。

## Existing / Legacy Commercial Licenses

過去にHiBARA Softwareから正当に取得されたCommercial Licenseについては、取得時の契約条件により既に付与された権利を、新しいライセンス体系によって遡及的に縮減しません。

従前の契約により後継バージョンの利用権が付与されている場合、その権利は従前の契約条件に従って維持されます。

ただし、従前の契約で付与されていないOEM、再配布その他の権利が、新しいライセンス体系によって自動的に付与されるものではありません。

## Which License Do I Need?

迷った場合の基本原則は次のとおりです。

> **個人で使う → 無償**  
> **仕事で自分が使う → Commercial**  
> **社内システムが複数人のために使う → Integration**  
> **自社の商品・サービスのために使う → OEM**

具体的な利用方法がどの区分に該当するか不明な場合は、HiBARA Softwareへお問い合わせください。

## Important

本ファイルは各ライセンス区分を分かりやすく説明するための案内です。

正式な権利および義務については、ソフトウェア使用許諾契約書、ライセンス証書、個別契約その他の適用される契約条件が優先します。