namespace EasyAuthLocalEmulator.BrowserTests.Fixtures;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BrowserTestGroup : ICollectionFixture<BrowserFixture>
{
    public const string Name = "Easy Auth browser tests";
}

public sealed class BrowserFixture : IAsyncLifetime
{
    private SampleAppProcess? _sampleApp;
    private EmulatorProcess? _emulator;

    public SampleAppProcess SampleApp =>
        _sampleApp ?? throw new InvalidOperationException("The sample app is not running.");

    public EmulatorProcess Emulator =>
        _emulator ?? throw new InvalidOperationException("The emulator is not running.");

    public async Task InitializeAsync()
    {
        _sampleApp = await SampleAppProcess.StartAsync();

        try
        {
            _emulator = await EmulatorProcess.StartAsync(_sampleApp.BaseUri, noUi: false);
        }
        catch
        {
            await _sampleApp.DisposeAsync();
            _sampleApp = null;
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_emulator is not null)
        {
            await _emulator.DisposeAsync();
            _emulator = null;
        }

        if (_sampleApp is not null)
        {
            await _sampleApp.DisposeAsync();
            _sampleApp = null;
        }
    }
}
