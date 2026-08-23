using System.CommandLine;
using EasyAuthLocalEmulator.Cli;

namespace EasyAuthLocalEmulator.UnitTests.Cli;

public sealed class EmulatorCliTests
{
    [Fact]
    public void StartCommandUsesExpectedDefaults()
    {
        RootCommand command = EmulatorCli.BuildCommand(new StartCommandRunner());

        System.CommandLine.ParseResult result =
            command.Parse(["start", "http://localhost:5173"]);

        Assert.Empty(result.Errors);
        Assert.Equal(4180, result.GetValue<int>("--port"));
        Assert.Equal("app-service", result.GetValue<string>("--platform"));
        Assert.False(result.GetValue<bool>("--open"));
        Assert.False(result.GetValue<bool>("--no-ui"));
    }

    [Fact]
    public void StartCommandRequiresUpstreamUrl()
    {
        RootCommand command = EmulatorCli.BuildCommand(new StartCommandRunner());

        System.CommandLine.ParseResult result = command.Parse(["start"]);

        Assert.NotEmpty(result.Errors);
    }
}
