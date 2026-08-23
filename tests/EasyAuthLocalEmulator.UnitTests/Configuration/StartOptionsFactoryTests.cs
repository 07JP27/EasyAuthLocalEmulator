using EasyAuthLocalEmulator.Configuration;

namespace EasyAuthLocalEmulator.UnitTests.Configuration;

public sealed class StartOptionsFactoryTests
{
    private readonly StartOptionsFactory _factory = new();

    [Fact]
    public void CreatesOptionsFromValidConfiguration()
    {
        using TemporaryConfig config = TemporaryConfig.Create(ValidConfiguration);

        EmulatorOptions options = _factory.Create(new StartCommandInput(
            "http://localhost:5173",
            4180,
            OpenBrowser: true,
            config.File,
            "alice-admin",
            NoUi: true));

        Assert.Equal("http://localhost:5173/", options.Upstream.AbsoluteUri);
        Assert.Equal(TimeSpan.FromHours(8), options.SessionLifetime);
        Assert.True(options.OpenBrowser);
        Assert.True(options.NoUi);
        Assert.Equal("Alice Example", options.SelectedProfile?.DisplayName);
        Assert.Equal(EmulatedPlatform.AppService, options.Platform);
    }

    [Fact]
    public void CreatesContainerAppsOptions()
    {
        EmulatorOptions options = _factory.Create(new StartCommandInput(
            "http://localhost:5173",
            4180,
            OpenBrowser: false,
            ConfigFile: null,
            ProfileName: null,
            NoUi: false,
            Platform: "container-apps"));

        Assert.Equal(EmulatedPlatform.ContainerApps, options.Platform);
        Assert.Equal("/.auth/logout/done", options.DefaultLogoutCompletePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("App-Service")]
    [InlineData("containerapps")]
    public void RejectsInvalidPlatform(string platform)
    {
        InputValidationException exception = Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                "http://localhost:5173",
                4180,
                OpenBrowser: false,
                ConfigFile: null,
                ProfileName: null,
                NoUi: false,
                Platform: platform)));

        Assert.Contains("--platform", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("ftp://localhost:21")]
    [InlineData("http://user:pass@localhost:5173")]
    [InlineData("http://localhost:5173?x=1")]
    [InlineData("http://localhost:5173#fragment")]
    public void RejectsUnsafeUpstream(string upstream)
    {
        InputValidationException exception = Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                upstream,
                4180,
                OpenBrowser: false,
                ConfigFile: null,
                ProfileName: null,
                NoUi: false)));

        Assert.NotEmpty(exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void RejectsInvalidPort(int port)
    {
        Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                "http://127.0.0.1:5173",
                port,
                OpenBrowser: false,
                ConfigFile: null,
                ProfileName: null,
                NoUi: false)));
    }

    [Fact]
    public void NoUiRequiresConfigAndProfile()
    {
        InputValidationException exception = Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                "http://localhost:5173",
                4180,
                OpenBrowser: false,
                ConfigFile: null,
                ProfileName: null,
                NoUi: true)));

        Assert.Contains("--config", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoUiWithProfileStillReportsMissingConfigFirst()
    {
        InputValidationException exception = Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                "http://localhost:5173",
                4180,
                OpenBrowser: false,
                ConfigFile: null,
                ProfileName: "alice",
                NoUi: true)));

        Assert.Contains("--config", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveProfileRequiresConfig()
    {
        InputValidationException exception = Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                "http://localhost:5173",
                4180,
                OpenBrowser: false,
                ConfigFile: null,
                ProfileName: "alice",
                NoUi: false)));

        Assert.Equal("--profile requires --config.", exception.Message);
    }

    [Fact]
    public void RejectsUnknownProperty()
    {
        using TemporaryConfig config = TemporaryConfig.Create(
            """{"profiles":{},"unexpected":true}""");

        InputValidationException exception = Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                "http://localhost:5173",
                4180,
                OpenBrowser: false,
                config.File,
                ProfileName: null,
                NoUi: false)));

        Assert.Contains("unexpected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsPlatformInJsonConfiguration()
    {
        using TemporaryConfig config = TemporaryConfig.Create(
            """{"platform":"container-apps","profiles":{}}""");

        InputValidationException exception = Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                "http://localhost:5173",
                4180,
                OpenBrowser: false,
                config.File,
                ProfileName: null,
                NoUi: false)));

        Assert.Contains("platform", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsDuplicateJsonProperty()
    {
        using TemporaryConfig config = TemporaryConfig.Create(
            """{"profiles":{},"profiles":{}}""");

        InputValidationException exception = Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                "http://localhost:5173",
                4180,
                OpenBrowser: false,
                config.File,
                ProfileName: null,
                NoUi: false)));

        Assert.Contains("Duplicate property", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownProfile()
    {
        using TemporaryConfig config = TemporaryConfig.Create(ValidConfiguration);

        InputValidationException exception = Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                "http://localhost:5173",
                4180,
                OpenBrowser: false,
                config.File,
                "missing",
                NoUi: false)));

        Assert.Contains("was not found", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:59")]
    [InlineData("7.00:00:01")]
    public void RejectsSessionLifetimeOutsideSupportedRange(string lifetime)
    {
        using TemporaryConfig config = TemporaryConfig.Create(
            ValidConfiguration.Replace(
                "08:00:00",
                lifetime,
                StringComparison.Ordinal));

        InputValidationException exception = Assert.Throws<InputValidationException>(() =>
            _factory.Create(new StartCommandInput(
                "http://localhost:5173",
                4180,
                OpenBrowser: false,
                config.File,
                ProfileName: null,
                NoUi: false)));

        Assert.Contains("sessionLifetime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsMaximumSessionLifetime()
    {
        using TemporaryConfig config = TemporaryConfig.Create(
            ValidConfiguration.Replace(
                "08:00:00",
                "7.00:00:00",
                StringComparison.Ordinal));

        EmulatorOptions options = _factory.Create(new StartCommandInput(
            "http://localhost:5173",
            4180,
            OpenBrowser: false,
            config.File,
            ProfileName: null,
            NoUi: false));

        Assert.Equal(TimeSpan.FromDays(7), options.SessionLifetime);
    }

    private const string ValidConfiguration =
        """
        {
          "sessionLifetime": "08:00:00",
          "profiles": {
            "alice-admin": {
              "displayName": "Alice Example",
              "upn": "alice@example.com",
              "userId": "11111111-1111-1111-1111-111111111111",
              "tenantId": "22222222-2222-2222-2222-222222222222",
              "roles": ["Admin", "Reader"],
              "claims": [
                { "typ": "department", "val": "Engineering" }
              ]
            }
          }
        }
        """;

    private sealed class TemporaryConfig : IDisposable
    {
        private TemporaryConfig(string path)
        {
            File = new FileInfo(path);
        }

        internal FileInfo File { get; }

        internal static TemporaryConfig Create(string contents)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                $"easyauth-tests-{Guid.NewGuid():N}.json");
            System.IO.File.WriteAllText(path, contents);
            return new TemporaryConfig(path);
        }

        public void Dispose()
        {
            System.IO.File.Delete(File.FullName);
        }
    }
}
