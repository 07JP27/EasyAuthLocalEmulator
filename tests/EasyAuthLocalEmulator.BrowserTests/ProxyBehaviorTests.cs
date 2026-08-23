using System.Text.Json;
using EasyAuthLocalEmulator.Auth;
using EasyAuthLocalEmulator.BrowserTests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace EasyAuthLocalEmulator.BrowserTests;

[Collection(BrowserTestGroup.Name)]
public sealed class ProxyBehaviorTests(BrowserFixture fixture) : PageTest
{
    [Fact]
    public async Task AnonymousProxySanitizesPlatformHeaders()
    {
        IAPIResponse response = await Page.APIRequest.GetAsync(
            new Uri(fixture.Emulator.BaseUri, "/echo?limit=2").AbsoluteUri,
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    [EasyAuthHeaderNames.Principal] = "spoofed",
                    [EasyAuthHeaderNames.PrincipalName] = "mallory@example.com",
                    ["X-MS-TOKEN-AAD-ACCESS-TOKEN"] = "secret",
                    ["X-ZUMO-AUTH"] = "token",
                    ["X-Forwarded-For"] = "203.0.113.10"
                }
            });

        Assert.Equal(200, response.Status);
        using JsonDocument echo = JsonDocument.Parse(await response.TextAsync());
        JsonElement root = echo.RootElement;
        JsonElement headers = root.GetProperty("headers");
        Assert.Equal("?limit=2", root.GetProperty("query").GetString());
        Assert.Equal(JsonValueKind.Null, headers.GetProperty(EasyAuthHeaderNames.Principal).ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            headers.GetProperty(EasyAuthHeaderNames.PrincipalName).ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            headers.GetProperty("X-MS-TOKEN-AAD-ACCESS-TOKEN").ValueKind);
        Assert.Equal(JsonValueKind.Null, headers.GetProperty("X-ZUMO-AUTH").ValueKind);
        Assert.Equal("127.0.0.1", headers.GetProperty("X-Forwarded-For").GetString());
        Assert.Equal("http", headers.GetProperty("X-Forwarded-Proto").GetString());
    }

    [Fact]
    public async Task AuthNamespaceIsReservedAndSecured()
    {
        IAPIResponse unknown = await Page.APIRequest.GetAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/not-a-route").AbsoluteUri);
        Assert.Equal(404, unknown.Status);
        Assert.DoesNotContain("UPSTREAM:", await unknown.TextAsync(), StringComparison.Ordinal);

        IAPIResponse login = await Page.APIRequest.GetAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);
        Assert.Contains(
            "no-store",
            login.Headers["cache-control"],
            StringComparison.Ordinal);
        Assert.Contains(
            "default-src 'none'",
            login.Headers["content-security-policy"],
            StringComparison.Ordinal);
        Assert.Equal("nosniff", login.Headers["x-content-type-options"]);
    }

    [Fact]
    public async Task RequestBodiesServerEventsAndWebSocketsPassThrough()
    {
        IAPIResponse post = await Page.APIRequest.PostAsync(
            new Uri(fixture.Emulator.BaseUri, "/echo?source=browser").AbsoluteUri,
            new APIRequestContextOptions
            {
                Data = "request-body",
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "text/plain"
                }
            });
        using JsonDocument echo = JsonDocument.Parse(await post.TextAsync());
        Assert.Equal("POST", echo.RootElement.GetProperty("method").GetString());
        Assert.Equal("request-body", echo.RootElement.GetProperty("body").GetString());
        Assert.Equal("?source=browser", echo.RootElement.GetProperty("query").GetString());

        IAPIResponse events = await Page.APIRequest.GetAsync(
            new Uri(fixture.Emulator.BaseUri, "/sse").AbsoluteUri);
        string eventBody = await events.TextAsync();
        Assert.Contains("data: first", eventBody, StringComparison.Ordinal);
        Assert.Contains("data: second", eventBody, StringComparison.Ordinal);

        await Page.GotoAsync(new Uri(fixture.Emulator.BaseUri, "/").AbsoluteUri);
        UriBuilder webSocketUri = new(new Uri(fixture.Emulator.BaseUri, "/ws"))
        {
            Scheme = "ws"
        };
        string echoed = await Page.EvaluateAsync<string>(
            """
            url => new Promise((resolve, reject) => {
              const socket = new WebSocket(url);
              socket.addEventListener("open", () => socket.send("websocket-message"));
              socket.addEventListener("message", event => resolve(event.data));
              socket.addEventListener("error", () => reject(new Error("websocket failed")));
            })
            """,
            webSocketUri.Uri.AbsoluteUri);
        Assert.Equal("websocket-message", echoed);
    }
}
