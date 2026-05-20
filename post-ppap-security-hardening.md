# Post-PPAP サーバ セキュリティ強化対策まとめ

> **実施日**: 2026-04-07
> **対象**: attachecase-key-server（Cloudflare Workers + KV）
> **対象ファイル**: `index.ts`, `types.ts`, `PostPpapManager.cs`, `PostPpapKeyInfo.cs`

---

## 1. レート制限

### 1.1 登録API（`POST /api/register/initiate`）— 3段階防御

登録APIはResendのメール送信を伴うため、最も厳しい制限を設けた。

#### ① 同一メールアドレス制限（10分に1回）

同じメールアドレスに対する確認メールの連打を防止する。

```typescript
// PENDING_KV にメールハッシュをキーとしたフラグを保存（TTL 10分）
const emailRateLimitKey = `ratelimit:email:${emailHash}`;
const emailRateLimited = await c.env.PENDING_KV.get(emailRateLimitKey);
if (emailRateLimited) {
  console.log(`RATE_LIMITED(email): hash=${emailHash.slice(0, 16)}... ip=${clientIp}`);
  return c.json<ApiResponse>({ ok: false, error: 'rate_limited' }, 429);
}

// メール送信成功後にフラグを記録
await c.env.PENDING_KV.put(emailRateLimitKey, '1', { expirationTtl: 600 });
```

- **守るもの**: 受信者のメールボックス（スパム防止）
- **KVコスト**: 読み取り1回 + 書き込み1回（送信成功時のみ）
- **TTL**: 10分で自動消滅

#### ② 日次グローバル上限（50通/日）

Resend無料枠（100通/日）の50%を安全マージンとして確保する。

```typescript
const today = new Date().toISOString().slice(0, 10); // "2026-04-07"
const dailyKey = `ratelimit:daily:${today}`;
const dailyCount = parseInt(await c.env.PENDING_KV.get(dailyKey) ?? '0');
if (dailyCount >= 50) {
  console.log(`RATE_LIMITED(daily): count=${dailyCount} ip=${clientIp}`);
  return c.json<ApiResponse>({ ok: false, error: 'service_busy' }, 503);
}

// メール送信成功後にカウントアップ
await c.env.PENDING_KV.put(dailyKey, String(dailyCount + 1), { expirationTtl: 86400 });
```

- **守るもの**: Resend無料枠（100通/日、次のプランは月$20）
- **KVコスト**: 読み取り1回 + 書き込み1回（送信成功時のみ）
- **TTL**: 24時間で自動消滅（日付が変われば新しいキー）

#### ③ 同一IP制限（1時間に5回）

単一のIPアドレスからの大量登録試行を抑止する。

```typescript
const ipRateLimitKey = `ratelimit:ip:${clientIp}`;
const ipCount = parseInt(await c.env.PENDING_KV.get(ipRateLimitKey) ?? '0');
if (ipCount >= 5) {
  console.log(`RATE_LIMITED(ip): ip=${clientIp} count=${ipCount}`);
  return c.json<ApiResponse>({ ok: false, error: 'rate_limited' }, 429);
}

// メール送信成功後にカウントアップ
await c.env.PENDING_KV.put(ipRateLimitKey, String(ipCount + 1), { expirationTtl: 3600 });
```

- **守るもの**: KV書き込み枠（Free: 1,000/日）+ Resend枠
- **IPアドレスの取得**: Cloudflareが自動付与する `cf-connecting-ip` ヘッダーを使用
- **TTL**: 1時間で自動消滅

#### レート制限の処理フロー（全体像）

```
リクエスト到着
  → ① メール制限チェック → 引っかかったら 429 + console.log
  → ② 日次上限チェック   → 引っかかったら 503 + console.log
  → ③ IP制限チェック     → 引っかかったら 429 + console.log
  → 入力バリデーション（セクション2参照）
  → 既存の登録処理（変更なし）
  → メール送信
  → ①②③のカウンターをKVに記録（3回のKV書き込み）
```

**設計判断**: カウンターの記録はメール送信成功後に行う。これによりバリデーションで弾かれたリクエストや送信失敗はカウントされず、KV書き込みも発生しない。

### 1.2 検索API（`GET /api/keys/:emailHash`）— 意図的な遅延

```typescript
// 1秒の遅延を挿入（setTimeout はCPU時間にカウントされない）
await new Promise(r => setTimeout(r, 1000));
```

- **効果1 — 列挙攻撃の抑止**: 1コネクションあたり1秒に1回が上限。SHA-256ハッシュの全空間（2^256）に対して1秒1回では、現実的な時間内にヒットすることは不可能
- **効果2 — タイミング攻撃対策**: 登録済み/未登録のレスポンス時間差が1秒の遅延に埋没し、応答時間からの存在判定を困難にする
- **KVコスト**: ゼロ（追加のKV操作なし）
- **並行攻撃**: CloudflareのインフラレベルDDoS保護に委ねる

**KVによるIP単位のロックを試みたが不採用とした経緯**: Cloudflare KVは結果整合性（eventual consistency）のため、書き込みがエッジキャッシュに反映されるまで最大60秒かかる。リアルタイムなロック機構には不向きであるため、KVに依存しない遅延方式を採用した。

### 1.3 ログ

レート制限でブロックしたリクエストを `console.log` で記録する。

```typescript
console.log(`RATE_LIMITED(email): hash=${emailHash.slice(0, 16)}... ip=${clientIp}`);
console.log(`RATE_LIMITED(daily): count=${dailyCount} ip=${clientIp}`);
console.log(`RATE_LIMITED(ip): ip=${clientIp} count=${ipCount}`);
```

- **確認方法**: Cloudflareダッシュボード → Workers & Pages → Logs、または `npx wrangler tail`
- **永続化**: Workers Logsを有効化済み（Persist logs to the Workers dashboard = ON）
- **KVコスト**: ゼロ（KVにログを書き込まない。KVへの書き込み自体が攻撃の標的になるため）

---

## 2. 入力バリデーション

登録API（`POST /api/register/initiate`）のリクエストボディに対して、フィールドごとのサイズ・形式チェックを追加した。

```typescript
// fingerprint: 16文字のhex（8バイト）
if (!/^[0-9a-f]{16}$/.test(fingerprint)) {
  return c.json<ApiResponse>({ ok: false, error: 'invalid_fingerprint' }, 400);
}

// modulus: Base64、デコード後 256〜512バイト（RSA-2048〜4096）
const modulusBytes = base64ByteLength(modulus);
if (modulusBytes < 256 || modulusBytes > 512) {
  return c.json<ApiResponse>({ ok: false, error: 'invalid_modulus' }, 400);
}

// exponent: Base64、デコード後 16バイト以内
const exponentBytes = base64ByteLength(exponent);
if (exponentBytes < 1 || exponentBytes > 16) {
  return c.json<ApiResponse>({ ok: false, error: 'invalid_exponent' }, 400);
}

// algorithmId: 有効な値のみ許可
const validAlgorithmIds = [0x01, 0x02, 0x10, 0x11, 0x12];
if (algorithmId != null && !validAlgorithmIds.includes(algorithmId)) {
  return c.json<ApiResponse>({ ok: false, error: 'invalid_algorithm' }, 400);
}

// mlkemPublicKey: ハイブリッドモード時は必須、デコード後 1184バイト（ML-KEM-768）
const isHybrid = algorithmId === 0x11 || algorithmId === 0x12;
if (isHybrid) {
  if (!mlkemPublicKey) {
    return c.json<ApiResponse>({ ok: false, error: 'missing_mlkem_key' }, 400);
  }
  const mlkemBytes = base64ByteLength(mlkemPublicKey);
  if (mlkemBytes !== 1184) {
    return c.json<ApiResponse>({ ok: false, error: 'invalid_mlkem_key' }, 400);
  }
}
```

Base64のデコード後バイト数を取得するユーティリティ関数:

```typescript
function base64ByteLength(base64: string): number {
  try {
    return Uint8Array.from(atob(base64), c => c.charCodeAt(0)).length;
  } catch {
    return -1;  // 不正なBase64
  }
}
```

### バリデーション一覧

| フィールド | チェック内容 | 弾かれる例 |
|-----------|------------|-----------|
| fingerprint | 16文字のhex | `"test"`, 長すぎる文字列 |
| modulus | Base64、256〜512バイト | 4バイトの `"dGVzdA=="` |
| exponent | Base64、1〜16バイト | 空文字、巨大な値 |
| algorithmId | 0x01,02,10,11,12のいずれか | `99`, `255` |
| mlkemPublicKey | ハイブリッド時は必須、1184バイト | 欠落、サイズ不正 |

**設計判断**: バリデーションはレート制限チェックの**前**に配置した。不正なデータはKV読み取りすら発生させずに即座に弾く。

---

## 3. ハートビートAPI（lastSeen 更新）

### 3.1 設計思想

デバイスが廃棄・アンインストールされてもサーバ上の公開鍵は残り続ける。`lastSeen`（最終通信日時）を記録することで「使われている鍵」と「もう使われていない鍵」を区別し、上限100に達した際に古い順から削除する基盤とする。

### 3.2 deviceToken — デバイス固有の認証トークン

他人のlastSeenを勝手に更新されることを防ぐため、デバイスごとの認証トークンを導入した。

**生成タイミング**: メール認証完了時（`GET /verify/:token`）

```typescript
// サーバ側: 認証完了時にdeviceTokenを生成
const deviceToken = await generateToken(32); // 64文字のランダムhex

// 鍵エントリに付与してKEYS_KVに保存
const keyEntryWithToken: PublicKeyEntry = {
  ...pending.keyEntry,
  deviceToken,
};

// pollレスポンスにも含める（クライアントが取得するため）
await c.env.PENDING_KV.put(
  `poll:${pending.pollToken}`,
  JSON.stringify({ status: 'verified', deviceToken }),
  { expirationTtl: 300 }
);
```

**クライアント側の保存**: ポーリングで`verified`を受信した際にaccounts.jsonに保存

```csharp
// PostPpapManager.cs: AwaitVerificationAsync 内
case "verified":
  if (!string.IsNullOrEmpty(result.DeviceToken) && !string.IsNullOrEmpty(emailHash))
  {
    var accounts = LoadAccounts();
    var account = accounts.Find(a =>
        string.Equals(a.EmailHash, emailHash, StringComparison.OrdinalIgnoreCase));
    if (account != null)
    {
      account.DeviceToken = result.DeviceToken;
      SaveAccounts(accounts);
    }
  }
  return true;
```

**セキュリティ特性**:
- deviceTokenはデバイスのローカル（accounts.json、DPAPI保護下）とサーバ（KEYS_KV）にのみ存在
- 検索APIのレスポンスにはdeviceTokenを含めない（除外して返す）
- 攻撃者はdeviceTokenを知り得ないため、他人のlastSeenを操作できない

### 3.3 主経路: 検索APIに統合されたlastSeen自動更新

独立したハートビートAPIを別途呼ぶのではなく、**公開鍵検索のリクエストに自デバイス情報を添える**方式を採用した。Post-PPAPの暗号化には検索が必須であるため、追加のAPIコールが不要になる。

**クライアント側**: 検索時にHTTPヘッダーで自分の情報を付与

```csharp
// PostPpapManager.cs: LookupPublicKeyAsync 内
var myAccount = LoadKeyInfo();

if (myAccount != null
    && !string.IsNullOrEmpty(myAccount.EmailHash)
    && !string.IsNullOrEmpty(myAccount.Fingerprint)
    && !string.IsNullOrEmpty(myAccount.DeviceToken))
{
  client.DefaultRequestHeaders.Add("X-My-EmailHash", myAccount.EmailHash);
  client.DefaultRequestHeaders.Add("X-My-Fingerprint", myAccount.Fingerprint);
  client.DefaultRequestHeaders.Add("X-My-DeviceToken", myAccount.DeviceToken);
}
```

**サーバ側**: 検索処理のついでにlastSeenを更新（24時間に1回まで）

```typescript
// index.ts: GET /api/keys/:emailHash 内
const myHash = c.req.header('x-my-emailhash');
const myFp = c.req.header('x-my-fingerprint');
const myToken = c.req.header('x-my-devicetoken');

if (myHash && myFp && myToken
    && /^[0-9a-f]{64}$/.test(myHash)
    && /^[0-9a-f]{16}$/.test(myFp)) {
  const myKeys = await c.env.KEYS_KV.get<PublicKeyEntry[]>(myHash, 'json');
  if (myKeys) {
    const me = myKeys.find(k => k.fingerprint === myFp && k.deviceToken === myToken);
    if (me) {
      const hoursSinceLastSeen =
        (Date.now() - new Date(me.lastSeen).getTime()) / 3600000;
      if (hoursSinceLastSeen >= 24) {
        me.lastSeen = new Date().toISOString();
        await c.env.KEYS_KV.put(myHash, JSON.stringify(myKeys));
      }
    }
  }
}
```

**設計判断**:
- try-catchで囲み、lastSeen更新の失敗が検索処理を妨げないようにした
- 24時間以内の更新はスキップし、KV書き込みを節約（1日1回まで）
- ヘッダーがない場合（旧クライアント）は何もしない（後方互換）
- 制御がサーバ側に集約されるため、クライアント側での改ざんの余地がない

### 3.4 副経路: 独立したハートビートAPI

検索APIとは別に、直接ハートビートを送ることもできる。テストや将来の用途のために残している。

```
POST /api/keys/:emailHash/heartbeat
リクエスト: { "fingerprint": "...", "deviceToken": "..." }
レスポンス: { "ok": true }
```

---

## 4. 鍵削除API

PCの紛失・盗難時に、別のデバイスから該当デバイスの公開鍵を無効化するためのAPI。

```
DELETE /api/keys/:emailHash/:fingerprint
リクエスト: { "deviceToken": "自分のdeviceToken" }
```

### 認証方式

**同一アカウントのいずれかの鍵のdeviceTokenで認証**する。これにより「自分のアカウントの鍵であること」を別のデバイスから証明できる。

```typescript
// 本人確認: 同一アカウントのいずれかの鍵のdeviceTokenが一致するか
const isAuthorized = keys.some(k => k.deviceToken === deviceToken);
if (!isAuthorized) {
  console.log(`DELETE_AUTH_FAILED: hash=${emailHash.slice(0, 16)}... ip=${...}`);
  return c.json<ApiResponse>({ ok: false, error: 'auth_failed' }, 403);
}

// 削除実行
const updated = keys.filter(k => k.fingerprint !== targetFingerprint);

if (updated.length === 0) {
  // 全鍵が削除された場合、KVエントリ自体を削除
  await c.env.KEYS_KV.delete(emailHash);
} else {
  await c.env.KEYS_KV.put(emailHash, JSON.stringify(updated));
}
```

### 利用シナリオ

```
シナリオ1: PCが盗まれた
  → 別のPCのアタッシェケースから
  → 自分のdeviceTokenで認証
  → 盗まれたPCのfingerprintを指定して削除
  → 以降、そのPC向けに暗号化されなくなる

シナリオ2: もう使わないPCを整理
  → 現在のPCから
  → 自分のdeviceTokenで認証
  → 古いPCのfingerprintを指定して削除

シナリオ3: 全デバイスからアンインストール済みでdeviceTokenがない
  → 個別対応（開発者がCloudflareダッシュボードからKVを直接削除）
  → または、lastSeenが更新されないため上限100に達した時点で自動削除される
```

### 現時点の制約

クライアント側のUI（デバイス一覧・削除ボタン）は未実装。設計資料では「フェーズ2: 上級者向け設定の奥」に分類されている。現段階ではcurlまたはCloudflareダッシュボードからの操作となる。

---

## 全エンドポイント一覧

| メソッド | エンドポイント | 用途 | 防御 |
|---------|-------------|------|------|
| POST | `/api/register/initiate` | 登録開始 | メール10分制限 + 日次50通上限 + IP 1時間5回 + 入力バリデーション |
| GET | `/verify/:token` | メール認証 | トークン推測不可能 + deviceToken生成 |
| GET | `/api/register/status/:pollToken` | ポーリング | トークン推測不可能 |
| GET | `/api/keys/:emailHash` | 公開鍵検索 | 1秒遅延 + lastSeen自動更新 + deviceToken除外 |
| POST | `/api/keys/:emailHash/heartbeat` | lastSeen更新 | deviceToken認証 |
| DELETE | `/api/keys/:emailHash/:fingerprint` | 鍵削除 | deviceToken認証 |

---

## コスト影響

| 操作 | KV読み取り | KV書き込み | Resend |
|------|----------|----------|--------|
| 正常な登録（1件） | +4回 | +6回 | 1通 |
| レート制限で弾かれた登録 | 1〜3回 | 0回 | 0通 |
| バリデーションで弾かれた登録 | 0回 | 0回 | 0通 |
| 検索（lastSeen更新なし） | +2回 | 0回 | — |
| 検索（lastSeen更新あり、24時間に1回） | +2回 | +1回 | — |
| ハートビート（独立版） | +1回 | +1回 | — |
| 鍵削除 | +1回 | +1回 | — |

Cloudflare Free プランの日次制限（KV読み取り: 100,000、KV書き込み: 1,000）に対して十分な余裕がある。

---

*このドキュメントは Post-PPAP サーバのセキュリティ強化作業の記録として作成されました。*
