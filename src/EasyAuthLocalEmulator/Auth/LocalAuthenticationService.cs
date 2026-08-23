using EasyAuthLocalEmulator.Configuration;

namespace EasyAuthLocalEmulator.Auth;

public sealed class LocalAuthenticationService
{
    public const string CookieName = "AppServiceAuthSession";

    private readonly object _fixedStateLock = new();
    private readonly EmulatorOptions _options;
    private readonly InMemorySessionStore _sessionStore;
    private readonly TimeProvider _timeProvider;
    private FixedAuthenticationState? _fixedState;

    public LocalAuthenticationService(
        EmulatorOptions options,
        InMemorySessionStore sessionStore,
        TimeProvider timeProvider)
    {
        _options = options;
        _sessionStore = sessionStore;
        _timeProvider = timeProvider;

        if (options.NoUi)
        {
            _fixedState = new FixedAuthenticationState(
                IsActive: true,
                ExpiresAt: timeProvider.GetUtcNow().Add(options.SessionLifetime));
        }
    }

    public bool NoUi => _options.NoUi;

    public UserProfile? SelectedProfile => _options.SelectedProfile;

    public UserProfile? Resolve(HttpContext context)
    {
        if (_options.NoUi)
        {
            lock (_fixedStateLock)
            {
                if (_fixedState is null ||
                    !_fixedState.IsActive ||
                    _fixedState.ExpiresAt <= _timeProvider.GetUtcNow())
                {
                    _fixedState = new FixedAuthenticationState(
                        IsActive: false,
                        ExpiresAt: _timeProvider.GetUtcNow());
                    return null;
                }

                return _options.SelectedProfile;
            }
        }

        if (!context.Request.Cookies.TryGetValue(CookieName, out string? sessionId))
        {
            return null;
        }

        if (_sessionStore.TryGet(sessionId, out SessionRecord? session))
        {
            return session.Profile;
        }

        DeleteCookie(context);
        return null;
    }

    public void SignIn(HttpContext context, UserProfile profile)
    {
        if (_options.NoUi)
        {
            lock (_fixedStateLock)
            {
                _fixedState = new FixedAuthenticationState(
                    IsActive: true,
                    ExpiresAt: _timeProvider.GetUtcNow().Add(_options.SessionLifetime));
            }

            return;
        }

        SessionTicket session = _sessionStore.Create(profile);
        AppendCookie(context, session.Id, session.ExpiresAt);
    }

    public bool Refresh(HttpContext context)
    {
        if (_options.NoUi)
        {
            lock (_fixedStateLock)
            {
                if (_fixedState is null ||
                    !_fixedState.IsActive ||
                    _fixedState.ExpiresAt <= _timeProvider.GetUtcNow())
                {
                    _fixedState = new FixedAuthenticationState(
                        IsActive: false,
                        ExpiresAt: _timeProvider.GetUtcNow());
                    return false;
                }

                _fixedState = _fixedState with
                {
                    ExpiresAt = _timeProvider.GetUtcNow().Add(_options.SessionLifetime)
                };
                return true;
            }
        }

        if (!context.Request.Cookies.TryGetValue(CookieName, out string? sessionId) ||
            !_sessionStore.TryRefresh(sessionId, out SessionRecord? session))
        {
            DeleteCookie(context);
            return false;
        }

        AppendCookie(context, sessionId, session.ExpiresAt);
        return true;
    }

    public void SignOut(HttpContext context)
    {
        if (_options.NoUi)
        {
            lock (_fixedStateLock)
            {
                _fixedState = new FixedAuthenticationState(
                    IsActive: false,
                    ExpiresAt: _timeProvider.GetUtcNow());
            }
        }
        else if (context.Request.Cookies.TryGetValue(CookieName, out string? sessionId))
        {
            _sessionStore.Remove(sessionId);
        }

        DeleteCookie(context);
    }

    private static void AppendCookie(
        HttpContext context,
        string sessionId,
        DateTimeOffset expiresAt)
    {
        context.Response.Cookies.Append(CookieName, sessionId, CreateCookieOptions(expiresAt));
    }

    private static void DeleteCookie(HttpContext context)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.Cookies.Delete(CookieName, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                Path = "/",
                SameSite = SameSiteMode.Lax,
                Secure = false
            });
        }
    }

    private static CookieOptions CreateCookieOptions(DateTimeOffset expiresAt)
    {
        return new CookieOptions
        {
            Expires = expiresAt,
            HttpOnly = true,
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = false
        };
    }

    private sealed record FixedAuthenticationState(bool IsActive, DateTimeOffset ExpiresAt);
}
