using EasyAuthLocalEmulator.Auth;
using Microsoft.AspNetCore.Http;

namespace EasyAuthLocalEmulator.UnitTests.Auth;

public sealed class AuthResponseSecurityMiddlewareTests
{
    [Fact]
    public async Task AddsSecurityHeadersToAuthResponses()
    {
        AuthResponseSecurityMiddleware middleware = new(_ => Task.CompletedTask);
        DefaultHttpContext context = new();
        context.Request.Path = "/.auth/login/aad";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Contains(
            "default-src 'none'",
            context.Response.Headers.ContentSecurityPolicy.ToString(),
            StringComparison.Ordinal);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
    }

    [Fact]
    public async Task DoesNotAddAuthHeadersToProxiedResponses()
    {
        AuthResponseSecurityMiddleware middleware = new(_ => Task.CompletedTask);
        DefaultHttpContext context = new();
        context.Request.Path = "/api/items";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
    }
}
