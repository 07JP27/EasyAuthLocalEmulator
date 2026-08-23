namespace EasyAuthLocalEmulator.Auth;

public static class AuthEndpoints
{
    private static readonly string[] ReservedMethods =
        ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD", "CONNECT", "TRACE"];

    public static void MapEasyAuthEndpoints(this WebApplication application)
    {
        application.MapGet("/.auth/assets/site.css", () =>
            Results.Text(AuthAssets.Styles, "text/css; charset=utf-8"));
        application.MapGet("/.auth/assets/login.js", () =>
            Results.Text(AuthAssets.LoginScript, "text/javascript; charset=utf-8"));

        application.MapGet(
            "/.auth/me",
            (HttpContext context, LocalAuthenticationService authentication, PrincipalBuilder builder) =>
            {
                UserProfile? profile = authentication.Resolve(context);
                if (profile is null)
                {
                    return Results.Json(Array.Empty<EasyAuthIdentity>());
                }

                PrincipalSnapshot snapshot = builder.Build(profile);
                return Results.Json(new[] { snapshot.Identity });
            });

        application.MapGet(
            "/.auth/logout",
            (
                HttpContext context,
                string? post_logout_redirect_uri,
                LocalAuthenticationService authentication,
                RedirectUriValidator redirectValidator) =>
            {
                if (!redirectValidator.TryValidate(
                        post_logout_redirect_uri,
                        "/.auth/logout/complete",
                        out string redirectUri))
                {
                    return Results.BadRequest("Invalid post_logout_redirect_uri.");
                }

                authentication.SignOut(context);
                return Results.Redirect(redirectUri);
            });

        application.MapGet(
            "/.auth/refresh",
            (HttpContext context, LocalAuthenticationService authentication) =>
                authentication.Refresh(context)
                    ? Results.StatusCode(StatusCodes.Status200OK)
                    : Results.StatusCode(StatusCodes.Status401Unauthorized));

        application.MapMethods("/.auth", ReservedMethods, () => Results.NotFound());
        application.MapMethods(
            "/.auth/{**authPath}",
            ReservedMethods,
            () => Results.NotFound());
    }
}
