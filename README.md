# EasyAuth Local Emulator

<p align="center">English | <a href="README_jp.md">日本語</a></p>

A development proxy that locally reproduces "Azure App Service Easy Auth" and "Azure Container Apps built-in authentication" without a real identity provider.

- Run any local web app behind the authentication proxy.
- Create mock users from the browser or from a configuration file.
- Verify the `/.auth/me` response and forwarded headers your web app uses, before deploying to Azure.
- The emulator handles `/.auth/*` itself, and forwards all other requests to your local app with the required Easy Auth headers attached.

![EasyAuth Local Emulator request path](docs/overview.png)


## Quick start

Prerequisites:

- Windows or macOS
- The upstream app listens on `localhost`, `127.0.0.1`, or `::1`

To use a release build, extract the archive that matches your environment from [GitHub Releases](https://github.com/07JP27/EasyAuthLocalEmulator/releases) and place `easyauth` somewhere you can run it from.

1. Start the web app you want to target. Here we use `http://localhost:5173`.

2. Start the emulator in another terminal.

   ```console
   easyauth start http://localhost:5173
   ```

   To reproduce Azure Container Apps:

   ```console
   easyauth start http://localhost:5173 \
     --platform container-apps
   ```

3. Open `http://127.0.0.1:4180`.

4. Set up a mock user via a login URL such as `http://127.0.0.1:4180/.auth/login/aad`.

If port `4180` is already in use, specify a different port with `--port`.

## Try it with the sample

If you don't have an upstream app, you can run the bundled sample instead. Running the sample requires the .NET 10 SDK.

```console
dotnet run --project samples/EasyAuthLocalEmulator.SampleApp -- \
  --urls http://127.0.0.1:5173
```

Then start the emulator in another terminal.

```console
easyauth start http://127.0.0.1:5173
```

Opening `http://127.0.0.1:4180` shows the authentication state, the four Easy Auth headers, and the decoded principal. The sample also includes diagnostic endpoints for HTTP, SSE, and WebSocket.

## Commands

```console
easyauth start <upstream-url> [options]
```

| Option | Description |
|---|---|
| `--platform <platform>` | `app-service` or `container-apps`. Defaults to `app-service` |
| `--port <port>` | The proxy port. Defaults to `4180` |
| `--open` | Opens the proxy URL in the default browser after startup |
| `--config <path>` | JSON configuration file |
| `--profile <name>` | Profile within the configuration file |
| `--no-ui` | Uses the selected profile without any UI |

The emulator only listens on `127.0.0.1`, and only allows upstream addresses that point to this same computer.
The platform is chosen on the CLI each time it starts, and is never saved in the JSON configuration.

## Supported platforms

| `--platform` | Reproduces | Default logout-complete URL |
|---|---|---|
| `app-service` | Azure App Service Easy Auth | `/.auth/logout/complete` |
| `container-apps` | Azure Container Apps authentication | `/.auth/logout/done` |

The login screen and the logout-complete screen show the currently selected platform.

> [!NOTE]
> In Azure Container Apps mode, make sure your SPA's client-side router doesn't intercept `/.auth/login/*`. This route must reach the server-side authentication feature.

## Profiles

Mock users you use repeatedly can be saved as JSON.

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

For automated tests, you can enable the same profile without any UI.

```console
easyauth start http://localhost:5173 \
  --config easyauth-local.json \
  --profile alice-admin \
  --no-ui
```

Existing configurations that omit `provider` are treated as `aad`, and the legacy `upn` field is still accepted. See the [JSON Schema](schemas/easyauth-local.schema.json) for all configuration options.

## Supported identity providers

| Identity provider | Login URL |
|---|---|
| Microsoft Entra ID | `/.auth/login/aad` |
| Facebook | `/.auth/login/facebook` |
| Google | `/.auth/login/google` |
| X | `/.auth/login/x` |
| GitHub | `/.auth/login/github` |
| Apple | `/.auth/login/apple` |

## What your app sees

### Authentication routes

| Route | Purpose |
|---|---|
| `GET /.auth/login/<provider>` | Log in as a mock user |
| `GET /.auth/me` | Get the current identity information |
| `GET /.auth/logout` | Log out |
| `GET /.auth/refresh` | Extend the local session's expiration |

### Forwarded headers

| Header | Content |
|---|---|
| `X-MS-CLIENT-PRINCIPAL` | Base64-encoded JSON containing the claims |
| `X-MS-CLIENT-PRINCIPAL-ID` | User ID |
| `X-MS-CLIENT-PRINCIPAL-NAME` | Username or email address |
| `X-MS-CLIENT-PRINCIPAL-IDP` | Identity provider name |

Any of these headers sent by the client are stripped before forwarding and replaced with the values the emulator generates. The default session lifetime is 8 hours, and sessions are lost when the emulator exits.

## Limitations

- This is for local development only. Do not expose it in production.
- It does not generate real tokens, so it does not reproduce external APIs, conditional access, or actual identity provider authentication.
- Arbitrary OpenID Connect providers, the full set of platform authorization settings, IIS integration, and HTTP/2 / gRPC compatibility are not guaranteed.
- Perform final verification on Azure.

## Learn more

- [Deep Dive](docs/deep-dive.md): architecture, compatibility, security, and development/testing
- [Feasibility research](research/azure-app-service-easy-auth-azure-static.md): research process, comparison with existing tools, and sources
- [JSON Schema](schemas/easyauth-local.schema.json): configuration options and constraints
