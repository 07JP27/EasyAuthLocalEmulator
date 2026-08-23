namespace EasyAuthLocalEmulator.BrowserTests.Fixtures;

public sealed record EmulatorProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class EmulatorProcess : IAsyncDisposable
{
    private const string DefaultProfileName = "alice-admin";

    private readonly ChildProcess _process;
    private readonly string _temporaryDirectory;

    private EmulatorProcess(
        ChildProcess process,
        string temporaryDirectory,
        int port)
    {
        _process = process;
        _temporaryDirectory = temporaryDirectory;
        Port = port;
        BaseUri = new Uri($"http://127.0.0.1:{port}");
    }

    public Uri BaseUri { get; }

    public int Port { get; }

    public static async Task<EmulatorProcess> StartAsync(
        Uri upstream,
        bool noUi,
        int? port = null,
        string profileName = DefaultProfileName)
    {
        int selectedPort = port ?? TestEnvironmentInfo.AllocatePort();
        string temporaryDirectory = CreateTemporaryDirectory();
        string configurationPath = WriteConfiguration(temporaryDirectory);
        System.Diagnostics.ProcessStartInfo startInfo = CreateStartInfo(
            upstream,
            selectedPort,
            configurationPath,
            noUi,
            profileName);
        ChildProcess? process = null;

        try
        {
            process = ChildProcess.Start(startInfo);
            EmulatorProcess emulator = new(process, temporaryDirectory, selectedPort);
            await process.WaitUntilReadyAsync(
                new Uri(emulator.BaseUri, "/.auth/me"),
                TimeSpan.FromSeconds(20));
            return emulator;
        }
        catch
        {
            if (process is not null)
            {
                await process.DisposeAsync();
            }

            Directory.Delete(temporaryDirectory, recursive: true);
            throw;
        }
    }

    public static async Task<EmulatorProcessResult> RunToExitAsync(Uri upstream, int port)
    {
        System.Diagnostics.ProcessStartInfo startInfo = CreateStartInfo(
            upstream,
            port,
            configurationPath: null,
            noUi: false,
            profileName: null);
        await using ChildProcess process = ChildProcess.Start(startInfo);
        ChildProcessResult result = await process.WaitForExitAsync(TimeSpan.FromSeconds(20));
        return new EmulatorProcessResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError);
    }

    public static int AllocatePort()
    {
        return TestEnvironmentInfo.AllocatePort();
    }

    public async ValueTask DisposeAsync()
    {
        await _process.DisposeAsync();
        Directory.Delete(_temporaryDirectory, recursive: true);
    }

    public string GetOutput()
    {
        return _process.GetOutput();
    }

    private static System.Diagnostics.ProcessStartInfo CreateStartInfo(
        Uri upstream,
        int port,
        string? configurationPath,
        bool noUi,
        string? profileName)
    {
        System.Diagnostics.ProcessStartInfo startInfo =
            TestEnvironmentInfo.CreateDotNetRunStartInfo(
                "src/EasyAuthLocalEmulator/EasyAuthLocalEmulator.csproj");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("start");
        startInfo.ArgumentList.Add(upstream.AbsoluteUri);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(
            port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (configurationPath is not null)
        {
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(configurationPath);
            startInfo.ArgumentList.Add("--profile");
            startInfo.ArgumentList.Add(
                profileName ??
                throw new InvalidOperationException(
                    "A profile name is required with a configuration file."));
        }

        if (noUi)
        {
            startInfo.ArgumentList.Add("--no-ui");
        }

        return startInfo;
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"easyauth-browser-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string WriteConfiguration(string directory)
    {
        string path = Path.Combine(directory, "easyauth-local.json");
        File.WriteAllText(
            path,
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
                },
                "facebook-user": {
                  "provider": "facebook",
                  "displayName": "Facebook User",
                  "userName": "facebook@example.com",
                  "userId": "100000000000001",
                  "roles": ["Reader"],
                  "claims": []
                },
                "google-user": {
                  "provider": "google",
                  "displayName": "Google User",
                  "userName": "google@example.com",
                  "userId": "google-subject-001",
                  "roles": ["Reader"],
                  "claims": []
                },
                "x-user": {
                  "provider": "x",
                  "authenticationType": "twitter",
                  "displayName": "X User",
                  "userName": "x_user",
                  "userId": "1000000001",
                  "issuer": "",
                  "roles": ["Reader"],
                  "claims": []
                },
                "github-user": {
                  "provider": "github",
                  "displayName": "GitHub User",
                  "userName": "github-user",
                  "userId": "10000001",
                  "roles": ["Reader"],
                  "claims": []
                },
                "apple-user": {
                  "provider": "apple",
                  "displayName": "Apple User",
                  "userName": "apple@example.com",
                  "userId": "apple-subject-001",
                  "roles": ["Reader"],
                  "claims": []
                }
              }
            }
            """);
        return path;
    }
}
