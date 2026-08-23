using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyAuthLocalEmulator.SampleApp;

internal static class SamplePage
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true
    };

    internal static string Render(HttpContext context)
    {
        string principal = GetHeader(context, EasyAuthHeaders.Principal);
        string principalId = GetHeader(context, EasyAuthHeaders.PrincipalId);
        string principalName = GetHeader(context, EasyAuthHeaders.PrincipalName);
        string identityProvider = GetHeader(context, EasyAuthHeaders.IdentityProvider);
        bool isAuthenticated = principal.Length > 0;
        string decodedPrincipal = DecodePrincipal(principal);
        string authenticationControls = RenderAuthenticationControls(
            isAuthenticated,
            principalName,
            identityProvider);

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Identity diagnostics · Easy Auth Local emulator sample app</title>
              <style>
                :root {
                  color-scheme: light;
                  font-family: ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                  color: #20242a;
                  background: #fff;
                  --app-bar: #252c35;
                  --border: #d9dde2;
                  --border-strong: #b7bdc5;
                  --muted: #5e6670;
                  --surface-subtle: #f5f6f7;
                  --primary: #0067b8;
                  --primary-hover: #005a9e;
                  --danger: #a4262c;
                }
                * { box-sizing: border-box; }
                body { margin: 0; min-height: 100vh; line-height: 1.5; }
                a { color: var(--primary); }
                code, pre { font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace; }
                .app-bar { min-height: 56px; color: #fff; background: var(--app-bar); }
                .app-bar-inner {
                  width: min(1080px, calc(100% - 40px));
                  min-height: 56px;
                  margin: 0 auto;
                  display: flex;
                  align-items: center;
                  justify-content: space-between;
                  gap: 24px;
                }
                .app-identity { min-width: 0; display: flex; align-items: baseline; gap: 12px; }
                .app-name { font-weight: 650; white-space: nowrap; }
                .auth-controls { display: flex; align-items: center; gap: 14px; }
                .account { min-width: 0; display: grid; justify-items: end; line-height: 1.2; }
                .account-name { max-width: 280px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: .875rem; font-weight: 600; }
                .account-provider { color: #bfc7d2; font-size: .75rem; }
                .bar-action,
                .sign-in-menu summary {
                  min-height: 36px;
                  display: inline-flex;
                  align-items: center;
                  justify-content: center;
                  border: 1px solid #7f8996;
                  border-radius: 3px;
                  padding: 6px 12px;
                  color: #fff;
                  background: transparent;
                  font-size: .875rem;
                  font-weight: 600;
                  text-decoration: none;
                  cursor: pointer;
                }
                .bar-action:hover,
                .sign-in-menu summary:hover { background: #343d48; }
                .sign-in-menu { position: relative; }
                .sign-in-menu summary { list-style: none; }
                .sign-in-menu summary::-webkit-details-marker { display: none; }
                .sign-in-menu nav {
                  position: absolute;
                  z-index: 10;
                  top: calc(100% + 6px);
                  right: 0;
                  width: 220px;
                  border: 1px solid var(--border-strong);
                  background: #fff;
                  box-shadow: 0 5px 14px rgba(0, 0, 0, .18);
                }
                .sign-in-menu nav a {
                  display: block;
                  padding: 9px 12px;
                  color: #20242a;
                  text-decoration: none;
                  font-size: .875rem;
                }
                .sign-in-menu nav a:hover { background: var(--surface-subtle); }
                main { width: min(1040px, calc(100% - 40px)); margin: 34px auto 64px; }
                .page-header { margin-bottom: 28px; }
                .page-header h1 { margin: 0; font-size: 1.75rem; font-weight: 600; letter-spacing: -.02em; }
                .workspace-section { padding: 26px 0; border-top: 1px solid var(--border); }
                .workspace-section h2 { margin: 0 0 14px; font-size: 1rem; font-weight: 650; }
                .data-table { margin: 0; border-top: 1px solid var(--border); }
                .data-row {
                  display: grid;
                  grid-template-columns: 260px minmax(0, 1fr);
                  min-height: 44px;
                  border-bottom: 1px solid var(--border);
                }
                .data-row dt,
                .data-row dd { margin: 0; padding: 10px 12px; }
                .data-row dt {
                  background: var(--surface-subtle);
                  font-size: .8rem;
                  font-weight: 600;
                  overflow-wrap: anywhere;
                }
                .data-row dd { min-width: 0; overflow-wrap: anywhere; }
                .data-row code { font-size: .8rem; }
                .status-value { display: inline-flex; align-items: center; gap: 8px; }
                .status-dot { width: 8px; height: 8px; border-radius: 50%; background: #8a9199; }
                .status-dot.authenticated { background: #107c41; }
                .missing { color: var(--muted); font-style: italic; }
                .code-panel {
                  max-height: 260px;
                  margin: 0;
                  overflow: auto;
                  border: 1px solid var(--border-strong);
                  background: var(--surface-subtle);
                  padding: 14px;
                  color: #20242a;
                  font-size: .8rem;
                  line-height: 1.55;
                  white-space: pre-wrap;
                  overflow-wrap: anywhere;
                }
                .endpoint-table { width: 100%; border-collapse: collapse; }
                .endpoint-table th,
                .endpoint-table td {
                  padding: 10px 12px;
                  border-top: 1px solid var(--border);
                  text-align: left;
                  vertical-align: top;
                }
                .endpoint-table th { width: 220px; background: var(--surface-subtle); font-size: .8rem; font-weight: 600; }
                .endpoint-table td { color: var(--muted); }
                .endpoint-table code { color: var(--primary); font-size: .8rem; }
                .bar-action:focus-visible,
                .sign-in-menu summary:focus-visible,
                a:focus-visible {
                  outline: 3px solid #fff;
                  outline-offset: 2px;
                }
                main a:focus-visible { outline-color: var(--primary); }
                .sign-in-menu nav a:focus-visible { outline-color: var(--primary); }
                @media (max-width: 680px) {
                  .app-bar-inner { width: calc(100% - 28px); padding: 10px 0; align-items: flex-start; flex-direction: column; gap: 8px; }
                  .auth-controls { width: 100%; justify-content: space-between; }
                  .account { justify-items: start; }
                  .sign-in-menu nav { right: 0; left: auto; width: min(220px, calc(100vw - 28px)); }
                  main { width: calc(100% - 28px); margin-top: 26px; }
                  .data-row { grid-template-columns: 1fr; }
                  .data-row dt { padding-bottom: 3px; background: transparent; border: 0; }
                  .data-row dd { padding-top: 3px; }
                  .endpoint-table,
                  .endpoint-table tbody,
                  .endpoint-table tr,
                  .endpoint-table th,
                  .endpoint-table td { display: block; width: 100%; }
                  .endpoint-table tr { border-top: 1px solid var(--border); padding: 9px 0; }
                  .endpoint-table th,
                  .endpoint-table td { border: 0; padding: 2px 0; background: transparent; }
                }
              </style>
            </head>
            <body>
              <header class="app-bar">
                <div class="app-bar-inner">
                  <div class="app-identity">
                    <span class="app-name">Easy Auth Local emulator sample app</span>
                  </div>
                  {{authenticationControls}}
                </div>
              </header>

              <main>
                <header class="page-header">
                  <h1>Identity diagnostics</h1>
                </header>

                <section class="workspace-section" aria-labelledby="session-heading">
                  <h2 id="session-heading">Session</h2>
                  <dl class="data-table session-table">
                    <div class="data-row">
                      <dt>Status</dt>
                      <dd class="status-value">
                        <span class="status-dot {{(isAuthenticated ? "authenticated" : null)}}" aria-hidden="true"></span>
                        <span id="authentication-status">{{(isAuthenticated ? "Authenticated" : "Anonymous")}}</span>
                      </dd>
                    </div>
                    <div class="data-row"><dt>Provider</dt><dd id="session-provider">{{RenderValue(identityProvider)}}</dd></div>
                    <div class="data-row"><dt>User name</dt><dd id="session-user-name">{{RenderValue(principalName)}}</dd></div>
                    <div class="data-row"><dt>User ID</dt><dd id="session-user-id">{{RenderValue(principalId)}}</dd></div>
                  </dl>
                </section>

                <section class="workspace-section" aria-labelledby="headers-heading">
                  <h2 id="headers-heading">Forwarded headers</h2>
                  <dl class="data-table">
                    <div class="data-row"><dt><code>{{EasyAuthHeaders.PrincipalName}}</code></dt><dd id="principal-name">{{RenderValue(principalName)}}</dd></div>
                    <div class="data-row"><dt><code>{{EasyAuthHeaders.PrincipalId}}</code></dt><dd id="principal-id">{{RenderValue(principalId)}}</dd></div>
                    <div class="data-row"><dt><code>{{EasyAuthHeaders.IdentityProvider}}</code></dt><dd id="identity-provider">{{RenderValue(identityProvider)}}</dd></div>
                    <div class="data-row">
                      <dt><code>{{EasyAuthHeaders.Principal}}</code></dt>
                      <dd><pre id="encoded-principal" class="code-panel">{{EncodeOrPlaceholder(principal)}}</pre></dd>
                    </div>
                  </dl>
                </section>

                <section class="workspace-section" aria-labelledby="principal-heading">
                  <h2 id="principal-heading">Decoded principal</h2>
                  <pre id="decoded-principal" class="code-panel">{{Encode(decodedPrincipal)}}</pre>
                </section>

                <section class="workspace-section" aria-labelledby="endpoints-heading">
                  <h2 id="endpoints-heading">Diagnostic endpoints</h2>
                  <table class="endpoint-table">
                    <tbody>
                      <tr><th scope="row"><a href="/.auth/me"><code>/.auth/me</code></a></th><td>Current Easy Auth identity</td></tr>
                      <tr><th scope="row"><a href="/.auth/refresh"><code>/.auth/refresh</code></a></th><td>Extend the local session</td></tr>
                      <tr><th scope="row"><a href="/echo"><code>/echo</code></a></th><td>Inspect request data and forwarded headers</td></tr>
                      <tr><th scope="row"><a href="/sse"><code>/sse</code></a></th><td>Test streaming events</td></tr>
                      <tr><th scope="row"><code>/ws</code></th><td>Test WebSocket proxying</td></tr>
                    </tbody>
                  </table>
                </section>
              </main>
            </body>
            </html>
            """;
    }

    private static string DecodePrincipal(string encodedPrincipal)
    {
        if (encodedPrincipal.Length == 0)
        {
            return "No client principal was provided.";
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(encodedPrincipal);
            string json = StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, IndentedJson);
        }
        catch (FormatException)
        {
            return "The client principal is not valid Base64.";
        }
        catch (DecoderFallbackException)
        {
            return "The client principal does not contain valid UTF-8.";
        }
        catch (JsonException)
        {
            return "The decoded client principal is not valid JSON.";
        }
    }

    private static string GetHeader(HttpContext context, string name)
    {
        return context.Request.Headers.TryGetValue(
            name,
            out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : string.Empty;
    }

    private static string Encode(string value)
    {
        return HtmlEncoder.Default.Encode(value);
    }

    private static string EncodeOrPlaceholder(string value)
    {
        return value.Length == 0 ? "Not present" : Encode(value);
    }

    private static string RenderValue(string value)
    {
        return value.Length == 0
            ? """<span class="missing">Not present</span>"""
            : Encode(value);
    }

    private static string RenderAuthenticationControls(
        bool isAuthenticated,
        string principalName,
        string identityProvider)
    {
        if (isAuthenticated)
        {
            string name = principalName.Length == 0 ? "Authenticated user" : Encode(principalName);
            string provider = identityProvider.Length == 0 ? "Unknown provider" : Encode(identityProvider);

            return $$"""
                <div class="auth-controls">
                  <div class="account">
                    <span class="account-name">{{name}}</span>
                    <span class="account-provider">{{provider}}</span>
                  </div>
                  <a class="bar-action" href="/.auth/logout?post_logout_redirect_uri=/">Sign out</a>
                </div>
                """;
        }

        return """
            <div class="auth-controls">
              <span class="account-provider">Anonymous</span>
              <details class="sign-in-menu">
                <summary>Sign in</summary>
                <nav aria-label="Identity providers">
                  <a href="/.auth/login/aad?post_login_redirect_uri=/">Microsoft Entra ID</a>
                  <a href="/.auth/login/facebook?post_login_redirect_uri=/">Facebook</a>
                  <a href="/.auth/login/google?post_login_redirect_uri=/">Google</a>
                  <a href="/.auth/login/x?post_login_redirect_uri=/">X</a>
                  <a href="/.auth/login/github?post_login_redirect_uri=/">GitHub</a>
                  <a href="/.auth/login/apple?post_login_redirect_uri=/">Apple</a>
                </nav>
              </details>
            </div>
            """;
    }
}
