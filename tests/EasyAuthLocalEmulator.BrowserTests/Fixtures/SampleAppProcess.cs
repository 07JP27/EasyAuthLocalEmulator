namespace EasyAuthLocalEmulator.BrowserTests.Fixtures;

public sealed class SampleAppProcess : IAsyncDisposable
{
    private readonly ChildProcess _process;

    private SampleAppProcess(ChildProcess process, int port)
    {
        _process = process;
        BaseUri = new Uri($"http://127.0.0.1:{port}");
    }

    public Uri BaseUri { get; }

    public static async Task<SampleAppProcess> StartAsync()
    {
        int port = TestEnvironmentInfo.AllocatePort();
        System.Diagnostics.ProcessStartInfo startInfo =
            TestEnvironmentInfo.CreateDotNetRunStartInfo(
                "samples/EasyAuthLocalEmulator.SampleApp/EasyAuthLocalEmulator.SampleApp.csproj");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add($"http://127.0.0.1:{port}");

        ChildProcess process = ChildProcess.Start(startInfo);
        SampleAppProcess sample = new(process, port);

        try
        {
            await process.WaitUntilReadyAsync(
                new Uri(sample.BaseUri, "/"),
                TimeSpan.FromSeconds(20));
            return sample;
        }
        catch
        {
            await process.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        return _process.DisposeAsync();
    }
}
