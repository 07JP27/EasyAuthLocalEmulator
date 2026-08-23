using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace EasyAuthLocalEmulator.Auth;

public sealed record SessionRecord(UserProfile Profile, DateTimeOffset ExpiresAt);

public sealed record SessionTicket(string Id, DateTimeOffset ExpiresAt);

public sealed class InMemorySessionStore
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions =
        new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;

    public InMemorySessionStore(TimeProvider timeProvider, TimeSpan lifetime)
    {
        _timeProvider = timeProvider;
        _lifetime = lifetime;
    }

    public SessionTicket Create(UserProfile profile)
    {
        DateTimeOffset expiresAt = _timeProvider.GetUtcNow().Add(_lifetime);

        while (true)
        {
            string id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            if (_sessions.TryAdd(id, new SessionRecord(profile, expiresAt)))
            {
                return new SessionTicket(id, expiresAt);
            }
        }
    }

    public bool TryGet(
        string id,
        [NotNullWhen(true)] out SessionRecord? session)
    {
        if (!_sessions.TryGetValue(id, out session))
        {
            return false;
        }

        if (session.ExpiresAt > _timeProvider.GetUtcNow())
        {
            return true;
        }

        _sessions.TryRemove(new KeyValuePair<string, SessionRecord>(id, session));
        session = null;
        return false;
    }

    public bool TryRefresh(string id, [NotNullWhen(true)] out SessionRecord? session)
    {
        while (_sessions.TryGetValue(id, out SessionRecord? current))
        {
            if (current.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                _sessions.TryRemove(new KeyValuePair<string, SessionRecord>(id, current));
                session = null;
                return false;
            }

            SessionRecord updated = current with
            {
                ExpiresAt = _timeProvider.GetUtcNow().Add(_lifetime)
            };

            if (_sessions.TryUpdate(id, updated, current))
            {
                session = updated;
                return true;
            }
        }

        session = null;
        return false;
    }

    public bool Remove(string id)
    {
        return _sessions.TryRemove(id, out _);
    }

    public int RemoveExpired()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        int removed = 0;

        foreach ((string id, SessionRecord session) in _sessions)
        {
            if (session.ExpiresAt <= now &&
                _sessions.TryRemove(new KeyValuePair<string, SessionRecord>(id, session)))
            {
                removed++;
            }
        }

        return removed;
    }
}
