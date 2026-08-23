namespace EasyAuthLocalEmulator.Configuration;

public enum EmulatedPlatform
{
    AppService,
    ContainerApps
}

public sealed record PlatformContract(
    EmulatedPlatform Platform,
    string CliValue,
    string DisplayName,
    string DefaultLogoutCompletePath);

public static class PlatformContracts
{
    public const string AppServiceCliValue = "app-service";

    public const string ContainerAppsCliValue = "container-apps";

    public static PlatformContract AppService { get; } = new(
        EmulatedPlatform.AppService,
        AppServiceCliValue,
        "Azure App Service Easy Auth",
        "/.auth/logout/complete");

    public static PlatformContract ContainerApps { get; } = new(
        EmulatedPlatform.ContainerApps,
        ContainerAppsCliValue,
        "Azure Container Apps authentication",
        "/.auth/logout/done");

    public static PlatformContract Get(EmulatedPlatform platform)
    {
        return platform switch
        {
            EmulatedPlatform.AppService => AppService,
            EmulatedPlatform.ContainerApps => ContainerApps,
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };
    }

    public static bool TryParse(
        string value,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out PlatformContract? contract)
    {
        if (value.Equals(AppService.CliValue, StringComparison.Ordinal))
        {
            contract = AppService;
            return true;
        }

        if (value.Equals(ContainerApps.CliValue, StringComparison.Ordinal))
        {
            contract = ContainerApps;
            return true;
        }

        contract = null;
        return false;
    }
}
