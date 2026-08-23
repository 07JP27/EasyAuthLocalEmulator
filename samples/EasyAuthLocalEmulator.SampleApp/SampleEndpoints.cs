using System.Net.WebSockets;

namespace EasyAuthLocalEmulator.SampleApp;

internal static class SampleEndpoints
{
    private static readonly string[] AllMethods =
        ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD", "TRACE"];

    internal static void MapSampleEndpoints(this WebApplication application)
    {
        application.UseWebSockets();
        application.MapGet("/", (HttpContext context) =>
            Results.Content(SamplePage.Render(context), "text/html; charset=utf-8"));
        application.MapMethods("/echo", AllMethods, EchoAsync);
        application.MapGet("/sse", SendServerEventsAsync);
        application.Map("/ws", EchoWebSocketAsync);
        application.MapMethods(
            "/{**catchAll}",
            AllMethods,
            (HttpContext context) =>
                Results.Text($"UPSTREAM:{context.Request.Path}", "text/plain"));
    }

    private static async Task EchoAsync(HttpContext context)
    {
        using StreamReader reader = new(context.Request.Body, leaveOpen: true);
        string body = await reader.ReadToEndAsync(context.RequestAborted);
        Dictionary<string, string?> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            [EasyAuthHeaders.Principal] =
                GetOptionalHeader(context, EasyAuthHeaders.Principal),
            [EasyAuthHeaders.PrincipalId] =
                GetOptionalHeader(context, EasyAuthHeaders.PrincipalId),
            [EasyAuthHeaders.PrincipalName] =
                GetOptionalHeader(context, EasyAuthHeaders.PrincipalName),
            [EasyAuthHeaders.IdentityProvider] =
                GetOptionalHeader(context, EasyAuthHeaders.IdentityProvider),
            ["X-MS-TOKEN-AAD-ACCESS-TOKEN"] =
                GetOptionalHeader(context, "X-MS-TOKEN-AAD-ACCESS-TOKEN"),
            ["X-ZUMO-AUTH"] = GetOptionalHeader(context, "X-ZUMO-AUTH"),
            ["X-Forwarded-For"] = GetOptionalHeader(context, "X-Forwarded-For"),
            ["X-Forwarded-Host"] = GetOptionalHeader(context, "X-Forwarded-Host"),
            ["X-Forwarded-Proto"] = GetOptionalHeader(context, "X-Forwarded-Proto")
        };

        await context.Response.WriteAsJsonAsync(
            new
            {
                method = context.Request.Method,
                path = context.Request.Path.Value,
                query = context.Request.QueryString.Value,
                body,
                headers
            },
            context.RequestAborted);
    }

    private static async Task SendServerEventsAsync(HttpContext context)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.WriteAsync("data: first\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        await Task.Delay(TimeSpan.FromMilliseconds(50), context.RequestAborted);
        await context.Response.WriteAsync("data: second\n\n", context.RequestAborted);
    }

    private static async Task EchoWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        byte[] buffer = new byte[4096];
        WebSocketReceiveResult result = await socket.ReceiveAsync(
            new ArraySegment<byte>(buffer),
            context.RequestAborted);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "closed",
                context.RequestAborted);
            return;
        }

        await socket.SendAsync(
            new ArraySegment<byte>(buffer, 0, result.Count),
            result.MessageType,
            result.EndOfMessage,
            context.RequestAborted);
        await socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "complete",
            context.RequestAborted);
    }

    private static string? GetOptionalHeader(HttpContext context, string name)
    {
        return context.Request.Headers.TryGetValue(
            name,
            out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }
}
