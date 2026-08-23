using System.Diagnostics;
using System.Net;
using Yarp.ReverseProxy.Forwarder;

namespace EasyAuthLocalEmulator.Proxy;

public static class ProxyEndpoints
{
    private static readonly Action<ILogger, ForwarderError, Exception?> LogProxyFailure =
        LoggerMessage.Define<ForwarderError>(
            LogLevel.Warning,
            new EventId(1, "ProxyFailure"),
            "The upstream proxy failed with {ForwarderError}.");

    private static readonly string[] ProxyMethods =
    [
        "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD", "CONNECT", "TRACE"
    ];

    public static void AddEasyAuthProxy(this IServiceCollection services)
    {
        services.AddHttpForwarder();
        services.AddSingleton<EasyAuthHttpTransformer>();
        services.AddSingleton(new ForwarderRequestConfig
        {
            ActivityTimeout = TimeSpan.FromMinutes(10)
        });
        services.AddSingleton(_ => new HttpMessageInvoker(new SocketsHttpHandler
        {
            ActivityHeadersPropagator =
                new ReverseProxyPropagator(DistributedContextPropagator.Current),
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            EnableMultipleHttp2Connections = true,
            UseCookies = false,
            UseProxy = false
        }));
    }

    public static void MapEasyAuthProxy(this WebApplication application)
    {
        application.MapMethods(
                "/{**catchAll}",
                ProxyMethods,
                ForwardAsync)
            .WithDisplayName("Easy Auth upstream proxy")
            .WithOrder(int.MaxValue);
    }

    private static async Task ForwardAsync(
        HttpContext context,
        IHttpForwarder forwarder,
        HttpMessageInvoker httpClient,
        ForwarderRequestConfig requestConfig,
        EasyAuthHttpTransformer transformer,
        ILoggerFactory loggerFactory)
    {
        ForwarderError error = await forwarder.SendAsync(
            context,
            transformer.Options.Upstream.AbsoluteUri,
            httpClient,
            requestConfig,
            transformer);

        if (error == ForwarderError.None)
        {
            return;
        }

        IForwarderErrorFeature? errorFeature = context.GetForwarderErrorFeature();
        ILogger logger = loggerFactory.CreateLogger("EasyAuthLocalEmulator.Proxy");
        LogProxyFailure(logger, error, errorFeature?.Exception);

        if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        context.Response.StatusCode =
            error is ForwarderError.RequestTimedOut or ForwarderError.UpgradeActivityTimeout
                ? StatusCodes.Status504GatewayTimeout
                : StatusCodes.Status502BadGateway;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(
            "The local upstream application could not complete the request.",
            context.RequestAborted);
    }
}
