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
    TimeSpan SessionLifetime)
{
    public string ProxyOrigin => $"http://127.0.0.1:{Port}";

    public string LoginUrl =>
        $"{ProxyOrigin}/.auth/login/{SelectedProfile?.Provider ?? "aad"}";
}
