# Azure App Service Easy Auth ローカルエミュレーター実現性調査

- 調査基準日: 2026-08-20
- 対象: Windows / macOS のローカル開発
- 想定用途: 既存のローカル開発サーバーをリバースプロキシで包み、App Service Easy Auth の `/.auth/*` とユーザー情報ヘッダーをダミーデータで再現する

## 結論

**実現可能性は高く、開発を推奨する（Go）**。HTTP リバースプロキシ、`/.auth` ルートの横取り、ブラウザー用ダミーログイン画面、セッション Cookie、`X-MS-CLIENT-PRINCIPAL` 生成、Windows/macOS 向け単体バイナリ配布は、いずれも既存 OSS と公式ライブラリで実証済みである。

ただし、目標は **「App Service Easy Auth のローカル開発用・公開契約エミュレーター」** と定義すべきであり、Azure 内部実装の完全な複製を掲げるべきではない。Cookie の内部形式、トークンストア、プロバイダー側トークン更新、一部エラー応答は公開仕様が不十分であり、ダミートークンは Microsoft Graph など実サービスには使えない。

| 観点 | 判定 |
|---|---|
| 指定 URL を包む新しいローカル URL | 容易。SWA CLI と既存 Easy Auth 開発プロキシで実証済み |
| Windows / macOS | 容易。.NET + YARP を自己完結 single-file で OS/CPU 別配布可能 |
| 名前、UPN、ユーザー ID、ロール、任意 claim の入力 | 容易。SWA CLI の UI がほぼ同じ UX を実証済み |
| `/.auth/login/aad`, `/.auth/me`, `/.auth/logout` | 実装可能。公開契約と既存実装から再現可能 |
| `X-MS-CLIENT-PRINCIPAL*` | 実装容易。JSON 形状が公式文書化済み |
| `/.auth/refresh` と provider token | ダミー動作は可能。実トークン更新の完全再現は別スコープ |
| Azure とバイト単位で同一の Cookie / 内部動作 | 非推奨かつ保証困難 |
| MVP 工数 | 経験者 1 名で約 3～5 週間 |
| 配布・セキュリティ・適合試験を含む v1 | 約 6～8 週間 |

## 1. 「Easy Auth は Azure 環境でしか動かない」の正確な評価

この表現は方向としては正しいが、厳密には次のように書くのがよい。

> Microsoft がサポートする App Service Easy Auth は Azure App Service / Azure Functions on App Service に組み込まれたプラットフォーム機能であり、一般のローカルホスト向けスタンドアロン・ランタイムや公式 App Service エミュレーターは提供されていない。Azure Container Apps も同系統の認証システムを利用する。

根拠は以下のとおり。

1. Microsoft Learn は Easy Auth を、アプリと同じ VM 上で全受信リクエストの前段を通る **platform feature** と説明している。Windows 非コンテナーではネイティブ IIS モジュール、Linux/コンテナーではアプリから分離したコンテナーが Ambassador パターンで動く。[^ms-overview]
2. Microsoft の Data API builder 文書はさらに明確で、`AppService` 認証プロバイダーは App Service または App Service 上の Functions でのみ使い、bare Windows host や非 App Service 環境では必要な Easy Auth 基盤がないため起動に失敗すると警告している。公式のローカル試験方法は `X-MS-CLIENT-PRINCIPAL` の手動送信である。[^ms-dab]
3. App Service の認証方式比較でも、組み込み認証は「プラットフォームに直接組み込まれる」とされ、IDE 内ローカル開発 SSO は非対応である。[^ms-identity]
4. 一方、Azure Container Apps は「Azure App Service と同じ認証・認可システム」を使うと公式に明記されている。したがって「App Service だけ」という表現は狭すぎる。[^ms-container]

### 例外に見えるもの

Linux 側の実ランタイムとみられる `mcr.microsoft.com/appsvc/middleware:stageN` を Docker でローカル起動する方法は、第三者によって実証されている。ログから `Host.ListenUrl` と `Host.DestinationHostUrl` を与えるリバースプロキシ構成も確認されている。[^hajek]

ただし、これは次の理由で本提案の代替ではない。

- Microsoft Learn が公開・サポートするローカル製品ではない。
- 内部的な `stageN` イメージと未公開設定に依存する。
- 実 Entra ID アプリ登録、client secret、実ログインを必要とする。
- 名前、UPN、claim を自由入力するダミーログインではない。
- 内部更新で挙動が変わる可能性があり、開発ツールの安定 API として扱えない。

## 2. SWA CLI が証明していること

Static Web Apps のローカル環境では、公式の SWA CLI が次を提供する。

- フロントエンド開発サーバーへのプロキシ
- API へのプロキシ
- mock authentication and authorization server
- ルーティング規則のローカル適用

公式手順では、たとえば `swa start http://localhost:3000` を起動し、ブラウザーは元の 3000 番ではなく `http://localhost:4280` を開く。4280 番へのリクエストが適切なローカルサービスへ振り分けられ、認証要求はエミュレーターが処理する。[^ms-swa-local]

`/.auth/login/<provider>` のローカル画面では以下を入力できる。

- Username
- User ID
- Roles
- Claims

ログイン後は `/.auth/me` からダミー `clientPrincipal` を取得でき、`/.auth/logout` でログアウトできる。任意 provider 名もローカルで扱える。[^ms-swa-local][^swa-local-auth]

### ソースコードで確認できる実装パターン

調査時の `Azure/static-web-apps-cli` は commit `61bfdc5dd6f273f82457b78df694dc652b7da4ae`、package version 2.0.10 である。

- `swa start` の位置引数が HTTP(S) URL なら `appDevserverUrl` として解釈する。[^swa-register]
- CLI は MSHA と呼ばれるエミュレーターを別プロセスで起動し、指定 URL を内部 upstream として使う。[^swa-start]
- `/.auth` を通常のアプリ転送より先に判定し、専用ルーターで `login`, `me`, `logout`, `purge` を処理する。[^swa-router]
- ダミーログイン画面は provider、user ID、username、roles、任意 `{typ,val}` claims を保存する。ダミーモードの Cookie は単純な Base64 JSON である。[^swa-auth-ui]
- `/.auth/me` は `{ "clientPrincipal": ... }` を返し、未ログイン時は `{ "clientPrincipal": null }` を返す。[^swa-me]
- Functions 転送時には `X-MS-CLIENT-PRINCIPAL` を追加する。ただし現行ソースでは SWA 本番挙動に合わせる目的で `claims` を削除してからヘッダー化している。[^swa-function]
- CLI 自身も「エミュレーターはクラウド環境と完全一致しない可能性があるので Azure でも試験すること」と警告する。[^swa-register]

### SWA CLI をそのまま使えない理由

SWA と App Service Easy Auth は、似た URL を持つが wire contract が異なる。

| 項目 | SWA / SWA CLI | App Service Easy Auth |
|---|---|---|
| `/.auth/me` | `{ "clientPrincipal": { ... } }` | provider 情報の配列 |
| Principal JSON | `identityProvider`, `userId`, `userDetails`, `userRoles` | `auth_typ`, `claims`, `name_typ`, `role_typ` |
| 個別ヘッダー | 主に `X-MS-CLIENT-PRINCIPAL` | `-ID`, `-NAME`, `-IDP` も提供 |
| role | `anonymous`, `authenticated` を SWA が付与 | App Service では同じ自動 role を前提にできない |
| Cookie | `StaticWebAppsAuthCookie` | 通常 `AppServiceAuthSession` として観測されるが内部形式は非公開 |
| provider token | ダミーモードでは実トークンでない | token store 有効時に `X-MS-TOKEN-*` |

したがって、SWA CLI は **UX とプロキシ構成の優れた先行例** だが、App Service 用エミュレーターは別の principal serializer、ヘッダー生成、`/.auth/me` 応答を持つ必要がある。

## 3. エミュレートすべき App Service Easy Auth の公開契約

### 3.1 リクエストヘッダー

App Service は認証済みリクエストに次のヘッダーを注入する。外部クライアントはこれらを設定できないため、App Service が設定した場合だけ信頼できる、というのが公式のセキュリティ境界である。[^ms-user]

| ヘッダー | 内容 |
|---|---|
| `X-MS-CLIENT-PRINCIPAL` | UTF-8 JSON を Base64 化した全 claim |
| `X-MS-CLIENT-PRINCIPAL-ID` | IdP が設定した caller ID。Entra では通常 object ID を使う |
| `X-MS-CLIENT-PRINCIPAL-NAME` | 人間可読名、email、UPN など |
| `X-MS-CLIENT-PRINCIPAL-IDP` | `aad` など provider 名 |

`X-MS-CLIENT-PRINCIPAL` の公式形状は次である。[^ms-user]

```json
{
  "auth_typ": "aad",
  "claims": [
    { "typ": "name", "val": "Alice Example" },
    { "typ": "preferred_username", "val": "alice@example.com" },
    { "typ": "roles", "val": "Admin" }
  ],
  "name_typ": "name",
  "role_typ": "roles"
}
```

重要な互換要件:

- claim は dictionary ではなく **配列のまま** 保持する。同じ `typ` の role や group が複数あり得る。
- claim 名はトークンから既定マッピングされることがあり、元トークンと異なる場合がある。[^ms-user]
- 大文字小文字を含むヘッダー表現はフレームワークによって変わり得る。
- UPN の convenience field を用意しても、最終的にはユーザーが正確な claim type を選べる必要がある。
- SWA の `anonymous` / `authenticated` role を App Service モードで無条件追加しない。

token store 有効時には、Entra ID について少なくとも次が対象になる。[^ms-tokens]

- `X-MS-TOKEN-AAD-ID-TOKEN`
- `X-MS-TOKEN-AAD-ACCESS-TOKEN`
- `X-MS-TOKEN-AAD-EXPIRES-ON`
- `X-MS-TOKEN-AAD-REFRESH-TOKEN`

MVP ではこれらを profile の任意文字列として注入可能にし、既定では省略するのが安全である。実 Graph API で使えるトークンを生成したように見せてはならない。

### 3.2 `/.auth` ルート

| ルート | 公式挙動 | MVP 方針 |
|---|---|---|
| `GET /.auth/login/aad` | Entra ID ログインを開始 | ダミープロファイル入力画面 |
| `GET /.auth/login/<provider>/callback` | IdP が戻る callback | fake mode では予約し、不正アクセスは 400/404 |
| `POST /.auth/login/aad` | client-directed token login | v0.2。ダミー token/profile を明示的に受理 |
| `GET /.auth/me` | 認証ユーザーと provider token を返す | App Service 形式の配列を返す |
| `GET /.auth/logout` | Cookie と token store を削除し sign-out | ローカルセッション削除と redirect |
| `GET /.auth/refresh` | token store の token を更新 | fake mode では期限延長だけ行う stub |

ログイン、callback、client-directed flow は App Service の認証フローで公式に説明されている。[^ms-overview] `post_login_redirect_uri`、`post_logout_redirect_uri`、`X-ZUMO-AUTH` を使う client-directed login、logout の動作も文書化されている。[^ms-signout]

`/.auth/me` は現在の Microsoft Learn では「provider-specific tokens を返す」とされるが、完全な JSON schema は掲載されていない。機能の元開発者による記録では次の配列である。[^ms-tokens][^gillum-token-store]

```json
[
  {
    "provider_name": "aad",
    "user_id": "00000000-0000-0000-0000-000000000000",
    "user_claims": [
      { "typ": "name", "val": "Alice Example" }
    ],
    "access_token": null,
    "authentication_token": null,
    "expires_on": null,
    "id_token": null,
    "refresh_token": null
  }
]
```

この資料は 2016 年のため、フィールドの存在条件、未認証時の `401` と `[]` の差、token store 無効時の応答は **実 App Service に対する適合試験で確定すべき項目** である。MVP の既定は未認証時 `[]` とし、`--compat unauthenticated-me=401|empty-array` のように切り替え可能にすると安全である。

### 3.3 設定動作

App Service の file-based configuration には次が含まれる。[^ms-file-config]

- `globalValidation.unauthenticatedClientAction`
- `globalValidation.redirectToProvider`
- `globalValidation.excludedPaths`
- `httpSettings.requireHttps`
- `httpSettings.routes.apiPrefix`
- `httpSettings.forwardProxy`
- `login.tokenStore`
- `login.cookieExpiration`
- `login.allowedExternalRedirectUrls`
- provider 登録

全 schema を初版で再実装する必要はない。優先順位は次のとおり。

1. `AllowAnonymous` と `RedirectToLoginPage`
2. 401 / 403 / 404 rejection
3. excluded paths
4. `apiPrefix`（既定 `/.auth`）
5. same-origin redirect 制約
6. HTTPS と forward headers

### 3.4 複数 IdP と issuer

App Service が公式に提供する組み込み IdP とログイン URL は次のとおりである。[^ms-overview]

| IdP | ログイン URL |
|---|---|
| Microsoft Entra ID | `/.auth/login/aad` |
| Facebook | `/.auth/login/facebook` |
| Google | `/.auth/login/google` |
| X | `/.auth/login/x` |
| GitHub | `/.auth/login/github` |
| Apple | `/.auth/login/apple` |
| 任意の OpenID Connect | `/.auth/login/<providerName>` |

本エミュレーターの初版では、上記の組み込み6種類を対象とし、任意の OpenID Connect は対象外とする。

#### 確定している契約

- `X-MS-CLIENT-PRINCIPAL` のトップレベル項目は `auth_typ`, `claims`, `name_typ`, `role_typ` であり、`issuer` は含まれない。[^ms-user]
- `/.auth/me` の既知のトップレベル項目は `provider_name`, `user_id`, `user_claims` と provider token 関連項目であり、既存資料と観測例には `issuer` という項目はない。[^gillum-token-store][^easyauth-live-capture]
- issuer は、トークンに存在する場合、`claims` / `user_claims` 配列内の `{ "typ": "iss", "val": "..." }` として表現される。Microsoft Entra ID の `iss` はトークン発行者を表す URI である。[^ms-id-token-claims][^easyauth-live-capture]
- X の公式ログイン URL は `/.auth/login/x` だが、App Service の設定キーと token header は従来名の `twitter` / `X-MS-TOKEN-TWITTER-*` を使用する。[^ms-overview][^ms-tokens][^ms-file-config]

#### 公式文書だけでは確定できない項目

- X の `auth_typ`, `X-MS-CLIENT-PRINCIPAL-IDP`, `/.auth/me.provider_name` が `x` か `twitter` か。
- Facebook, Google, X, GitHub, Apple の App Service による完全な claim mapping。
- 未認証時や token store 無効時の `/.auth/me` の厳密な応答。

したがって実装では、ログイン URL を公式表どおり固定する一方、`authenticationType` と convenience field の claim mapping をプロファイル設定で上書き可能にする。X の既定 `authenticationType` は `x` とし、必要な利用者は `twitter` へ変更できる。issuer 入力欄は全 IdP に用意するが、公式に issuer を確認できる AAD, Google, Apple だけ初期値を設定し、Facebook, X, GitHub は空欄とする。

### 3.5 App Service と Azure Container Apps の差分追記（2026-08-23）

Azure Container Apps は App Service と同じ認証・認可システムを使うが、公開文書、ARM スキーマ、Azure CLI を比較すると次の差分がある。[^ms-container][^ms-container-token-store][^aca-auth-arm][^appservice-auth-arm][^aca-auth-cli][^webapp-auth-cli]

#### 確認済みの差分

| 項目 | App Service | Azure Container Apps |
|---|---|---|
| 既定の logout 完了 URL | `/.auth/logout/complete` | `/.auth/logout/done` |
| `unauthenticatedClientAction.Return404` | 概念文書と CLI にあり | 概念文書・CLI ともになし |
| `globalValidation.requireAuthentication` | ARM スキーマにあり | ARM スキーマになし |
| `login.tokenStore.fileSystem` | ARM スキーマにあり | ARM スキーマになし |
| `encryptionSettings` | ARM スキーマになし | 署名・暗号化 secret 名を指定する項目あり |
| `legacyMicrosoftAccount` | ARM スキーマにあり | ARM スキーマになし |
| token refresh extension の専用 CLI 引数 | `az webapp auth update` にあり | `az containerapp auth update` になし |
| legacy V1 / V2 CLI | `auth-classic`, `config-version` あり | なし |

App Service の `Return404` は概念文書と CLI では確認できる一方、ARM スキーマの enum には反映されていない。これは Azure REST API specification の既知の不一致として報告されている。[^appservice-return404-spec-gap]

#### 公式文書間の不一致

- Apple は App Service の概念文書、ARM、CLI に存在する。
- ACA の概念文書と専用 how-to には Apple がないが、ACA の ARM スキーマと `az containerapp auth apple` には存在する。
- ACA の `platform.configFilePath` は ARM スキーマにないが、`az containerapp auth update --config-file-path` は存在する。
- ACA の client-directed login 表には `microsoftaccount` があるが、対応する ARM provider 項目や専用 CLI group は確認できない。
- App Service 文書は GitHub provider の customized sign-in / sign-out を非対応と明記するが、ACA 文書には同じ制限の記載がない。実動作差か文書差かは未確認。

実装では技術面の証拠がある Apple を両モードで利用可能にし、`microsoftaccount` は追加しない。

#### ACA 文書で独立確認できない項目

- App Service 文書にある8時間 session / 72時間 grace period と同じ数値。
- `/.auth/me` の完全な JSON スキーマと未認証時の応答。
- `AppServiceAuthSession` という Cookie 名。
- 非 AAD の完全な claim mapping。
- App Service の preview 機能である Protected Resource Metadata (`/.well-known/oauth-protected-resource`) に相当する ACA 文書・設定。

同じ認証エンジンであることから共通動作が期待されるが、証拠のない platform 差分は追加しない。

#### 実装方針

現在の fake profile / no-token emulator で利用者が観測できる確定差分は、platform 表示と既定 logout 完了 URL である。

- `--platform app-service`: `/.auth/logout/complete`
- `--platform container-apps`: `/.auth/logout/done`

principal、4つの `X-MS-CLIENT-PRINCIPAL*`、IdP、`/.auth/me`、セッション、プロキシは共有する。`Return404`、`requireAuthentication`、token store、`encryptionSettings` は、将来対応設定を実装するときの platform 制約として扱う。

Protected Resource Metadata は実 token / MCP resource metadata を扱う機能であり、現在の fake-profile emulator の対象外とする。GitHub の customized sign-in / sign-out も、現状の模擬 GET ログインとは別機能なので runtime 分岐を追加しない。

ACA 固有の運用上の注意として、SPA のクライアント側ルーターが `/.auth/login/*` を横取りすると sidecar に届かないことが公式文書に明記されている。[^ms-container]

## 4. 既存ツール調査

### 4.1 `pnopjp/easyauth-emulator`

調査時 commit: `c1232d1bb97dea060184057dabf4f08ee48ddc8d`、Apache-2.0。

これは名称まで同じ既存プロジェクトで、かなり広い範囲を実装している。[^pnop-readme]

- `--app-upstream http://localhost:3000` で upstream を指定
- `/.auth/me`, `/.auth/login[/<idp>]`, `/.auth/logout`, `/.auth/refresh`
- `X-MS-CLIENT-PRINCIPAL*`
- AAD access token / ID token
- Windows x64、macOS Apple Silicon、Linux 向けバイナリ
- WebSocket、オプションの HTTP/2 / gRPC
- VS Code extension

しかし本依頼との差は大きい。

- oauth2-proxy を使った **実 IdP ログイン** であり、client ID / secret と callback 登録が必要。
- README、設定、実装を確認した範囲では、SWA CLI のように名前、UPN、role、任意 claim を画面で自由入力する dummy profile mode はない。
- `/.auth/refresh` は認証済みなら 200 を返すだけの stub。
- `X-MS-TOKEN-AAD-EXPIRES-ON` と refresh token header は未実装。
- byte-for-byte compatibility ではないと明記されている。

実装では未ログインの `/.auth/me` に `[]` を返し、ログイン時は ID token から `user_claims` を生成している。[^pnop-app]

**評価:** 競合であると同時に最短の拡張候補である。新規開発前に、同プロジェクトへ `AUTH_MODE=fake` と profile editor を追加する提案を検討すべきである。

### 4.2 `alanta/EasyAuthDevProxy`

調査時 commit: `7027118eabb6f9ff60841e4a2ad753e5fadf0eca`、MIT。

Container Apps 向けだが、依頼内容に最も近い fake-login UX を持つ。[^alanta-readme]

- .NET / YARP のリバースプロキシ
- `--urls=https://localhost:8888 --backend=https://localhost:7290`
- `/.auth/login/<idp>` で username、user ID、roles を入力
- Cookie から `X-MS-CLIENT-PRINCIPAL`, `-IDP`, `-NAME`, `-ID` を注入
- 実 IdP は不要
- Aspire integration

不足・リスク:

- 任意 claim 入力、UPN 専用入力、`/.auth/me`、`/.auth/refresh`、token headers がない。
- `Program.cs` で明示的に登録している auth route は logout のみで、残りは Razor Pages と汎用 proxy に依存する。[^alanta-program]
- Cookie は principal JSON の Base64 であり、decode 部に `TODO: validate` が残る。[^alanta-easyauth]
- `role_typ` は `"role"`、生成 claim は `"roles"` となっており、厳密な `ClaimsPrincipal.IsInRole()` 互換性に差が出る可能性がある。[^alanta-profile]
- 外部から届いた `X-MS-*` の明示的除去が確認できず、開発端末外へ公開すると header spoofing の危険がある。

**評価:** YARP による最小実装の良い proof of concept。ただし、そのまま製品化するより security model と App Service contract を作り直すべきである。

### 4.3 `buchanan-edwards/azure-easy-auth-local`

2018 年の Express middleware で、`AppServiceAuthSession` を使って既に Azure にデプロイ済みのアプリの `/.auth/me` / `/.auth/refresh` に中継する。[^legacy-local]

ダミー認証ではなく、Azure 側へのデプロイと実セッションを必要とするため本要件を満たさない。

### 4.4 実 middleware コンテナー

前述の `mcr.microsoft.com/appsvc/middleware` は高い忠実度を期待できるが、非公式・内部依存・実 IdP 必須である。fake identity の高速なローカル試験という目的には合わない。ただし、互換試験の比較対象としては有用である。

### 4.5 市場ギャップ

確認できた選択肢は次の分布になっている。

| ツール | fake login | 任意 claim | App Service 形式 | proxy URL | 実 IdP 不要 |
|---|---:|---:|---:|---:|---:|
| SWA CLI | ○ | ○ | × | ○ | ○ |
| pnopjp/easyauth-emulator | × | トークン由来のみ | ○（部分） | ○ | × |
| EasyAuthDevProxy | ○ | × | ○（部分） | ○ | ○ |
| azure-easy-auth-local | × | × | Azure に依存 | × | × |

**「SWA CLI の fake profile UX」+「App Service Easy Auth の header/route contract」+「単一 CLI」** の組み合わせには、依然として明確な余地がある。ただし `EasyAuth Emulator` という名称は既に使われているため、名称衝突と Microsoft 公式品に見える表現を避ける必要がある。

## 5. 推奨アーキテクチャ

### 5.1 技術選定

**推奨: .NET 10 + ASP.NET Core + YARP**

理由:

- YARP は Windows、Linux、macOS で開発・ローカルサービスとして利用できる。[^yarp-start]
- custom request transform で転送前ヘッダーを除去・追加できる。[^yarp-transforms]
- WebSocket は既定で proxy でき、HTTP/1.1 と HTTP/2 の変換にも対応する。[^yarp-websocket]
- `.NET publish` の self-contained single-file により OS/CPU 別の単体バイナリを作れる。[^dotnet-single]
- EasyAuthDevProxy に実例があり、実装リスクが低い。
- Node.js ランタイムを利用者に要求せずに配布できる。

推奨配布物:

- `win-x64`
- `win-arm64`
- `osx-x64`
- `osx-arm64`

Node.js / TypeScript も SWA CLI とコード共有しやすいが、単一実行ファイル、YARP の protocol 対応、既存 EasyAuthDevProxy の知見を考えると .NET が有利である。

### 5.2 データフロー

```text
Browser
  |
  | http://127.0.0.1:4180
  v
Easy Auth Emulator
  |- /.auth/* をローカル処理
  |- session/profile を解決
  |- 外部 X-MS-* を除去
  |- X-MS-CLIENT-PRINCIPAL* を生成
  |- X-Forwarded-* を設定
  v
Upstream development server
  http://localhost:5173
```

構成要素:

1. **CLI host**: 引数、設定、空き port、browser open、終了処理
2. **Auth route handler**: login UI、me、logout、refresh
3. **Profile store**: 設定済み profile と UI 入力
4. **Session store**: CSPRNG で発行した opaque ID とサーバー側 profile
5. **Principal builder**: App Service 形式 JSON と個別ヘッダー
6. **Header sanitizer**: client-supplied platform headers を必ず除去
7. **YARP proxy**: HTTP、stream、WebSocket、forwarded headers
8. **Compatibility layer**: 未認証 `/.auth/me` など不確定挙動の切替

## 6. 推奨 CLI と設定

### 6.1 最小 UX

```console
appservice-auth-emulator start http://localhost:5173 --port 4180 --open
```

想定出力:

```text
Upstream:  http://localhost:5173
Proxy:     http://127.0.0.1:4180
Login:     http://127.0.0.1:4180/.auth/login/aad
Profile:   default
```

ブラウザーは常に `Proxy` URL を開く。元 upstream を直接開くと認証を迂回できるため、起動ログと UI の両方で警告する。

CI / 自動試験用:

```console
appservice-auth-emulator start http://localhost:5173 \
  --profile alice-admin \
  --no-ui \
  --port 4180
```

### 6.2 設定例

```json
{
  "$schema": "https://example.invalid/appservice-auth-emulator.schema.json",
  "upstream": "http://localhost:5173",
  "listen": {
    "host": "127.0.0.1",
    "port": 4180,
    "https": false
  },
  "auth": {
    "apiPrefix": "/.auth",
    "defaultProvider": "aad",
    "unauthenticatedAction": "allowAnonymous",
    "sessionLifetime": "08:00:00",
    "allowedExternalRedirectUrls": []
  },
  "profiles": {
    "alice-admin": {
      "provider": "aad",
      "userId": "11111111-1111-1111-1111-111111111111",
      "displayName": "Alice Example",
      "upn": "alice@example.com",
      "nameClaimType": "name",
      "roleClaimType": "roles",
      "roles": ["Admin", "Reader"],
      "claims": [
        { "typ": "name", "val": "Alice Example" },
        { "typ": "preferred_username", "val": "alice@example.com" },
        {
          "typ": "http://schemas.microsoft.com/identity/claims/objectidentifier",
          "val": "11111111-1111-1111-1111-111111111111"
        },
        {
          "typ": "http://schemas.microsoft.com/identity/claims/tenantid",
          "val": "22222222-2222-2222-2222-222222222222"
        },
        { "typ": "department", "val": "Engineering" }
      ],
      "tokens": {
        "idToken": null,
        "accessToken": null,
        "refreshToken": null,
        "expiresOn": null
      }
    }
  }
}
```

設計上は `displayName`, `upn`, `userId`, `roles` を convenience input としつつ、最終 `claims` 配列を画面で確認・上書きできるようにする。Entra v2 claim 名と既定 claim mapping 後の URI claim 名を profile preset で選べるとよい。

## 7. ルートごとの推奨仕様

### `GET /.auth/login/<provider>`

- provider の既定は `aad`。
- profile 選択または編集画面を表示。
- 入力: display name、UPN/email、user ID/OID、tenant ID、roles、任意 claims、任意 dummy token。
- `post_login_redirect_uri` を保持。
- 保存時に新しい session ID を発行し、固定 URL `/` または検証済み redirect へ 302。
- 任意 claim JSON は schema validation し、`typ` と `val` の文字列以外を拒否。

### `GET /.auth/me`

- 認証済み: App Service 形式の配列を返す。
- 未認証: 初期値は `200 []`。実 Azure 適合試験の結果に応じて互換モードを更新。
- token 未設定時は null または field omission を compatibility option で選択。
- `Cache-Control: no-store` を付与。

### `GET /.auth/logout`

- session store から session を削除。
- Cookie を期限切れにする。
- same-origin の `post_logout_redirect_uri` を許可。
- 指定がなければ `/.auth/logout/complete` へ redirect。
- Container Apps 文書では既定が `/.auth/logout/done`、現行 App Service 文書では `/.auth/logout/complete` と差があるため、互換モードで選択可能にする。[^ms-signout][^ms-container]

### `GET /.auth/refresh`

fake mode:

- 認証済みなら session 期限を延長して 200。
- 未認証なら 401。
- token 値は変更しない。
- verbose log に「実 provider token は更新していない」と表示。

real IdP mode を将来追加する場合だけ、本当の refresh token exchange を行う。

### その他の `/.auth/*`

upstream に転送せず 404 とする。認証 namespace がアプリへ漏れると、本番とローカルのルーティング差や意図しない endpoint 公開につながる。

## 8. セキュリティ要件

開発用でも、認証前段を名乗る proxy には最低限の防御が必要である。

1. **loopback bind を既定にする**  
   `127.0.0.1` / `::1` のみ。`0.0.0.0` は `--allow-network-access` がある場合だけ許可し、強い警告を出す。

2. **upstream を制限する**  
   既定は loopback URL のみ。任意外部 URL は `--allow-remote-upstream` を要求し、open proxy / SSRF 化を防ぐ。

3. **platform-owned header を除去してから再生成する**  
   少なくとも `X-MS-CLIENT-PRINCIPAL`, `X-MS-CLIENT-PRINCIPAL-ID`, `-NAME`, `-IDP`, `X-MS-TOKEN-*` を client request から削除する。公式にも外部 request はこれらを設定できないことが信頼の前提とされる。[^ms-user]

4. **Cookie に principal/PII を直接入れない**  
   Cookie は 128-bit 以上の CSPRNG opaque session ID とし、profile はメモリ上に置く。OWASP も session ID を意味のない値にし、PII を含めないことを推奨している。[^owasp-session]

5. **Cookie 属性**  
   `HttpOnly`, `SameSite=Lax`, `Path=/`, 明示 expiry。HTTPS mode では `Secure`。HTTP localhost では `Secure` を付けると動作しないため、起動時に「開発専用」を明示する。

6. **redirect 検証**  
   相対 URL または明示 allowlist の同一 origin URL のみ許可する。`//evil.example`、scheme 変更、userinfo、encoded traversal を拒否する。

7. **CSRF**  
   profile 保存 POST に anti-forgery token を使う。App Service 自体も session-cookie 認証 POST の Origin/Referer/CORS 条件を検査する。[^ms-overview]

8. **TLS 検証を安易に無効化しない**  
   self-signed upstream を許す場合は `--allow-untrusted-upstream-certificate` を明示指定させる。既定で全証明書を許可しない。

9. **secret と token のログ抑制**  
   `X-MS-TOKEN-*`, Cookie、principal 全文は通常ログに出さない。`--verbose` でも token はマスクする。

10. **製品境界を明示する**  
    「認証のセキュリティ試験」「Entra conditional access」「Graph token」「本番環境」の代替ではない。SWA CLI と同様、Azure 上で最終試験が必要と表示する。

## 9. 完全互換にできない、または初版から外すべき項目

### 9.1 実 provider token

dummy access token は Microsoft Graph、Azure Storage、別 API では検証に失敗する。選択肢は次の三つ。

1. 既定では token header を省略する。
2. アプリが単に token の有無や parse を試す用途向けに、明確に `dev-only` としたローカル署名 JWT を生成する。
3. 実 downstream API 呼び出しが必要な利用者には、将来の real IdP mode または既存 `pnopjp/easyauth-emulator` を案内する。

fake token を実 token のように黙って返す設計は避ける。

### 9.2 Cookie 内部形式

App Service の認証 Cookie は platform-managed であり、現在の公式文書は Cookie 名や暗号化 wire format を契約として公開していない。アプリは Cookie を解析すべきではないため、エミュレーターは名前とブラウザーセッション挙動だけを合わせ、byte-level compatibility を非目標とする。

### 9.3 Windows IIS 統合

Windows App Service の Easy Auth は IIS module である。ASP.NET Framework の `ClaimsPrincipal.Current` など、IIS 内プロセス統合に依存するアプリは外部 proxy のヘッダーだけでは完全再現できない。[^ms-overview][^ms-user]

対象を「ヘッダーまたは `/.auth/*` を利用するアプリ」と明記し、必要なら各フレームワーク用の小さな header-to-principal adapter を別パッケージにする。

### 9.4 platform authorization の全設定

Entra tenant/audience/application allowlist、全 provider、PKCE、nonce、token store backend、Front Door の forward proxy 規則まで初版で再現すると範囲が急増する。fake profile に必要な公開 surface から始めるべきである。

## 10. 実装ロードマップ

### Phase 0: 適合性スパイク（3～5 日）

- 最小 App Service 検証アプリを Azure に作成。
- AllowAnonymous / RequireAuthentication、token store on/off を組み合わせる。
- `/.auth/me`, logout, refresh、redirect、Cookie 属性、header の golden capture を取得。
- Windows / Linux App Service の差を記録。
- 公開契約と非公開挙動を分類。

### Phase 1: MVP（3～5 週間）

- `start <upstream-url>`
- 空き port 選択、proxy URL 表示、`--open`
- HTTP / WebSocket proxy
- fake Entra profile UI
- name、UPN、OID、tenant ID、roles、任意 claims
- server-side in-memory session
- `/.auth/login/aad`, `/.auth/me`, `/.auth/logout`, refresh stub
- `X-MS-CLIENT-PRINCIPAL*`
- 外部 `X-MS-*` 除去
- JSON config と profile preset
- Windows/macOS の self-contained binaries
- unit / integration / browser E2E

### Phase 2: v1 hardening（追加 2～3 週間）

- HTTPS / dev certificate
- excluded paths と unauthenticated action
- token header の明示 dummy 値
- CI 用 no-UI mode
- config schema と shell completion
- proxy timeout、large header、streaming、IPv4/IPv6
- signed release artifacts、SBOM、更新手順
- Azure golden tests

### Phase 3: 任意の高忠実度機能

- client-directed `POST /.auth/login/<provider>`
- local JWT issuer / JWKS
- real IdP mode
- HTTP/2 / gRPC
- persistent encrypted profiles
- VS Code / Aspire integration

## 11. テスト戦略

### Unit

- principal JSON と Base64
- duplicate claim / Unicode / large claim
- `name_typ`, `role_typ`
- header sanitization
- redirect allowlist
- session expiry
- config validation

### Integration

echo upstream を起動し、次を確認する。

- 未ログイン時は identity header がない。
- login 後は全 proxy request に正しい header が付く。
- 外部から偽 `X-MS-CLIENT-PRINCIPAL` を送っても上書きされる。
- `/.auth/*` は upstream に届かない。
- `/.auth/me` と転送 header の claim が一致する。
- logout 後に header が消える。
- request body、streaming、SSE、WebSocket が壊れない。

### Cross-platform

- Windows x64 / arm64
- macOS Intel / Apple Silicon
- localhost の IPv4 / IPv6
- HTTP upstream / HTTPS upstream
- Chrome / Edge / Safari

### Azure conformance

同一 profile に相当する実 Entra ユーザーを使い、次を snapshot 比較する。

- status / Location / Cache-Control
- decoded principal shape
- individual identity headers
- `/.auth/me` の存在フィールド
- Cookie 属性。ただし暗号化値そのものは比較しない。

## 12. 開発方針の選択

### 最速: 既存プロジェクトへ貢献

`pnopjp/easyauth-emulator` に fake mode と profile editor を追加するのが、proxy、route、header、binary packaging、WebSocket/gRPC を再利用できる最短経路である。Apache-2.0 の条件を確認したうえで、まず upstream maintainer に提案する価値が高い。

### 最も制御しやすい: .NET/YARP で独立実装

長期的に App Service contract、セキュリティ、配布品質を自分たちで管理するならこちらを推奨する。`EasyAuthDevProxy` は設計参考または MIT 条件下の部品候補にできるが、Cookie validation、header sanitization、route coverage はそのまま採用しない。

### 推奨判断

1. `pnopjp/easyauth-emulator` に fake mode を提案する。
2. 受け入れ方針や製品目的が合わなければ、.NET/YARP で独立実装する。
3. いずれの場合も Phase 0 の実 Azure 適合試験を先に行う。

## 最終判定

本アイデアの中心部分は技術的に難しくない。むしろ重要なのは、SWA と App Service の似て非なる契約を混同しないこと、外部 `X-MS-*` を除去すること、fake token の限界を明示すること、そして非公開の内部動作を「完全互換」と約束しないことである。

**推奨する v1 の価値提案:**

> 任意のローカル Web アプリを 1 コマンドで App Service Easy Auth 互換 proxy の後ろに置き、Entra ID ユーザーの名前、UPN、OID、role、任意 claim をブラウザーまたは設定ファイルから再現し、`/.auth/me` と `X-MS-CLIENT-PRINCIPAL*` をデプロイなしでデバッグできる。

この範囲なら Windows/macOS で安定した開発ツールとして成立し、既存ツールとの差別化も明確である。

---

## 出典

[^ms-overview]: Microsoft Learn, [Authentication and authorization in Azure App Service and Azure Functions](https://learn.microsoft.com/en-us/azure/app-service/overview-authentication-authorization)（platform feature、Windows IIS module、Linux/container ambassador、認証フロー、CSRF、token store。2026-08-20 参照）
[^ms-dab]: Microsoft Learn, [Configure App Service authentication (EasyAuth) - Data API builder](https://learn.microsoft.com/en-us/azure/data-api-builder/concept/security/authenticate-easy-auth)（非 App Service 環境には Easy Auth 基盤がなく、ローカルでは `X-MS-CLIENT-PRINCIPAL` を手動送信するという明示的警告）
[^ms-identity]: Microsoft Learn, [Authentication scenarios and recommendations](https://learn.microsoft.com/en-us/azure/app-service/identity-scenarios)（built directly into the platform、ローカル IDE SSO 比較）
[^ms-container]: Microsoft Learn, [Authentication and authorization in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/authentication)（App Service と同じ認証システム、sidecar、routes、logout）
[^ms-user]: Microsoft Learn, [Work with user identities in Azure App Service authentication](https://learn.microsoft.com/en-us/azure/app-service/configure-authentication-user-identities)（identity headers と principal JSON schema）
[^ms-tokens]: Microsoft Learn, [Manage OAuth tokens in Azure App Service](https://learn.microsoft.com/en-us/azure/app-service/configure-authentication-oauth-tokens)（`/.auth/me`, `/.auth/refresh`, token headers、8 時間 session と 72 時間 grace period）
[^ms-signout]: Microsoft Learn, [Customize sign-ins and sign-outs in Azure App Service authentication](https://learn.microsoft.com/en-us/azure/app-service/configure-authentication-customize-sign-in-out)（login、client-directed login、logout、redirect）
[^ms-file-config]: Microsoft Learn, [Configure authentication in Azure App Service by using a configuration file](https://learn.microsoft.com/en-us/azure/app-service/configure-authentication-file-based)（file-based configuration 全体）
[^ms-swa-local]: Microsoft Learn, [Set up local development for Azure Static Web Apps](https://learn.microsoft.com/en-us/azure/static-web-apps/local-development)（`swa start <url>`、localhost:4280、mock auth、fake profile fields）
[^swa-local-auth]: Azure Static Web Apps CLI docs, [Local authentication](https://azure.github.io/static-web-apps-cli/docs/cli/local-auth/)（fake UI と `clientPrincipal` 応答）
[^swa-register]: Azure/static-web-apps-cli commit `61bfdc5`, [`src/cli/commands/start/register.ts`](https://github.com/Azure/static-web-apps-cli/blob/61bfdc5dd6f273f82457b78df694dc652b7da4ae/src/cli/commands/start/register.ts#L10-L76)（URL 位置引数、cloud mismatch 警告）
[^swa-start]: Azure/static-web-apps-cli commit `61bfdc5`, [`src/cli/commands/start/start.ts`](https://github.com/Azure/static-web-apps-cli/blob/61bfdc5dd6f273f82457b78df694dc652b7da4ae/src/cli/commands/start/start.ts#L44-L90) および [MSHA process 起動](https://github.com/Azure/static-web-apps-cli/blob/61bfdc5dd6f273f82457b78df694dc652b7da4ae/src/cli/commands/start/start.ts#L246-L258)
[^swa-router]: Azure/static-web-apps-cli commit `61bfdc5`, [`src/msha/auth/index.ts`](https://github.com/Azure/static-web-apps-cli/blob/61bfdc5dd6f273f82457b78df694dc652b7da4ae/src/msha/auth/index.ts#L8-L65)
[^swa-auth-ui]: Azure/static-web-apps-cli commit `61bfdc5`, [`src/public/auth.html`](https://github.com/Azure/static-web-apps-cli/blob/61bfdc5dd6f273f82457b78df694dc652b7da4ae/src/public/auth.html)（fake profile form、claims、Base64 Cookie）
[^swa-me]: Azure/static-web-apps-cli commit `61bfdc5`, [`src/msha/auth/routes/auth-me.ts`](https://github.com/Azure/static-web-apps-cli/blob/61bfdc5dd6f273f82457b78df694dc652b7da4ae/src/msha/auth/routes/auth-me.ts#L1-L31)
[^swa-function]: Azure/static-web-apps-cli commit `61bfdc5`, [`src/msha/handlers/function.handler.ts`](https://github.com/Azure/static-web-apps-cli/blob/61bfdc5dd6f273f82457b78df694dc652b7da4ae/src/msha/handlers/function.handler.ts#L20-L62)（header injection、claims 削除、fake bearer）
[^gillum-token-store]: Chris Gillum, [The App Service Token Store](https://cgillum.tech/2016/03/08/app-service-token-store/)（`/.auth/me` の具体的 JSON、token store、refresh。歴史的資料として使用）
[^pnop-readme]: pnopjp/easyauth-emulator commit `c1232d1`, [README](https://github.com/pnopjp/easyauth-emulator/blob/c1232d1bb97dea060184057dabf4f08ee48ddc8d/README.md) および [config example](https://github.com/pnopjp/easyauth-emulator/blob/c1232d1bb97dea060184057dabf4f08ee48ddc8d/config.toml.example)
[^pnop-app]: pnopjp/easyauth-emulator commit `c1232d1`, [`src/app.py` auth handlers](https://github.com/pnopjp/easyauth-emulator/blob/c1232d1bb97dea060184057dabf4f08ee48ddc8d/src/app.py#L893-L949)
[^alanta-readme]: alanta/EasyAuthDevProxy commit `7027118`, [README](https://github.com/alanta/EasyAuthDevProxy/blob/7027118eabb6f9ff60841e4a2ad753e5fadf0eca/README.md)
[^alanta-program]: alanta/EasyAuthDevProxy commit `7027118`, [`Program.cs`](https://github.com/alanta/EasyAuthDevProxy/blob/7027118eabb6f9ff60841e4a2ad753e5fadf0eca/EasyAuthDevProxy/Program.cs#L26-L67)
[^alanta-easyauth]: alanta/EasyAuthDevProxy commit `7027118`, [`Infrastructure/EasyAuth.cs`](https://github.com/alanta/EasyAuthDevProxy/blob/7027118eabb6f9ff60841e4a2ad753e5fadf0eca/EasyAuthDevProxy/Infrastructure/EasyAuth.cs#L38-L72)
[^alanta-profile]: alanta/EasyAuthDevProxy commit `7027118`, [`Pages/Index.cshtml.cs`](https://github.com/alanta/EasyAuthDevProxy/blob/7027118eabb6f9ff60841e4a2ad753e5fadf0eca/EasyAuthDevProxy/Pages/Index.cshtml.cs#L9-L68)
[^legacy-local]: buchanan-edwards/azure-easy-auth-local commit `8af11e7`, [`azure-easy-auth-local.js`](https://github.com/buchanan-edwards/azure-easy-auth-local/blob/8af11e7f0b8780d6b1790d5eb5a15c1e35bdede2/azure-easy-auth-local.js#L1-L130)
[^hajek]: Jan Hájek, [Running EasyAuth in Docker revisited](https://hajekj.net/2024/12/15/running-easyauth-in-docker-revisited/)（非公式の middleware image / startup parameters / local Docker 手順）
[^yarp-start]: Microsoft Learn, [Get started with YARP](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started?view=aspnetcore-10.0)
[^yarp-transforms]: Microsoft Learn, [YARP Request and Response Transforms](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms?view=aspnetcore-10.0)
[^yarp-websocket]: Microsoft Learn, [YARP Proxying WebSockets and SPDY](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/websockets?view=aspnetcore-10.0)
[^dotnet-single]: Microsoft Learn, [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
[^owasp-session]: OWASP Cheat Sheet Series, [Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)
[^ms-id-token-claims]: Microsoft Learn, [ID token claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference)（`iss` は token issuer / authorization server を示す URI）
[^easyauth-live-capture]: Icefire555, [Easy Auth Header Decoding – Quick Reference Guide](https://icefire555.com/easy-auth-header-decoding-quick-reference-guide/)（2025 年の App Service 応答例。`/.auth/me.user_claims` 内の `iss` を確認するための観測資料であり、公式契約ではない）
[^ms-container-token-store]: Microsoft Learn, [Use token store with Azure Container Apps authentication](https://learn.microsoft.com/en-us/azure/container-apps/token-store)（ACA token store は Azure Blob Storage と SAS URL を使用）
[^aca-auth-arm]: Microsoft Learn, [Microsoft.App/containerApps/authConfigs](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/containerapps/authconfigs)（ACA 認証 ARM スキーマ。`encryptionSettings`, IdP, token store）
[^appservice-auth-arm]: Microsoft Learn, [Microsoft.Web/sites/config-authsettingsV2](https://learn.microsoft.com/en-us/azure/templates/microsoft.web/sites/config-authsettingsv2)（App Service 認証 ARM スキーマ。`requireAuthentication`, `legacyMicrosoftAccount`, file system token store）
[^aca-auth-cli]: Microsoft Learn, [`az containerapp auth`](https://learn.microsoft.com/en-us/cli/azure/containerapp/auth)（ACA 認証 CLI と unauthenticated action、provider command）
[^webapp-auth-cli]: Microsoft Learn, [`az webapp auth`](https://learn.microsoft.com/en-us/cli/azure/webapp/auth)（App Service 認証 CLI と `Return404`, token refresh extension, legacy config command）
[^appservice-return404-spec-gap]: Azure REST API Specs issue [Web Apps: Add "Return404" value to unauthenticatedClientAction](https://github.com/Azure/azure-rest-api-specs/issues/20576)（App Service 実動作／CLI と ARM specification の差）
