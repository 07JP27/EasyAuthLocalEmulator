using EasyAuthLocalEmulator.Auth;
using EasyAuthLocalEmulator.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace EasyAuthLocalEmulator.Proxy;

public sealed class EasyAuthHttpTransformer(
    EmulatorOptions options,
    LocalAuthenticationService authentication,
    PrincipalBuilder principalBuilder) : HttpTransformer
{
    private static readonly string[] ForwardedHeaders =
    [
        "Forwarded",
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Port",
        "X-Forwarded-Prefix",
        "X-Forwarded-Proto"
    ];

    internal EmulatorOptions Options => options;

    public override async ValueTask TransformRequestAsync(
        HttpContext httpContext,
        HttpRequestMessage proxyRequest,
        string destinationPrefix,
        CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(
            httpContext,
            proxyRequest,
            destinationPrefix,
            cancellationToken);

        RemovePlatformHeaders(proxyRequest);
        foreach (string header in ForwardedHeaders)
        {
            RemoveHeader(proxyRequest, header);
        }

        UserProfile? profile = authentication.Resolve(httpContext);
        if (profile is not null)
        {
            PrincipalSnapshot snapshot = principalBuilder.Build(profile);
            foreach ((string name, string value) in snapshot.Headers)
            {
                proxyRequest.Headers.TryAddWithoutValidation(name, value);
            }
        }

        string? remoteAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        if (remoteAddress is not null)
        {
            proxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-For", remoteAddress);
        }

        proxyRequest.Headers.TryAddWithoutValidation(
            "X-Forwarded-Host",
            httpContext.Request.Host.Value);
        proxyRequest.Headers.TryAddWithoutValidation(
            "X-Forwarded-Proto",
            httpContext.Request.Scheme);
        proxyRequest.Headers.Host = null;
        proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress(
            options.Upstream.AbsoluteUri,
            httpContext.Request.Path,
            httpContext.Request.QueryString);
    }

    private static void RemovePlatformHeaders(HttpRequestMessage request)
    {
        foreach (string name in request.Headers
                     .Select(header => header.Key)
                     .Where(IsPlatformOwnedHeader)
                     .ToArray())
        {
            request.Headers.Remove(name);
        }

        if (request.Content is null)
        {
            return;
        }

        foreach (string name in request.Content.Headers
                     .Select(header => header.Key)
                     .Where(IsPlatformOwnedHeader)
                     .ToArray())
        {
            request.Content.Headers.Remove(name);
        }
    }

    private static bool IsPlatformOwnedHeader(string name)
    {
        return name.StartsWith(
                "X-MS-CLIENT-PRINCIPAL",
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("X-MS-TOKEN-", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("X-ZUMO-AUTH", StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveHeader(HttpRequestMessage request, string name)
    {
        request.Headers.Remove(name);
        request.Content?.Headers.Remove(name);
    }
}
