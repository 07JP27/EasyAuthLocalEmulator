namespace EasyAuthLocalEmulator.Auth;

public sealed class SessionCleanupService(
    InMemorySessionStore sessionStore,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), timeProvider, stoppingToken);
            sessionStore.RemoveExpired();
        }
    }
}
