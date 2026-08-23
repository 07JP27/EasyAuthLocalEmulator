using EasyAuthLocalEmulator.Auth;

namespace EasyAuthLocalEmulator.Configuration;

public sealed record EmulatorOptions(
    Uri Upstream,
    int Port,
    bool OpenBrowser,
    IReadOnlyDictionary<string, UserProfile> Profiles,
    string? SelectedProfileName,
    UserProfile? SelectedProfile,
    bool NoUi,
    TimeSpan SessionLifetime,
    EmulatedPlatform Platform = EmulatedPlatform.AppService)
{
    public string ProxyOrigin => $"http://127.0.0.1:{Port}";

    public string LoginUrl =>
        $"{ProxyOrigin}/.auth/login/{SelectedProfile?.Provider ?? "aad"}";

    public PlatformContract PlatformContract => PlatformContracts.Get(Platform);

    public string PlatformDisplayName => PlatformContract.DisplayName;

    public string DefaultLogoutCompletePath =>
        PlatformContract.DefaultLogoutCompletePath;
}
