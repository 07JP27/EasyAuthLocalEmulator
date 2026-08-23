namespace EasyAuthLocalEmulator.Auth;

public sealed class AuthResponseSecurityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/.auth"))
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers.CacheControl = "no-store";
            headers.ContentSecurityPolicy =
                "default-src 'none'; style-src 'self'; script-src 'self'; " +
                "img-src 'self' data:; form-action 'self'; base-uri 'none'; " +
                "frame-ancestors 'none'";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Referrer-Policy"] = "no-referrer";
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
        }

        await next(context);
    }
}
