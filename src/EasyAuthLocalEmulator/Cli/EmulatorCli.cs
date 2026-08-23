using System.CommandLine;
using System.CommandLine.Parsing;
using EasyAuthLocalEmulator.Configuration;

namespace EasyAuthLocalEmulator.Cli;

public static class EmulatorCli
{
    public static Task<int> InvokeAsync(
        string[] args,
        StartCommandRunner? runner = null)
    {
        RootCommand rootCommand = BuildCommand(runner ?? new StartCommandRunner());
        return rootCommand.Parse(args).InvokeAsync();
    }

    public static RootCommand BuildCommand(StartCommandRunner runner)
    {
        Argument<string> upstreamArgument = new("upstream-url")
        {
            Description = "The local HTTP or HTTPS application URL to proxy."
        };
        Option<int> portOption = new("--port")
        {
            Description = "The loopback port exposed by the emulator.",
            DefaultValueFactory = _ => 4180
        };
        Option<bool> openOption = new("--open")
        {
            Description = "Open the proxy URL in the default browser after startup."
        };
        Option<FileInfo?> configOption = new("--config")
        {
            Description = "Path to an easyauth JSON configuration file."
        };
        Option<string?> profileOption = new("--profile")
        {
            Description = "A profile name from the configuration file."
        };
        Option<bool> noUiOption = new("--no-ui")
        {
            Description = "Use the selected profile without showing the editor UI."
        };

        Command startCommand = new("start", "Start the Easy Auth local proxy.")
        {
            upstreamArgument,
            portOption,
            openOption,
            configOption,
            profileOption,
            noUiOption
        };

        startCommand.SetAction((ParseResult parseResult, CancellationToken token) =>
        {
            StartCommandInput input = new(
                parseResult.GetValue(upstreamArgument)!,
                parseResult.GetValue(portOption),
                parseResult.GetValue(openOption),
                parseResult.GetValue(configOption),
                parseResult.GetValue(profileOption),
                parseResult.GetValue(noUiOption));

            return runner.RunAsync(input, token);
        });

        RootCommand rootCommand = new("Emulate Azure App Service Easy Auth for a local application.");
        rootCommand.Subcommands.Add(startCommand);
        return rootCommand;
    }
}
