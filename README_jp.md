# EasyAuth Local Emulator

<p align="center"><a href="README.md">English</a> | 日本語</p>

「Azure App Service Easy Auth」 と 「Azure Container Apps の組み込み認証」を、実際の ID プロバイダーなしでローカル再現する開発用プロキシです。

- 任意のローカル Web アプリを認証プロキシの後ろで動かせます。
- ブラウザーまたは設定ファイルから模擬ユーザーを作れます。
- Webアプリで使用する `/.auth/me` レスポンスと転送ヘッダーを Azure へデプロイする前に確認できます。
- `/.auth/*` はエミュレーターが処理し、それ以外のリクエストは必要な Easy Auth ヘッダーを付けてローカルアプリへ転送します。

![EasyAuth Local Emulator のリクエスト経路](docs/overview.png)

![EasyAuth Local Emulator のログイン画面](docs/login.png)

## クイックスタート

前提:

- Windows または macOS
- 転送先アプリは `localhost`、`127.0.0.1`、`::1` のいずれかで待ち受ける

### 1. リリースをダウンロード

[GitHub Releases](https://github.com/07JP27/EasyAuthLocalEmulator/releases) から環境に合うアーカイブと、対応する `.sha256` ファイルをダウンロードします。

| 使用環境 | ファイル名内の識別子 |
|---|---|
| Windows（Intel / AMD） | `win-x64` |
| Windows on Arm | `win-arm64` |
| Mac（Apple silicon） | `osx-arm64` |
| Mac（Intel） | `osx-x64` |

### 2. ダウンロードを検証

OS のセキュリティ警告を解除する前に、アーカイブがリリースで公開されたチェックサムと一致することを確認します。次の例は `v0.1.0` です。ダウンロードしたファイルに合わせて version と RID を置き換えてください。

macOS:

ダウンロードしたアーカイブとチェックサムがあるフォルダをターミナルで開きます。`cd ` と入力し、Finder からフォルダをターミナルへドラッグして Return キーを押す方法でも開けます。

```console
shasum -a 256 -c easyauth-v0.1.0-osx-arm64.tar.gz.sha256
```

結果の末尾が `OK` であることを確認し、Finder で `.tar.gz` ファイルをダブルクリックして展開します。

Windows PowerShell:

ダウンロードしたアーカイブとチェックサムがあるフォルダをエクスプローラーで開き、アドレスバーに `powershell` と入力して Enter キーを押します。

```powershell
$archive = ".\easyauth-v0.1.0-win-x64.zip"
$expected = (Get-Content "$archive.sha256").Split()[0]
$actual = (Get-FileHash $archive -Algorithm SHA256).Hash
$actual.ToLowerInvariant() -eq $expected.ToLowerInvariant()
```

結果が `True` であることを確認してから、アーカイブを展開します。

### 3. CLI をインストール

#### macOS

展開したフォルダをターミナルで開きます。

1. `cd ` と入力します。末尾に半角スペースを含めます。
2. Finder から展開したフォルダをターミナルへドラッグします。
3. Return キーを押します。
4. 現在のフォルダに実行ファイルがあることを確認します。

```console
pwd
ls -l ./easyauth
```

`./easyauth` は「現在のフォルダにある `easyauth`」を意味します。`No such file or directory` と表示された場合は、上の手順に戻って展開したフォルダを開いてください。

通常 `PATH` に含まれている `/usr/local/bin` へ CLI をインストールします。

```console
sudo mkdir -p /usr/local/bin
sudo install -m 755 ./easyauth /usr/local/bin/easyauth
```

`sudo` が `Password:` を求めたら、Mac のログインパスワードを入力して Return キーを押します。入力中は文字が表示されません。

`easyauth --version` でインストールを確認します。現在の macOS リリースは Apple Developer ID で署名されておらず、Apple の公証も受けていないため、この初回実行が Gatekeeper にブロックされる場合があります。この警告自体はマルウェア検出ではありませんが、このリポジトリの公式 GitHub Release からダウンロードし、チェックサムを確認した場合だけ解除してください。

次のいずれかを実行します。

- **システム設定:** `easyauth --version` を一度実行して警告を表示します。次に **システム設定 → プライバシーとセキュリティ** で `easyauth` の **このまま開く** を選び、確認後にもう一度コマンドを実行します。
- **ターミナル:**

  ```console
  sudo xattr -d com.apple.quarantine /usr/local/bin/easyauth
  easyauth --version
  ```

[Apple の Gatekeeper に関する案内](https://support.apple.com/guide/mac-help/open-a-mac-app-from-an-unknown-developer-mh40616/mac)も参照してください。

#### Windows

エクスプローラーで展開したフォルダを開き、アドレスバーに `powershell` と入力して Enter キーを押します。PowerShell が正しいフォルダで開いたことを確認します。

```powershell
Get-Location
Get-ChildItem .\easyauth.exe
```

現在の Windows リリースは Authenticode 署名されていません。実行ファイルをエクスプローラーから開くと、Microsoft Defender SmartScreen が未認識のアプリとして警告する場合があります。セキュリティポリシーによってダウンロードしたファイルがブロックされる場合もあります。このリポジトリの公式 GitHub Release からダウンロードし、チェックサムを確認した場合だけ解除してください。

次のいずれかを実行します。

- **Windows の画面操作:** `easyauth.exe` を右クリックして **プロパティ** を開き、**許可する** を選択して適用します。実行ファイルをダブルクリックし、Microsoft Defender SmartScreen に **Windows によって PC が保護されました** と表示された場合は、**詳細情報 → 実行** を選びます。
- **PowerShell:**

  ```powershell
  Unblock-File -LiteralPath .\easyauth.exe
  ```

`Unblock-File` はエクスプローラーの **許可する** と同じ操作を行います。[Microsoft の `Unblock-File` ドキュメント](https://learn.microsoft.com/powershell/module/microsoft.powershell.utility/unblock-file)も参照してください。
[アプリとブラウザー コントロールおよび SmartScreen に関する Microsoft の案内](https://support.microsoft.com/windows/app-browser-control-in-the-windows-security-app-7b2fd298-bf1d-4e39-97d4-043e94fd5d96)も参照してください。

Windows ユーザーの領域へ CLI をインストールし、ユーザー `PATH` へ追加します。

```powershell
$installDir = "$env:LOCALAPPDATA\Programs\EasyAuthLocalEmulator"
New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item .\easyauth.exe $installDir -Force
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (($userPath -split ";") -notcontains $installDir) {
  $newPath = if ([string]::IsNullOrEmpty($userPath)) { $installDir } else { "$userPath;$installDir" }
  [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
}
```

新しい PowerShell を開き、インストールを確認します。

```powershell
easyauth --version
```

### 4. エミュレーターを起動

Easy Auth の後ろで動かすローカル Web アプリを起動し、その URL を確認します。続けて別のターミナルでエミュレーターを起動します。

```console
easyauth start http://localhost:5173 --open
```

`http://localhost:5173` は実際のアプリの URL に置き換えてください。Azure Container Apps を再現する場合は、末尾に `--platform container-apps` を追加します。

`--open` を指定すると、プロキシ `http://127.0.0.1:4180` が開きます。`http://127.0.0.1:4180/.auth/login/aad` などのログイン URL で模擬ユーザーを設定します。

エミュレーターの使用中は、必ずプロキシ経由でアプリへアクセスしてください。元のアプリ URL を直接開くと Easy Auth を経由しません。ポート `4180` が使用中の場合は、`--port` で別のポートを指定してください。

インストールせずに試す場合は、展開したフォルダで macOS なら `./easyauth start ...`、Windows PowerShell なら `.\easyauth.exe start ...` を実行します。この場合も同じ OS 警告が出ることがあります。macOS のターミナルで解除する場合は展開したファイルを対象に `xattr -d com.apple.quarantine ./easyauth`、Windows では実行前に上記の `Unblock-File` コマンドを使用します。

## サンプルで試す

転送先アプリがない場合は、付属のサンプルを起動できます。サンプルの実行には .NET 10 SDK が必要です。

```console
dotnet run --project samples/EasyAuthLocalEmulator.SampleApp -- \
  --urls http://127.0.0.1:5173
```

続けて、別のターミナルでエミュレーターを起動します。

```console
easyauth start http://127.0.0.1:5173
```

`http://127.0.0.1:4180` を開くと、認証状態、4つの Easy Auth ヘッダー、復号済みプリンシパルを確認できます。サンプルには HTTP、SSE、WebSocket の診断用エンドポイントも含まれます。

## コマンド

```console
easyauth start <upstream-url> [options]
```

| オプション | 説明 |
|---|---|
| `--platform <platform>` | `app-service` または `container-apps`。既定値は `app-service` |
| `--port <port>` | プロキシのポート。既定値は `4180` |
| `--open` | 起動後にプロキシ URL を既定のブラウザーで開く |
| `--config <path>` | JSON 設定ファイル |
| `--profile <name>` | 設定ファイル内のプロファイル |
| `--no-ui` | 選択プロファイルを画面なしで使用する |

エミュレーターは `127.0.0.1` だけで待ち受け、転送先もこのコンピューター自身を指すアドレスだけを許可します。
platform は起動するたびに CLI で選び、JSON 設定には保存しません。

## 対応プラットフォーム

| `--platform` | 再現対象 | 既定のログアウト完了 URL |
|---|---|---|
| `app-service` | Azure App Service Easy Auth | `/.auth/logout/complete` |
| `container-apps` | Azure Container Apps authentication | `/.auth/logout/done` |

ログイン画面とログアウト完了画面には、現在選択している platform が表示されます。

> [!NOTE]
> Azure Container Apps モードでは、SPA のクライアント側ルーターが `/.auth/login/*` を横取りしないようにしてください。このルートはサーバー側の認証機能へ到達する必要があります。

## プロファイル

繰り返し使う模擬ユーザーは JSON に保存できます。

```json
{
  "$schema": "https://raw.githubusercontent.com/07JP27/EasyAuthLocalEmulator/main/schemas/easyauth-local.schema.json",
  "profiles": {
    "alice-admin": {
      "provider": "aad",
      "displayName": "Alice Example",
      "userName": "alice@example.com",
      "userId": "11111111-1111-1111-1111-111111111111",
      "tenantId": "22222222-2222-2222-2222-222222222222",
      "roles": ["Admin"],
      "claims": [
        { "typ": "department", "val": "Engineering" }
      ]
    }
  }
}
```

```console
easyauth start http://localhost:5173 \
  --config easyauth-local.json \
  --profile alice-admin
```

自動テストでは同じプロファイルを画面なしで有効化できます。

```console
easyauth start http://localhost:5173 \
  --config easyauth-local.json \
  --profile alice-admin \
  --no-ui
```

`provider` を省略した既存設定は `aad` として扱い、旧 `upn` も利用できます。全設定項目は [JSON Schema](schemas/easyauth-local.schema.json) を参照してください。

## 対応する ID プロバイダー

| ID プロバイダー | ログイン URL |
|---|---|
| Microsoft Entra ID | `/.auth/login/aad` |
| Facebook | `/.auth/login/facebook` |
| Google | `/.auth/login/google` |
| X | `/.auth/login/x` |
| GitHub | `/.auth/login/github` |
| Apple | `/.auth/login/apple` |

## アプリから見えるもの

### 認証ルート

| ルート | 用途 |
|---|---|
| `GET /.auth/login/<provider>` | 模擬ユーザーでログイン |
| `GET /.auth/me` | 現在の ID 情報を取得 |
| `GET /.auth/logout` | ログアウト |
| `GET /.auth/refresh` | ローカルセッションの有効期限を延長 |

### 転送ヘッダー

| ヘッダー | 内容 |
|---|---|
| `X-MS-CLIENT-PRINCIPAL` | クレームを含む Base64 符号化 JSON |
| `X-MS-CLIENT-PRINCIPAL-ID` | ユーザー ID |
| `X-MS-CLIENT-PRINCIPAL-NAME` | ユーザー名またはメールアドレス |
| `X-MS-CLIENT-PRINCIPAL-IDP` | ID プロバイダー名 |

クライアントが送った同名ヘッダーは転送前に削除し、エミュレーターが生成した値に置き換えます。セッションの既定有効期間は8時間で、エミュレーターを終了すると失われます。

## 制限事項

- ローカル開発専用です。本番環境へ公開しないでください。
- 実トークンを生成しないため、外部 API、条件付きアクセス、実際の ID プロバイダー認証は再現しません。
- 任意の OpenID Connect プロバイダー、platform の認可設定全体、IIS 統合、HTTP/2・gRPC の互換性は保証しません。
- 最終確認は Azure 上で実施してください。

## さらに詳しく

- [Deep Dive](docs/deep-dive_jp.md): アーキテクチャ、互換性、セキュリティ、開発・テスト
- [実現性調査](research/azure-app-service-easy-auth-azure-static.md): 調査過程、既存ツール比較、出典
- [JSON Schema](schemas/easyauth-local.schema.json): 設定項目と制約
