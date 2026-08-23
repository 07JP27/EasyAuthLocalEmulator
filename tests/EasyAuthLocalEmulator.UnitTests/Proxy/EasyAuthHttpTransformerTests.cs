using System.Net;
using EasyAuthLocalEmulator.Auth;
using EasyAuthLocalEmulator.Configuration;
using EasyAuthLocalEmulator.Proxy;
using Microsoft.AspNetCore.Http;

namespace EasyAuthLocalEmulator.UnitTests.Proxy;

public sealed class EasyAuthHttpTransformerTests
{
    [Fact]
    public async Task RemovesSpoofedHeadersAndInjectsResolvedIdentity()
    {
        (EasyAuthHttpTransformer transformer, LocalAuthenticationService authentication) =
            CreateTransformer();
        DefaultHttpContext signInContext = new();
        authentication.SignIn(signInContext, TestData.CreateProfile());
        string setCookie = signInContext.Response.Headers.SetCookie.ToString();
        DefaultHttpContext context = CreateProxyContext(setCookie);
        context.Request.Headers[EasyAuthHeaderNames.Principal] = "spoofed";
        context.Request.Headers[EasyAuthHeaderNames.PrincipalName] = "mallory@example.com";
        context.Request.Headers["X-MS-TOKEN-AAD-ACCESS-TOKEN"] = "secret";
        context.Request.Headers["X-ZUMO-AUTH"] = "token";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
        using HttpRequestMessage proxyRequest = new();

        await transformer.TransformRequestAsync(
            context,
            proxyRequest,
            "http://localhost:5173/",
            CancellationToken.None);

        Assert.Equal(
            "alice@example.com",
            Assert.Single(proxyRequest.Headers.GetValues(
                EasyAuthHeaderNames.PrincipalName)));
        Assert.NotEqual(
            "spoofed",
            Assert.Single(proxyRequest.Headers.GetValues(EasyAuthHeaderNames.Principal)));
        Assert.False(proxyRequest.Headers.Contains("X-MS-TOKEN-AAD-ACCESS-TOKEN"));
        Assert.False(proxyRequest.Headers.Contains("X-ZUMO-AUTH"));
        Assert.Equal(
            IPAddress.Loopback.ToString(),
            Assert.Single(proxyRequest.Headers.GetValues("X-Forwarded-For")));
        Assert.Equal(
            "127.0.0.1:4180",
            Assert.Single(proxyRequest.Headers.GetValues("X-Forwarded-Host")));
        Assert.Equal(
            "http",
            Assert.Single(proxyRequest.Headers.GetValues("X-Forwarded-Proto")));
        Assert.Equal("http://localhost:5173/api/items?limit=2", proxyRequest.RequestUri?.ToString());
        Assert.Null(proxyRequest.Headers.Host);
    }

    [Fact]
    public async Task AnonymousRequestHasNoIdentityHeaders()
    {
        (EasyAuthHttpTransformer transformer, _) = CreateTransformer();
        DefaultHttpContext context = CreateProxyContext(setCookie: null);
        context.Request.Headers[EasyAuthHeaderNames.PrincipalId] = "spoofed";
        using HttpRequestMessage proxyRequest = new();

        await transformer.TransformRequestAsync(
            context,
            proxyRequest,
            "http://localhost:5173/",
            CancellationToken.None);

        Assert.False(proxyRequest.Headers.Contains(EasyAuthHeaderNames.Principal));
        Assert.False(proxyRequest.Headers.Contains(EasyAuthHeaderNames.PrincipalId));
        Assert.False(proxyRequest.Headers.Contains(EasyAuthHeaderNames.PrincipalName));
        Assert.False(proxyRequest.Headers.Contains(EasyAuthHeaderNames.IdentityProvider));
    }

    private static (EasyAuthHttpTransformer, LocalAuthenticationService) CreateTransformer()
    {
        UserProfile profile = TestData.CreateProfile();
        EmulatorOptions options = new(
            new Uri("http://localhost:5173/"),
            4180,
            OpenBrowser: false,
            new Dictionary<string, UserProfile>(StringComparer.Ordinal)
            {
                ["alice"] = profile
            },
            "alice",
            profile,
            NoUi: false,
            TimeSpan.FromHours(8));
        ManualTimeProvider timeProvider = new(
            new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero));
        InMemorySessionStore store = new(timeProvider, options.SessionLifetime);
        LocalAuthenticationService authentication = new(options, store, timeProvider);
        return (
            new EasyAuthHttpTransformer(options, authentication, new PrincipalBuilder()),
            authentication);
    }

    private static DefaultHttpContext CreateProxyContext(string? setCookie)
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("127.0.0.1", 4180);
        context.Request.Path = "/api/items";
        context.Request.QueryString = new QueryString("?limit=2");

        if (setCookie is not null)
        {
            context.Request.Headers.Cookie = setCookie.Split(
                ';',
                2,
                StringSplitOptions.TrimEntries)[0];
        }

        return context;
    }
}
