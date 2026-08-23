using EasyAuthLocalEmulator.Auth;

namespace EasyAuthLocalEmulator.UnitTests.Auth;

public sealed class InMemorySessionStoreTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreatesOpaqueSessionAndExpiresIt()
    {
        ManualTimeProvider timeProvider = new(InitialTime);
        InMemorySessionStore store = new(timeProvider, TimeSpan.FromHours(8));

        SessionTicket ticket = store.Create(TestData.CreateProfile());

        Assert.True(ticket.Id.Length >= 43);
        Assert.DoesNotContain("Alice", ticket.Id, StringComparison.Ordinal);
        Assert.True(store.TryGet(ticket.Id, out SessionRecord? active));
        Assert.Equal("Alice Example", active.Profile.DisplayName);

        timeProvider.Advance(TimeSpan.FromHours(8));

        Assert.False(store.TryGet(ticket.Id, out _));
    }

    [Fact]
    public void RefreshExtendsSessionLifetime()
    {
        ManualTimeProvider timeProvider = new(InitialTime);
        InMemorySessionStore store = new(timeProvider, TimeSpan.FromHours(8));
        SessionTicket ticket = store.Create(TestData.CreateProfile());
        timeProvider.Advance(TimeSpan.FromHours(7));

        Assert.True(store.TryRefresh(ticket.Id, out SessionRecord? refreshed));
        Assert.Equal(InitialTime.AddHours(15), refreshed.ExpiresAt);

        timeProvider.Advance(TimeSpan.FromHours(8));
        Assert.False(store.TryGet(ticket.Id, out _));
    }

    [Fact]
    public void RemovesExpiredSessionsInBulk()
    {
        ManualTimeProvider timeProvider = new(InitialTime);
        InMemorySessionStore store = new(timeProvider, TimeSpan.FromMinutes(5));
        _ = store.Create(TestData.CreateProfile());
        _ = store.Create(TestData.CreateProfile());
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(2, store.RemoveExpired());
        Assert.Equal(0, store.RemoveExpired());
    }
}
