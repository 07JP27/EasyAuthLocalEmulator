using EasyAuthLocalEmulator.Configuration;
using EasyAuthLocalEmulator.Hosting;

namespace EasyAuthLocalEmulator.Cli;

public sealed class StartCommandRunner
{
    private readonly StartOptionsFactory _optionsFactory;
    private readonly EmulatorHost _host;
    private readonly TextWriter _standardOutput;
    private readonly TextWriter _standardError;

    public StartCommandRunner(
        StartOptionsFactory? optionsFactory = null,
        EmulatorHost? host = null,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null)
    {
        _optionsFactory = optionsFactory ?? new StartOptionsFactory();
        _host = host ?? new EmulatorHost();
        _standardOutput = standardOutput ?? Console.Out;
        _standardError = standardError ?? Console.Error;
    }

    public async Task<int> RunAsync(StartCommandInput input, CancellationToken cancellationToken)
    {
        EmulatorOptions options;

        try
        {
            options = _optionsFactory.Create(input);
        }
        catch (InputValidationException exception)
        {
            await _standardError.WriteLineAsync($"error: {exception.Message}");
            return 2;
        }

        try
        {
            await _host.RunAsync(options, _standardOutput, _standardError, cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (IOException exception)
        {
            await _standardError.WriteLineAsync(
                $"error: Could not listen on http://127.0.0.1:{options.Port}. " +
                $"The port may already be in use; choose another value with --port. ({exception.Message})");
            return 1;
        }
    }
}
