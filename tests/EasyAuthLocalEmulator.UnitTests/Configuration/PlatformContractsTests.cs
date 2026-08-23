using EasyAuthLocalEmulator.Configuration;

namespace EasyAuthLocalEmulator.UnitTests.Configuration;

public sealed class PlatformContractsTests
{
    [Theory]
    [InlineData(
        EmulatedPlatform.AppService,
        "app-service",
        "Azure App Service Easy Auth",
        "Azure App Service",
        "/.auth/logout/complete")]
    [InlineData(
        EmulatedPlatform.ContainerApps,
        "container-apps",
        "Azure Container Apps authentication",
        "Azure Container Apps",
        "/.auth/logout/done")]
    public void ReturnsPlatformContract(
        EmulatedPlatform platform,
        string cliValue,
        string displayName,
        string uiDisplayName,
        string logoutPath)
    {
        PlatformContract contract = PlatformContracts.Get(platform);

        Assert.Equal(cliValue, contract.CliValue);
        Assert.Equal(displayName, contract.DisplayName);
        Assert.Equal(uiDisplayName, contract.UiDisplayName);
        Assert.Equal(logoutPath, contract.DefaultLogoutCompletePath);
    }

    [Theory]
    [InlineData("app-service", EmulatedPlatform.AppService)]
    [InlineData("container-apps", EmulatedPlatform.ContainerApps)]
    public void ParsesExactCliValues(string value, EmulatedPlatform expected)
    {
        Assert.True(PlatformContracts.TryParse(value, out PlatformContract? contract));
        Assert.Equal(expected, contract.Platform);
    }

    [Theory]
    [InlineData("")]
    [InlineData("App-Service")]
    [InlineData("containerapps")]
    [InlineData("azure-functions")]
    public void RejectsUnsupportedCliValues(string value)
    {
        Assert.False(PlatformContracts.TryParse(value, out _));
    }

    [Fact]
    public void LoginUrlDoesNotDependOnPlatform()
    {
        Dictionary<string, EasyAuthLocalEmulator.Auth.UserProfile> profiles = [];
        EmulatorOptions appService = new(
            new Uri("http://localhost:5173"),
            4180,
            OpenBrowser: false,
            profiles,
            SelectedProfileName: null,
            SelectedProfile: null,
            NoUi: false,
            TimeSpan.FromHours(8),
            EmulatedPlatform.AppService);
        EmulatorOptions containerApps = appService with
        {
            Platform = EmulatedPlatform.ContainerApps
        };

        Assert.Equal(appService.LoginUrl, containerApps.LoginUrl);
        Assert.Equal(
            "http://127.0.0.1:4180/.auth/login/aad",
            appService.LoginUrl);
    }
}
