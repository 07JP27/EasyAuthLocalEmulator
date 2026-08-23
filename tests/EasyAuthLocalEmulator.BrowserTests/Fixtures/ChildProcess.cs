using System.Diagnostics;
using System.Text;

namespace EasyAuthLocalEmulator.BrowserTests.Fixtures;

internal sealed record ChildProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed class ChildProcess : IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly Process _process;
    private readonly StringBuilder _standardOutput = new();
    private readonly StringBuilder _standardError = new();
    private readonly object _outputLock = new();
    private readonly TaskCompletionSource _standardOutputClosed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _standardErrorClosed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ChildProcess(Process process)
    {
        _process = process;
    }

    internal static ChildProcess Start(ProcessStartInfo startInfo)
    {
        Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        ChildProcess childProcess = new(process);
        childProcess.AttachOutputHandlers();

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException(
                $"The process '{startInfo.FileName}' could not be started.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return childProcess;
    }

    internal async Task WaitUntilReadyAsync(Uri readyUri, TimeSpan timeout)
    {
        using SocketsHttpHandler handler = new() { UseProxy = false };
        using HttpClient client = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(1)
        };
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                await WaitForOutputToCloseAsync(OutputDrainTimeout);
                throw new InvalidOperationException(
                    $"The process exited with code {_process.ExitCode}.{Environment.NewLine}{GetOutput()}");
            }

            try
            {
                using HttpResponseMessage response = await client.GetAsync(readyUri);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"The process did not become ready at {readyUri}.{Environment.NewLine}{GetOutput()}");
    }

    internal async Task<ChildProcessResult> WaitForExitAsync(TimeSpan timeout)
    {
        using CancellationTokenSource cancellation = new(timeout);

        try
        {
            await _process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException exception) when (cancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The child process did not exit within {timeout}.{Environment.NewLine}{GetOutput()}",
                exception);
        }

        await WaitForOutputToCloseAsync(OutputDrainTimeout);

        lock (_outputLock)
        {
            return new ChildProcessResult(
                _process.ExitCode,
                _standardOutput.ToString(),
                _standardError.ToString());
        }
    }

    internal string GetOutput()
    {
        lock (_outputLock)
        {
            return $"stdout:{Environment.NewLine}{_standardOutput}" +
                $"{Environment.NewLine}stderr:{Environment.NewLine}{_standardError}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (_process.HasExited)
        {
        }

        using CancellationTokenSource cancellation = new(ShutdownTimeout);

        try
        {
            await _process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync(
                $"warning: Child process {_process.Id} did not exit within " +
                $"{ShutdownTimeout}.{Environment.NewLine}{GetOutput()}");
        }
        finally
        {
            await WaitForOutputToCloseAsync(OutputDrainTimeout);
            _process.Dispose();
        }
    }

    private void AttachOutputHandlers()
    {
        _process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                _standardOutputClosed.TrySetResult();
                return;
            }

            lock (_outputLock)
            {
                _standardOutput.AppendLine(eventArgs.Data);
            }
        };
        _process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                _standardErrorClosed.TrySetResult();
                return;
            }

            lock (_outputLock)
            {
                _standardError.AppendLine(eventArgs.Data);
            }
        };
    }

    private async Task WaitForOutputToCloseAsync(TimeSpan timeout)
    {
        try
        {
            await Task.WhenAll(_standardOutputClosed.Task, _standardErrorClosed.Task)
                .WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            await Console.Error.WriteLineAsync(
                $"warning: Child process {_process.Id} output did not close within {timeout}.");
        }
    }
}
