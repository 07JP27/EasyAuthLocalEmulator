namespace EasyAuthLocalEmulator.Configuration;

public sealed record StartCommandInput(
    string UpstreamUrl,
    int Port,
    bool OpenBrowser,
    FileInfo? ConfigFile,
    string? ProfileName,
    bool NoUi,
    string Platform = PlatformContracts.AppServiceCliValue);
