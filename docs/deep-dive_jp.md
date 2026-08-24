# EasyAuth Local Emulator Deep Dive

<p align="center"><a href="deep-dive.md">English</a> | 日本語</p>

この文書は、EasyAuth Local Emulator の設計や実装を変更するコントリビューター向けの技術ガイドです。

- 利用方法は [README](../README_jp.md)
- 全設定項目は [JSON Schema](../schemas/easyauth-local.schema.json)
- 実現性調査、既存ツール比較、出典は [調査資料](../research/azure-app-service-easy-auth-azure-static.md)

## 設計目標

このプロジェクトが再現するのは、Azure App Service Easy Auth と Azure Container Apps 組み込み認証の公開された開発者向け HTTP 契約です。

- `/.auth/*` の認証ルート
- `X-MS-CLIENT-PRINCIPAL*` ヘッダー
- `/.auth/me` の ID 情報
- ブラウザーのセッション Cookie
- 認証プロキシとしての通常リクエスト転送

Azure 内部の実装や、実 IdP が発行するトークンまでは再現しません。

platform は起動引数だけで選びます。

```console
easyauth start http://localhost:5173 \
  --platform app-service

easyauth start http://localhost:5173 \
  --platform container-apps
```

省略時は `app-service` です。模擬プロファイルは両 platform で共有できるため、JSON 設定には platform を保存しません。

## アーキテクチャ

[利用者向けのリクエスト経路図](easy-auth-local-emulator-overview.drawio.svg)

実行ファイルは .NET 10 の ASP.NET Core アプリです。CLI、Razor Pages、認証ルート、セッション、YARP プロキシを一つのプロセスに収めています。

| 構成要素 | 主な場所 | 責務 |
|---|---|---|
| CLI | `src/EasyAuthLocalEmulator/Cli` | 引数解析、設定読込、起動、終了 |
| 設定 | `src/EasyAuthLocalEmulator/Configuration` | JSON 読込、厳格な検証、CLI オプションとの統合 |
| IdP / プリンシパル | `src/EasyAuthLocalEmulator/Auth` | IdP 定義、プロファイル、クレーム、ヘッダー、`/.auth/me` |
| 認証画面 | `src/EasyAuthLocalEmulator/Pages/Auth` | 模擬ログイン、ログアウト完了 |
| プロキシ | `src/EasyAuthLocalEmulator/Proxy` | 通常リクエスト、ストリーム、WebSocket の転送 |
| サンプル | `samples/EasyAuthLocalEmulator.SampleApp` | 手動確認と E2E で共用する転送先アプリ |
| UnitTests | `tests/EasyAuthLocalEmulator.UnitTests` | データ契約、設定、セッション、変換処理 |
| BrowserTests | `tests/EasyAuthLocalEmulator.BrowserTests` | 実プロセスを使う Chromium / WebKit E2E |

### リクエスト処理順

1. `/.auth/*` を認証ルートとして先に照合します。
2. 未知の `/.auth/*` は `404` とし、転送先へ渡しません。
3. その他のリクエストは YARP の直接転送機能へ渡します。
4. クライアントが付けた Easy Auth ヘッダーと転送ヘッダーを削除します。
5. セッションが有効ならプリンシパルを組み立て、4つの `X-MS-CLIENT-PRINCIPAL*` を付けます。
6. パス、クエリ、HTTP メソッド、本文を保ったまま転送先へ送ります。

## App Service と Azure Container Apps の差分

両 platform は同じ認証システムを使います。現在のエミュレーターで利用者が観測できる差分は、platform 表示と既定のログアウト完了 URL です。

| 項目 | App Service | Azure Container Apps | エミュレーター |
|---|---|---|---|
| 既定のログアウト完了 URL | `/.auth/logout/complete` | `/.auth/logout/done` | platform で切り替え |
| `Return404` | あり | なし | 認可設定が未実装のため文書化のみ |
| `globalValidation.requireAuthentication` | ARM にあり | ARM になし | 未実装 |
| ファイルシステム token store | あり | なし | 実トークン非対応 |
| Blob token store | あり | あり | 実トークン非対応 |
| 明示的な `encryptionSettings` | なし | あり | 単一プロセスのため非対応 |
| file-based auth config | 公式文書と ARM 項目あり | ARM 項目なし、CLI 引数あり | 独自プロファイル JSON とは無関係 |
| Apple | 概念文書、ARM、CLI にあり | ARM と CLI にあるが概念文書にない | 両モードで利用可能 |
| GitHub のカスタムサインイン／サインアウト | 非対応と明記 | 同等の制限記載なし | 現エミュレーターでは対象外、不確定事項として記録 |
| Protected Resource Metadata | preview で提供 | ACA では確認できない | 両モードとも対象外 |
| 既定セッション8時間 / 72時間猶予 | 公式記載あり | ACA 固有の記載なし | 両モードで8時間 |
| `/.auth/me` 完全スキーマ | 未公開 | ACA 固有説明なし | 両モードで共通 |

### 実行時に分けないもの

次は両モードで同じです。

- IdP とログイン URL
- `X-MS-CLIENT-PRINCIPAL*`
- principal JSON
- `/.auth/me`
- 模擬プロファイルとセッション
- YARP プロキシ

Cookie 名、ACA のセッション既定時間、`/.auth/me` の完全な応答差は公式文書だけでは確定できません。証拠のない platform 分岐は作らず、現在の互換方針を共有します。

### ACA 固有の注意

- SPA のクライアント側ルーターが `/.auth/login/*` を横取りすると、認証 sidecar へ届きません。
- 複数 replica の署名・暗号化キーは ACA の `encryptionSettings` で明示できますが、単一プロセスの本エミュレーターでは再現しません。
- ACA の Apple 対応は ARM スキーマと Azure CLI で確認できますが、概念文書の IdP 一覧には掲載されていません。
- App Service 文書は GitHub のカスタムサインイン／サインアウトを非対応としていますが、ACA 文書に同じ制限はありません。実動作差か文書差かは未確認です。
- App Service の preview 機能である Protected Resource Metadata (`/.well-known/oauth-protected-resource`) は ACA で確認できず、本エミュレーターでは両モードとも対象外です。

## 認証ルート

| ルート | 現在の動作 |
|---|---|
| `GET /.auth/login/<provider>` | IdP ごとの模擬ログイン画面 |
| `POST /.auth/login/<provider>` | 偽造防止トークンと入力を検証し、セッションを作成 |
| `GET /.auth/me` | 認証済みは ID 情報の配列、未認証は `[]` |
| `GET /.auth/logout` | セッションを破棄してリダイレクト |
| `GET /.auth/refresh` | 有効なら期限を延長して `200`、無効なら `401` |
| その他の `/.auth/*` | `404` |

ログインとログアウトのリダイレクト先は、プロキシ内の絶対パスだけを許可します。`//example.com` 形式の URL、外部オリジン、バックスラッシュ、制御文字、二重デコードで意味が変わる値を拒否します。

`post_logout_redirect_uri` がない場合は、App Service モードで `/.auth/logout/complete`、Azure Container Apps モードで `/.auth/logout/done` へ移動します。サンプルアプリは明示的に `post_logout_redirect_uri=/` を指定します。

完了画面は両 URL で表示できます。既定の移動先だけを platform で切り替えます。

## プリンシパルとヘッダー

`X-MS-CLIENT-PRINCIPAL` は、次の JSON を UTF-8 で直列化し、標準 Base64 で符号化した値です。

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

クレームは辞書に変換せず配列のまま保持します。同じ種類のロール、グループ、その他のクレームを複数含められるようにするためです。

同じ `PrincipalBuilder` から次を生成します。

- `X-MS-CLIENT-PRINCIPAL`
- `X-MS-CLIENT-PRINCIPAL-ID`
- `X-MS-CLIENT-PRINCIPAL-NAME`
- `X-MS-CLIENT-PRINCIPAL-IDP`
- `/.auth/me[0]`

`auth_typ`、`X-MS-CLIENT-PRINCIPAL-IDP`、`/.auth/me[0].provider_name` は、プロファイルの `authenticationType` を共有します。

生成したプリンシパル JSON は 64 KiB を上限とします。

## `/.auth/me`

認証済みでは、現在の実装は次の形の配列を返します。

```json
[
  {
    "provider_name": "aad",
    "user_id": "11111111-1111-1111-1111-111111111111",
    "user_claims": [
      { "typ": "name", "val": "Alice Example" },
      { "typ": "iss", "val": "https://login.microsoftonline.com/tenant/v2.0" }
    ],
    "access_token": null,
    "authentication_token": null,
    "expires_on": null,
    "id_token": null,
    "refresh_token": null
  }
]
```

実トークンを発行しないため、トークン関連項目は `null` です。

発行者 (`issuer`) はトップレベルの `issuer` 項目ではなく、`user_claims` とプリンシパルの `claims` にある `iss` クレームとして表現します。`issuer` を空文字にすると `iss` を生成しません。

`/.auth/me` の完全な公式スキーマと未認証時の挙動は公開されていません。未認証時の `200 []` は、このエミュレーターが採用している互換方針です。根拠と不確定事項は [調査資料](../research/azure-app-service-easy-auth-azure-static.md) を参照してください。

## IdP 互換性

| IdP | ログイン URL のキー | 既定の `authenticationType` | 発行者の初期値 |
|---|---|---|---|
| Microsoft Entra ID | `aad` | `aad` | `https://login.microsoftonline.com/{tenantId}/v2.0` |
| Facebook | `facebook` | `facebook` | なし |
| Google | `google` | `google` | `https://accounts.google.com` |
| X | `x` | `x` | なし |
| GitHub | `github` | `github` | なし |
| Apple | `apple` | `apple` | `https://appleid.apple.com` |

App Service の X は、ログイン URL では `x`、設定やトークンヘッダーでは `twitter` を使うため、公開面に不一致があります。実ヘッダーの値は公式文書だけでは確定できないため、このエミュレーターは `x` を既定とし、プロファイルの `authenticationType` で `twitter` へ変更できるようにしています。

非 AAD の完全なクレーム対応規則も公開されていません。`IdentityProviderRegistry` は保守的な既定値を持ちますが、次の設定で変更または無効化できます。

- `authenticationType`
- `nameClaimType`
- `roleClaimType`
- `claimMappings.displayName`
- `claimMappings.userName`
- `claimMappings.userId`
- `claimMappings.tenantId`

空文字または `null` の設定項目からは、対応する補助クレームを生成しません。

Microsoft Entra ID では `userId` と `tenantId` に GUID を要求します。その他の IdP の `userId` は文字列です。

`provider` を省略した既存プロファイルは `aad` として扱います。旧 `upn` は `userName` の別名として受理しますが、両方を同時に指定するとエラーになります。

任意の OpenID Connect プロバイダーは現在の対象外です。

## 設定

設定ファイルは UTF-8 JSON です。未知のプロパティ、重複したプロパティ、型の不一致を黙って無視せず、起動時にエラーにします。ファイルサイズは 1 MiB を上限とします。

完全な制約は [JSON Schema](../schemas/easyauth-local.schema.json) が表します。

```json
{
  "$schema": "https://raw.githubusercontent.com/07JP27/EasyAuthLocalEmulator/main/schemas/easyauth-local.schema.json",
  "sessionLifetime": "08:00:00",
  "profiles": {
    "x-reader": {
      "provider": "x",
      "authenticationType": "twitter",
      "displayName": "Local User",
      "userName": "local_user",
      "userId": "1000000001",
      "issuer": "",
      "roles": ["Reader"],
      "claims": []
    }
  }
}
```

### 発行者 (`issuer`) の正規化

- `issuer` が未指定なら IdP の既定値を使います。
- `issuer` が空文字なら `iss` を出しません。
- `issuer` が未指定で `claims` に `iss` が1件あれば、専用項目へ正規化します。
- `issuer` と `claims[].typ = "iss"` を同時に指定するとエラーにします。
- `iss` が複数ある場合もエラーにします。

### `--no-ui`

`--no-ui` は `--config` と `--profile` を必須とし、選択プロファイルをプロセス全体の認証状態として使います。

- 起動時から全クライアントが同じ ID で認証済みになります。
- ログアウトはプロセス全体を未認証にします。
- 選択プロファイルと同じ IdP のログイン URL だけが再有効化できます。
- クライアントごとのセッション分離はありません。

## セッション

通常モードでは、Cookie `AppServiceAuthSession` に 256 ビット CSPRNG で生成した不透明なセッション ID だけを格納します。ユーザー情報やプリンシパルは Cookie に入れず、サーバー側メモリに保持します。

Cookie 属性:

- `HttpOnly`
- `SameSite=Lax`
- `Path=/`
- 明示的な有効期限
- HTTP のローカル待ち受けなので `Secure` は付けない

既定の有効期間は8時間です。`/.auth/refresh` で期限を延長します。期限切れはリクエスト時と定期処理の両方で削除します。プロセスを終了するとすべてのセッションが失われます。

8時間とその後の72時間の猶予は App Service 文書に記載されていますが、ACA 文書では同じ数値を独立確認できません。エミュレーターは両モードで8時間を使い、72時間の猶予は再現しません。

## セキュリティ境界

### 待ち受けと転送先

- 待ち受けは `127.0.0.1` 固定です。
- 転送先は `localhost`、`127.0.0.1`、`::1` など、このコンピューター自身を指すアドレスだけを許可します。
- URL のユーザー情報、クエリ、フラグメントは転送先指定に使えません。
- 転送先の TLS 検証は無効化しません。

### ヘッダー偽装の防止

転送前に次を削除し、エミュレーターが生成した値だけを使います。

- `X-MS-CLIENT-PRINCIPAL*`
- `X-MS-TOKEN-*`
- `X-ZUMO-AUTH`
- `Forwarded`
- `X-Forwarded-*`

その後、実際の接続情報から `X-Forwarded-For`、`X-Forwarded-Host`、`X-Forwarded-Proto` を再生成します。

### フォームとリダイレクト

- ログイン POST は ASP.NET Core の偽造防止機能を使います。
- ログイン／ログアウト後の移動先はローカル絶対パスだけです。
- `//example.com` 形式の URL、外部 URL、バックスラッシュ、制御文字、二重デコードで意味が変わる値を拒否します。
- 認証 UI には CSP、`Cache-Control: no-store`、`X-Content-Type-Options: nosniff` などを付けます。

### ログ

Cookie、トークン、プリンシパル全文を通常ログへ出しません。起動ログには転送先、プロキシ、ログイン URL、プロファイル名、UI モードだけを表示します。

## プロキシ

YARP の直接転送機能を使い、通常のリクエストを指定した転送先へ送ります。

- HTTP メソッド、パス、クエリ、リクエスト本文を保持
- レスポンス本文をストリーミング
- サーバー送信イベント (SSE)
- WebSocket への切り替え
- 転送先のリダイレクトを自動追従しない
- 転送先の Cookie をプロキシ自身の Cookie 保存領域に保存しない
- 自動展開をしない
- 接続タイムアウト10秒
- 無通信タイムアウト10分

転送開始前の失敗は、タイムアウト系を `504`、その他を `502` として返します。レスポンス開始後の失敗やクライアント切断では、新しいエラー本文を追加しません。

HTTP/2 と gRPC は YARP / Kestrel が処理できる場合がありますが、このプロジェクトの互換保証対象ではありません。

## テスト

### ソースから起動

転送先アプリを起動したうえで、次のコマンドを実行します。

```console
dotnet run --project src/EasyAuthLocalEmulator -- \
  start http://localhost:5173
```

付属サンプルを転送先にする場合:

```console
dotnet run --project samples/EasyAuthLocalEmulator.SampleApp -- \
  --urls http://127.0.0.1:5173
```

### UnitTests

主な対象:

- CLI オプションと設定
- JSON の未知項目・重複項目
- IdP 定義と後方互換
- 発行者とクレーム対応規則
- プリンシパル JSON と Base64
- ヘッダー生成と偽装除去
- セッションの発行、期限、更新、ログアウト
- リダイレクト検証
- 認証 UI のセキュリティヘッダー

```console
dotnet test tests/EasyAuthLocalEmulator.UnitTests/EasyAuthLocalEmulator.UnitTests.csproj \
  --configuration Release --no-build --no-restore
```

### BrowserTests

BrowserTests は、サンプルアプリと `easyauth` を別プロセスで動的ポートへ起動します。手動確認と同じサンプルを使うことで、UI だけでなく実際のプロキシ経路を検証します。

主な対象:

- ログイン画面、入力検証、偽造防止
- 6 IdP とプロバイダーごとのプロファイル
- 発行者と X の `twitter` 上書き
- プリンシパルと `/.auth/me` の一致
- クライアント指定ヘッダーの除去
- ログアウト、更新、no-UI
- HTTP メソッド、本文、クエリ
- SSE、WebSocket
- 転送先エラー、ポート競合
- モバイル表示と主要な UI 状態

初回だけブラウザーをインストールします。

```console
pwsh tests/EasyAuthLocalEmulator.BrowserTests/bin/Release/net10.0/playwright.ps1 \
  install chromium webkit
```

macOS / Linux:

```console
BROWSER=chromium dotnet test \
  tests/EasyAuthLocalEmulator.BrowserTests/EasyAuthLocalEmulator.BrowserTests.csproj \
  --configuration Release --no-build --no-restore

BROWSER=webkit dotnet test \
  tests/EasyAuthLocalEmulator.BrowserTests/EasyAuthLocalEmulator.BrowserTests.csproj \
  --configuration Release --no-build --no-restore
```

PowerShell では実行前に `$env:BROWSER = "chromium"` または `"webkit"` を設定します。

### 子プロセス

`tests/EasyAuthLocalEmulator.BrowserTests/Fixtures` の責務:

| クラス | 責務 |
|---|---|
| `ChildProcess` | 起動、標準出力・標準エラー、起動確認、タイムアウト、プロセスツリー終了 |
| `SampleAppProcess` | サンプルアプリを動的ポートで起動 |
| `EmulatorProcess` | 一時設定を作成してエミュレーターを起動 |
| `BrowserFixture` | サンプル、エミュレーターの順に起動し、逆順に終了 |

## ビルドとリリース

すべてのプロジェクトは .NET 10 を使用し、nullable と warnings-as-errors を有効にしています。

CI は Windows と macOS で次を実行します。

- パッケージの復元
- Release ビルド
- UnitTests
- Chromium BrowserTests
- WebKit BrowserTests

`v*` タグで、次の自己完結単一ファイルを作ります。

- `win-x64`
- `win-arm64`
- `osx-x64`
- `osx-arm64`

リリースワークフローは RID ごとのアーカイブと SHA-256 チェックサムを作り、GitHub Release へ添付します。サンプルアプリは配布アーカイブに含めません。

## ソース構成

| パス | 内容 |
|---|---|
| `src/EasyAuthLocalEmulator/Cli` | コマンド定義と起動 |
| `src/EasyAuthLocalEmulator/Configuration` | 設定 DTO、読込、検証 |
| `src/EasyAuthLocalEmulator/Auth` | IdP、プロファイル、プリンシパル、セッション、認証ルート |
| `src/EasyAuthLocalEmulator/Proxy` | YARP とリクエスト変換 |
| `src/EasyAuthLocalEmulator/Pages/Auth` | ログイン／ログアウト UI |
| `samples/EasyAuthLocalEmulator.SampleApp` | 手動確認／E2E 共用サンプル |
| `tests/EasyAuthLocalEmulator.UnitTests` | UnitTests |
| `tests/EasyAuthLocalEmulator.BrowserTests` | Playwright E2E |
| `schemas` | JSON Schema |
| `.github/workflows` | CI / リリース |

## 非目標と不確定事項

現在の対象外:

- 実 IdP への接続
- 実アクセストークン、ID トークン、更新トークン
- `X-MS-TOKEN-*` の生成
- 任意の OpenID Connect プロバイダー
- Azure の非公開 Cookie 内部形式
- Windows IIS のプロセス内統合
- App Service の認可設定全体
- HTTP/2 / gRPC の互換保証

公式文書だけでは、非 AAD の完全なクレーム対応規則、X の実際の `auth_typ`、`/.auth/me` の完全なスキーマ、未認証時のすべての構成差を確定できません。現在の選択と証拠レベルは [調査資料](../research/azure-app-service-easy-auth-azure-static.md) に記録しています。
