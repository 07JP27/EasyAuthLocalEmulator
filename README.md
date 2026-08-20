# EasyAuth Local Emulator

## 技術スタック

- 製品/リポジトリ: EasyAuth Local Emulator (`EasyAuthLocalEmulator`)
- 予定している CLI: `easyauth-local`
- 目的: 実際の ID プロバイダーではなく偽のローカルプロファイルを使い、Azure App Service Easy Auth の公開された開発者向け仕様を再現するクロスプラットフォーム対応ローカルエミュレーター
- .NET 10
- ASP.NET Core minimal APIs
- YARP リバースプロキシ
- System.CommandLine
- ローカルのログイン/プロファイル編集画面に Razor Pages または最小限のサーバーレンダリング HTML
- System.Text.Json
- ASP.NET Core の cookie/session primitives、サーバー側で管理する不透明なセッション ID
- xUnit
- Playwright によるブラウザー E2E テスト
- 自己完結型の単一ファイル発行: `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`
- ビルド、テスト、リリースに GitHub Actions

## 使い方

- 予定している使い方
- 最初にアプリの開発サーバーを起動する。例: `http://localhost:5173`
- エミュレーターを起動する: `easyauth-local start http://localhost:5173`
- 新しいローカルプロキシ URL を使う。例: `http://127.0.0.1:4180`
- ブラウザーでは元のアップストリーム URL ではなくプロキシ URL を開く
- `/.auth/login/aad`: 偽の ID 編集画面を開く
- ID 項目: 表示名、UPN、ユーザー/オブジェクト ID、テナント ID、ロール、任意の `{typ,val}` クレーム
- `/.auth/me`: App Service 形式の ID データを返す想定
- 転送先リクエストのヘッダー: `X-MS-CLIENT-PRINCIPAL`, `X-MS-CLIENT-PRINCIPAL-ID`, `X-MS-CLIENT-PRINCIPAL-NAME`, `X-MS-CLIENT-PRINCIPAL-IDP`
- `/.auth/logout`: ローカルセッションを消去する想定
- オプションフラグ: `--port 4180`, `--open`, `--profile alice-admin`, `--config easyauth-local.json`, CI 向けの `--no-ui`
- 制限: ローカル開発専用。偽のトークンでは Microsoft Graph や Azure APIs を呼び出せない。最終確認は Azure 上で行う。非公開の cookie 内部仕様までは再現しない
