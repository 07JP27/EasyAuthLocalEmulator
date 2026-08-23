using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using EasyAuthLocalEmulator.Auth;
using EasyAuthLocalEmulator.BrowserTests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace EasyAuthLocalEmulator.BrowserTests;

[Collection(BrowserTestGroup.Name)]
public sealed class NoUiAndFailureTests(BrowserFixture fixture) : PageTest
{
    [Fact]
    public async Task NoUiLogoutAndLoginToggleProcessWideIdentity()
    {
        await using EmulatorProcess emulator = await EmulatorProcess.StartAsync(
            fixture.SampleApp.BaseUri,
            noUi: true,
            profileName: "x-user");

        Assert.Equal(
            "x_user",
            await ReadPrincipalNameAsync(emulator.BaseUri));
        Assert.Equal(
            "twitter",
            await ReadIdentityProviderAsync(emulator.BaseUri));

        IAPIResponse logout = await Page.APIRequest.GetAsync(
            new Uri(emulator.BaseUri, "/.auth/logout").AbsoluteUri,
            new APIRequestContextOptions { MaxRedirects = 0 });
        Assert.Equal(302, logout.Status);
        Assert.Null(await ReadPrincipalNameAsync(emulator.BaseUri));

        await Page.GotoAsync(
            new Uri(emulator.BaseUri, "/.auth/logout/complete").AbsoluteUri);
        await Expect(Page.Locator(".actions a[href^='/.auth/login/']"))
            .ToHaveCountAsync(1);
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Sign in with X" }))
            .ToBeVisibleAsync();

        IAPIResponse wrongProvider = await Page.APIRequest.GetAsync(
            new Uri(emulator.BaseUri, "/.auth/login/aad").AbsoluteUri,
            new APIRequestContextOptions { MaxRedirects = 0 });
        IAPIResponse login = await Page.APIRequest.GetAsync(
            new Uri(emulator.BaseUri, "/.auth/login/x").AbsoluteUri,
            new APIRequestContextOptions { MaxRedirects = 0 });

        Assert.Equal(404, wrongProvider.Status);
        Assert.Equal(302, login.Status);
        Assert.Equal(
            "x_user",
            await ReadPrincipalNameAsync(emulator.BaseUri));
    }

    [Fact]
    public async Task UnavailableUpstreamReturnsBadGateway()
    {
        int unavailablePort = EmulatorProcess.AllocatePort();
        await using EmulatorProcess emulator = await EmulatorProcess.StartAsync(
            new Uri($"http://127.0.0.1:{unavailablePort}"),
            noUi: false);

        IAPIResponse response = await Page.APIRequest.GetAsync(
            new Uri(emulator.BaseUri, "/echo").AbsoluteUri);

        Assert.Equal(502, response.Status);
        Assert.Contains(
            "could not complete",
            await response.TextAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PortConflictExitsWithActionableError()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            int occupiedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            EmulatorProcessResult result = await EmulatorProcess.RunToExitAsync(
                fixture.SampleApp.BaseUri,
                occupiedPort);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("--port", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("already be in use", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task<string?> ReadPrincipalNameAsync(Uri emulatorBaseUri)
    {
        return await ReadHeaderAsync(
            emulatorBaseUri,
            EasyAuthHeaderNames.PrincipalName);
    }

    private async Task<string?> ReadIdentityProviderAsync(Uri emulatorBaseUri)
    {
        return await ReadHeaderAsync(
            emulatorBaseUri,
            EasyAuthHeaderNames.IdentityProvider);
    }

    private async Task<string?> ReadHeaderAsync(Uri emulatorBaseUri, string headerName)
    {
        IAPIResponse response = await Page.APIRequest.GetAsync(
            new Uri(emulatorBaseUri, "/echo").AbsoluteUri);
        using JsonDocument echo = JsonDocument.Parse(await response.TextAsync());
        JsonElement value = echo.RootElement
            .GetProperty("headers")
            .GetProperty(headerName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }
}
