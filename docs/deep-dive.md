# EasyAuth Local Emulator Deep Dive

<p align="center">English | <a href="deep-dive_jp.md">日本語</a></p>

This document is a technical guide for contributors who change the design or implementation of EasyAuth Local Emulator.

- For usage, see the [README](../README.md)
- For all configuration options, see the [JSON Schema](../schemas/easyauth-local.schema.json)
- For the feasibility research, comparison with existing tools, and sources, see the [research document](../research/azure-app-service-easy-auth-azure-static.md)

## Design goals

This project reproduces the published, developer-facing HTTP contract of Azure App Service Easy Auth and Azure Container Apps built-in authentication.

- The `/.auth/*` authentication routes
- The `X-MS-CLIENT-PRINCIPAL*` headers
- The identity information in `/.auth/me`
- The browser session cookie
- Regular request forwarding as an authentication proxy

It does not reproduce Azure's internal implementation or tokens issued by a real identity provider.

The platform is selected only via startup arguments.

```console
easyauth start http://localhost:5173 \
  --platform app-service

easyauth start http://localhost:5173 \
  --platform container-apps
```

It defaults to `app-service`. Because mock profiles can be shared across both platforms, the platform is not saved in the JSON configuration.

## Architecture

[Request path diagram for users](easy-auth-local-emulator-overview.drawio.svg)

The executable is a .NET 10 ASP.NET Core app. The CLI, Razor Pages, authentication routes, session, and YARP proxy are all contained in a single process.

| Component | Primary location | Responsibility |
|---|---|---|
| CLI | `src/EasyAuthLocalEmulator/Cli` | Argument parsing, configuration loading, startup, shutdown |
| Configuration | `src/EasyAuthLocalEmulator/Configuration` | JSON loading, strict validation, integration with CLI options |
| IdP / principal | `src/EasyAuthLocalEmulator/Auth` | IdP definitions, profiles, claims, headers, `/.auth/me` |
| Authentication screens | `src/EasyAuthLocalEmulator/Pages/Auth` | Mock login, logout complete |
| Proxy | `src/EasyAuthLocalEmulator/Proxy` | Forwarding of regular requests, streams, and WebSocket |
| Sample | `samples/EasyAuthLocalEmulator.SampleApp` | Upstream app shared by manual verification and E2E |
| UnitTests | `tests/EasyAuthLocalEmulator.UnitTests` | Data contracts, configuration, session, transformation logic |
| BrowserTests | `tests/EasyAuthLocalEmulator.BrowserTests` | Chromium / WebKit E2E using real processes |

### Request processing order

1. Match `/.auth/*` as an authentication route first.
2. Return `404` for unknown `/.auth/*` routes, without passing them to the upstream.
3. Pass all other requests to YARP's direct forwarding feature.
4. Strip any Easy Auth headers and forwarded headers attached by the client.
5. If the session is valid, build the principal and attach the four `X-MS-CLIENT-PRINCIPAL*` headers.
6. Forward the request to the upstream, preserving the path, query, HTTP method, and body.

## Differences between App Service and Azure Container Apps

Both platforms use the same authentication system. The differences currently observable by users of the emulator are the platform display and the default logout-complete URL.

| Item | App Service | Azure Container Apps | Emulator |
|---|---|---|---|
| Default logout-complete URL | `/.auth/logout/complete` | `/.auth/logout/done` | Switched by platform |
| `Return404` | Present | Absent | Documented only, since authorization configuration is not implemented |
| `globalValidation.requireAuthentication` | In ARM | Not in ARM | Not implemented |
| Filesystem token store | Present | Absent | Real tokens not supported |
| Blob token store | Present | Present | Real tokens not supported |
| Explicit `encryptionSettings` | Absent | Present | Not supported, since it's a single process |
| File-based auth config | In official docs and ARM | In CLI args, not in ARM | Unrelated to the emulator's own profile JSON |
| Apple | In conceptual docs, ARM, and CLI | In ARM and CLI, not in conceptual docs | Available in both modes |
| GitHub custom sign-in/sign-out | Explicitly unsupported | No equivalent restriction documented | Out of scope for the current emulator; recorded as an open question |
| Protected Resource Metadata | Available in preview | Not confirmed for ACA | Out of scope in both modes |
| Default 8-hour session / 72-hour grace period | Documented officially | Not documented specifically for ACA | 8 hours in both modes |
| Full `/.auth/me` schema | Undocumented | No ACA-specific description | Common to both modes |

### What is not differentiated at runtime

The following are the same in both modes.

- IdPs and login URLs
- `X-MS-CLIENT-PRINCIPAL*`
- Principal JSON
- `/.auth/me`
- Mock profiles and sessions
- The YARP proxy

The cookie name, ACA's default session duration, and the exact differences in the `/.auth/me` response cannot be confirmed from official documentation alone. We avoid platform branching that lacks evidence, and share the current compatibility policy here.

### ACA-specific notes

- If a SPA's client-side router intercepts `/.auth/login/*`, requests never reach the authentication sidecar.
- Signing and encryption keys across multiple replicas can be made explicit via ACA's `encryptionSettings`, but this emulator does not reproduce that since it is a single process.
- ACA's Apple support can be confirmed via the ARM schema and Azure CLI, but it is not listed in the conceptual documentation's IdP list.
- App Service docs state that GitHub custom sign-in/sign-out is unsupported, but the ACA docs do not state the same restriction. It is unconfirmed whether this is an actual behavioral difference or a documentation gap.
- App Service's preview feature, Protected Resource Metadata (`/.well-known/oauth-protected-resource`), cannot be confirmed for ACA, and this emulator excludes it in both modes.

## Authentication routes

| Route | Current behavior |
|---|---|
| `GET /.auth/login/<provider>` | Mock login screen per IdP |
| `POST /.auth/login/<provider>` | Validates the anti-forgery token and input, and creates a session |
| `GET /.auth/me` | An array of identity information when authenticated, `[]` when not |
| `GET /.auth/logout` | Destroys the session and redirects |
| `GET /.auth/refresh` | `200` with an extended expiration if valid, `401` if invalid |
| Other `/.auth/*` | `404` |

The redirect destinations for login and logout only allow absolute paths inside the proxy. URLs in the `//example.com` form, external origins, backslashes, control characters, and values whose meaning changes under double decoding are all rejected.

If `post_logout_redirect_uri` is absent, the browser is redirected to `/.auth/logout/complete` in App Service mode and `/.auth/logout/done` in Azure Container Apps mode. The sample app explicitly specifies `post_logout_redirect_uri=/`.

The complete screen can be shown at either URL. Only the default destination is switched by platform.

## Principal and headers

`X-MS-CLIENT-PRINCIPAL` is the following JSON, serialized as UTF-8 and encoded with standard Base64.

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

Claims are kept as an array rather than converted to a dictionary, so that multiple roles, groups, or other claims of the same type can be included.

The same `PrincipalBuilder` generates all of the following.

- `X-MS-CLIENT-PRINCIPAL`
- `X-MS-CLIENT-PRINCIPAL-ID`
- `X-MS-CLIENT-PRINCIPAL-NAME`
- `X-MS-CLIENT-PRINCIPAL-IDP`
- `/.auth/me[0]`

`auth_typ`, `X-MS-CLIENT-PRINCIPAL-IDP`, and `/.auth/me[0].provider_name` all share the profile's `authenticationType`.

The generated principal JSON is capped at 64 KiB.

## `/.auth/me`

When authenticated, the current implementation returns an array of the following shape.

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

Because it doesn't issue real tokens, the token-related fields are `null`.

The issuer is expressed as an `iss` claim in `user_claims` and in the principal's `claims`, rather than as a top-level `issuer` field. Setting `issuer` to an empty string prevents `iss` from being generated.

The complete official schema for `/.auth/me`, and the behavior when unauthenticated, are not publicly documented. The `200 []` response when unauthenticated is the compatibility policy adopted by this emulator. See the [research document](../research/azure-app-service-easy-auth-azure-static.md) for the rationale and open questions.

## IdP compatibility

| IdP | Login URL key | Default `authenticationType` | Default issuer |
|---|---|---|---|
| Microsoft Entra ID | `aad` | `aad` | `https://login.microsoftonline.com/{tenantId}/v2.0` |
| Facebook | `facebook` | `facebook` | None |
| Google | `google` | `google` | `https://accounts.google.com` |
| X | `x` | `x` | None |
| GitHub | `github` | `github` | None |
| Apple | `apple` | `apple` | `https://appleid.apple.com` |

App Service uses `x` in the login URL for X, but `twitter` in configuration and token headers, so there is a public inconsistency. Since the actual header value cannot be confirmed from official documentation alone, this emulator defaults to `x` and allows it to be changed to `twitter` via the profile's `authenticationType`.

The complete claim-mapping rules for non-AAD providers are also not publicly documented. `IdentityProviderRegistry` has conservative defaults, but they can be changed or disabled via the following settings.

- `authenticationType`
- `nameClaimType`
- `roleClaimType`
- `claimMappings.displayName`
- `claimMappings.userName`
- `claimMappings.userId`
- `claimMappings.tenantId`

Configuration fields left empty or `null` do not generate the corresponding auxiliary claim.

Microsoft Entra ID requires `userId` and `tenantId` to be GUIDs. For other IdPs, `userId` is a string.

Existing profiles that omit `provider` are treated as `aad`. The legacy `upn` field is accepted as an alias for `userName`, but specifying both at the same time results in an error.

Arbitrary OpenID Connect providers are currently out of scope.

## Configuration

The configuration file is UTF-8 JSON. Unknown properties, duplicate properties, and type mismatches are not silently ignored — they cause a startup error. The file size is capped at 1 MiB.

The complete set of constraints is expressed by the [JSON Schema](../schemas/easyauth-local.schema.json).

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

### Issuer (`issuer`) normalization

- If `issuer` is unspecified, the IdP's default value is used.
- If `issuer` is an empty string, no `iss` is emitted.
- If `issuer` is unspecified and `claims` contains exactly one `iss`, it is normalized into the dedicated field.
- Specifying both `issuer` and `claims[].typ = "iss"` at the same time results in an error.
- Multiple `iss` entries also result in an error.

### `--no-ui`

`--no-ui` requires `--config` and `--profile`, and uses the selected profile as the authentication state for the entire process.

- All clients are authenticated as the same identity from startup.
- Logging out unauthenticates the entire process.
- Only the login URL for the same IdP as the selected profile can re-enable it.
- There is no per-client session isolation.

## Session

In normal mode, the `AppServiceAuthSession` cookie stores only an opaque session ID generated with a 256-bit CSPRNG. User information and the principal are not stored in the cookie; they are kept in server-side memory.

Cookie attributes:

- `HttpOnly`
- `SameSite=Lax`
- `Path=/`
- An explicit expiration
- No `Secure`, since it listens over local HTTP

The default lifetime is 8 hours. `/.auth/refresh` extends the expiration. Expired sessions are removed both on request and via periodic cleanup. Ending the process loses all sessions.

The 8-hour lifetime and the subsequent 72-hour grace period are documented for App Service, but the same figures cannot be independently confirmed in ACA documentation. The emulator uses 8 hours in both modes, and does not reproduce the 72-hour grace period.

## Security boundaries

### Listening and upstream addresses

- The emulator always listens on `127.0.0.1`.
- Only upstream addresses that point to this same computer, such as `localhost`, `127.0.0.1`, and `::1`, are allowed.
- Userinfo, query, and fragment parts of the URL cannot be used to specify the upstream.
- TLS validation for the upstream is never disabled.

### Preventing header spoofing

Before forwarding, the following are stripped, and only values generated by the emulator are used.

- `X-MS-CLIENT-PRINCIPAL*`
- `X-MS-TOKEN-*`
- `X-ZUMO-AUTH`
- `Forwarded`
- `X-Forwarded-*`

Afterward, `X-Forwarded-For`, `X-Forwarded-Host`, and `X-Forwarded-Proto` are regenerated from the actual connection information.

### Forms and redirects

- Login POSTs use ASP.NET Core's anti-forgery feature.
- Post-login/logout destinations are local absolute paths only.
- URLs in the `//example.com` form, external URLs, backslashes, control characters, and values whose meaning changes under double decoding are all rejected.
- The authentication UI is served with CSP, `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, and similar headers.

### Logging

Cookies, tokens, and the full principal are never written to normal logs. Startup logs show only the upstream, the proxy, login URLs, profile names, and the UI mode.

## Proxy

Regular requests are forwarded to the specified upstream using YARP's direct forwarding feature.

- Preserves the HTTP method, path, query, and request body
- Streams the response body
- Server-sent events (SSE)
- Switches to WebSocket
- Does not automatically follow upstream redirects
- Does not store the upstream's cookies in the proxy's own cookie storage
- No automatic decompression
- 10-second connection timeout
- 10-minute idle timeout

Failures before forwarding begins return `504` for timeout-related failures and `502` for others. Failures after the response has started, or client disconnects, do not add a new error body.

HTTP/2 and gRPC may work if YARP / Kestrel can handle them, but they are not within this project's compatibility guarantees.

## Testing

### Running from source

After starting the upstream app, run the following command.

```console
dotnet run --project src/EasyAuthLocalEmulator -- \
  start http://localhost:5173
```

To use the bundled sample as the upstream:

```console
dotnet run --project samples/EasyAuthLocalEmulator.SampleApp -- \
  --urls http://127.0.0.1:5173
```

### UnitTests

Main coverage:

- CLI options and configuration
- Unknown/duplicate properties in JSON
- IdP definitions and backward compatibility
- Issuer and claim-mapping rules
- Principal JSON and Base64
- Header generation and spoofing removal
- Session issuance, expiration, refresh, and logout
- Redirect validation
- Security headers for the authentication UI

```console
dotnet test tests/EasyAuthLocalEmulator.UnitTests/EasyAuthLocalEmulator.UnitTests.csproj \
  --configuration Release --no-build --no-restore
```

### BrowserTests

BrowserTests starts the sample app and `easyauth` as separate processes on dynamic ports. By reusing the same sample as manual verification, it exercises the actual proxy path, not just the UI.

Main coverage:

- Login screen, input validation, anti-forgery
- The 6 IdPs and per-provider profiles
- Issuer and the `twitter` override for X
- Consistency between the principal and `/.auth/me`
- Removal of client-supplied headers
- Logout, refresh, no-UI
- HTTP methods, body, query
- SSE, WebSocket
- Upstream errors, port conflicts
- Mobile display and key UI states

Browsers only need to be installed the first time.

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

On PowerShell, set `$env:BROWSER = "chromium"` or `"webkit"` before running.

### Child processes

Responsibilities of `tests/EasyAuthLocalEmulator.BrowserTests/Fixtures`:

| Class | Responsibility |
|---|---|
| `ChildProcess` | Startup, stdout/stderr, startup confirmation, timeout, process-tree termination |
| `SampleAppProcess` | Starts the sample app on a dynamic port |
| `EmulatorProcess` | Creates a temporary configuration and starts the emulator |
| `BrowserFixture` | Starts the sample and emulator in order, and stops them in reverse order |

## Build and release

All projects target .NET 10, with nullable reference types and warnings-as-errors enabled.

CI runs the following on Windows and macOS.

- Package restore
- Release build
- UnitTests
- Chromium BrowserTests
- WebKit BrowserTests

Releases are started manually from **Actions → Release → Run workflow** on `main`.
Enter a version without the `v` prefix, such as `1.0.0`; the workflow creates the corresponding `v1.0.0` tag and GitHub Release.
SemVer prerelease versions such as `1.0.0-beta.1` are published as GitHub prereleases.

The following self-contained single-file binaries are built.

- `win-x64`
- `win-arm64`
- `osx-x64`
- `osx-arm64`

The release workflow creates an archive and a SHA-256 checksum per RID, generates release notes, and attaches all artifacts to the GitHub Release. The sample app is not included in the distributed archives.

## Source layout

| Path | Content |
|---|---|
| `src/EasyAuthLocalEmulator/Cli` | Command definitions and startup |
| `src/EasyAuthLocalEmulator/Configuration` | Configuration DTOs, loading, validation |
| `src/EasyAuthLocalEmulator/Auth` | IdP, profiles, principal, session, authentication routes |
| `src/EasyAuthLocalEmulator/Proxy` | YARP and request transformation |
| `src/EasyAuthLocalEmulator/Pages/Auth` | Login/logout UI |
| `samples/EasyAuthLocalEmulator.SampleApp` | Sample shared by manual verification / E2E |
| `tests/EasyAuthLocalEmulator.UnitTests` | UnitTests |
| `tests/EasyAuthLocalEmulator.BrowserTests` | Playwright E2E |
| `schemas` | JSON Schema |
| `.github/workflows` | CI / release |

## Non-goals and open questions

Currently out of scope:

- Connecting to a real IdP
- Real access tokens, ID tokens, and refresh tokens
- Generating `X-MS-TOKEN-*`
- Arbitrary OpenID Connect providers
- Azure's undocumented internal cookie format
- In-process integration with Windows IIS
- The full set of App Service authorization settings
- HTTP/2 / gRPC compatibility guarantees

The official documentation alone cannot confirm the complete claim-mapping rules for non-AAD providers, the actual `auth_typ` for X, the full `/.auth/me` schema, or every configuration difference when unauthenticated. Current choices and their evidence level are recorded in the [research document](../research/azure-app-service-easy-auth-azure-static.md).
