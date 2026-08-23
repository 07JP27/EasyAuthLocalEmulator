using EasyAuthLocalEmulator.Auth;
using EasyAuthLocalEmulator.Configuration;
using Microsoft.AspNetCore.Http;

namespace EasyAuthLocalEmulator.UnitTests.Auth;

public sealed class LocalAuthenticationServiceTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InteractiveModeUsesOpaqueCookieSession()
    {
        ManualTimeProvider timeProvider = new(InitialTime);
        LocalAuthenticationService service = CreateService(
            noUi: false,
            timeProvider,
            out _);
        DefaultHttpContext signInContext = new();

        service.SignIn(signInContext, TestData.CreateProfile());

        string setCookie = signInContext.Response.Headers.SetCookie.ToString();
        string[] cookieAttributes = setCookie.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("AppServiceAuthSession=", setCookie, StringComparison.Ordinal);
        Assert.Contains(
            cookieAttributes,
            attribute => attribute.Equals("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            cookieAttributes,
            attribute => attribute.Equals("samesite=lax", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            cookieAttributes,
            attribute => attribute.Equals("path=/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            cookieAttributes,
            attribute => attribute.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            cookieAttributes,
            attribute => attribute.Equals("secure", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Alice", setCookie, StringComparison.Ordinal);

        DefaultHttpContext authenticatedContext = CreateContextWithCookie(setCookie);
        UserProfile? profile = service.Resolve(authenticatedContext);

        Assert.Equal("alice@example.com", profile?.UserName);
    }

    [Fact]
    public void InteractiveLogoutInvalidatesSession()
    {
        ManualTimeProvider timeProvider = new(InitialTime);
        LocalAuthenticationService service = CreateService(
            noUi: false,
            timeProvider,
            out _);
        DefaultHttpContext signInContext = new();
        service.SignIn(signInContext, TestData.CreateProfile());
        string setCookie = signInContext.Response.Headers.SetCookie.ToString();
        DefaultHttpContext logoutContext = CreateContextWithCookie(setCookie);

        service.SignOut(logoutContext);

        Assert.Null(service.Resolve(CreateContextWithCookie(setCookie)));
    }

    [Fact]
    public void NoUiLogoutAndLoginChangeProcessWideState()
    {
        ManualTimeProvider timeProvider = new(InitialTime);
        LocalAuthenticationService service = CreateService(
            noUi: true,
            timeProvider,
            out UserProfile profile);

        Assert.Same(profile, service.Resolve(new DefaultHttpContext()));

        service.SignOut(new DefaultHttpContext());
        Assert.Null(service.Resolve(new DefaultHttpContext()));

        service.SignIn(new DefaultHttpContext(), profile);
        Assert.Same(profile, service.Resolve(new DefaultHttpContext()));
    }

    [Fact]
    public void NoUiStateExpiresAndRefreshExtendsIt()
    {
        ManualTimeProvider timeProvider = new(InitialTime);
        LocalAuthenticationService service = CreateService(
            noUi: true,
            timeProvider,
            out _);
        timeProvider.Advance(TimeSpan.FromHours(7));

        Assert.True(service.Refresh(new DefaultHttpContext()));

        timeProvider.Advance(TimeSpan.FromHours(7));
        Assert.NotNull(service.Resolve(new DefaultHttpContext()));

        timeProvider.Advance(TimeSpan.FromHours(1));
        Assert.Null(service.Resolve(new DefaultHttpContext()));
    }

    private static LocalAuthenticationService CreateService(
        bool noUi,
        TimeProvider timeProvider,
        out UserProfile profile)
    {
        profile = TestData.CreateProfile();
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
            noUi,
            TimeSpan.FromHours(8));
        InMemorySessionStore store = new(timeProvider, options.SessionLifetime);
        return new LocalAuthenticationService(options, store, timeProvider);
    }

    private static DefaultHttpContext CreateContextWithCookie(string setCookie)
    {
        string cookie = setCookie.Split(';', 2, StringSplitOptions.TrimEntries)[0];
        DefaultHttpContext context = new();
        context.Request.Headers.Cookie = cookie;
        return context;
    }
}
