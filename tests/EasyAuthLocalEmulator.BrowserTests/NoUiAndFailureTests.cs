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
    [Theory]
    [InlineData(null, "/.auth/logout/complete")]
    [InlineData("container-apps", "/.auth/logout/done")]
    public async Task NoUiLogoutAndLoginToggleProcessWideIdentity(
        string? platform,
        string expectedLogoutPath)
    {
        await using EmulatorProcess emulator = await EmulatorProcess.StartAsync(
            fixture.SampleApp.BaseUri,
            noUi: true,
            profileName: "x-user",
            platform: platform);

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
        Assert.Equal(expectedLogoutPath, logout.Headers["location"]);
        Assert.Null(await ReadPrincipalNameAsync(emulator.BaseUri));

        await Page.GotoAsync(
            new Uri(emulator.BaseUri, expectedLogoutPath).AbsoluteUri);
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

    [Theory]
    [InlineData(
        null,
        "Azure App Service Easy Auth",
        "Azure App Service",
        "/.auth/logout/complete")]
    [InlineData(
        "app-service",
        "Azure App Service Easy Auth",
        "Azure App Service",
        "/.auth/logout/complete")]
    [InlineData(
        "container-apps",
        "Azure Container Apps authentication",
        "Azure Container Apps",
        "/.auth/logout/done")]
    public async Task PlatformControlsStartupAndDefaultLogout(
        string? platform,
        string displayName,
        string uiDisplayName,
        string expectedLogoutPath)
    {
        await using EmulatorProcess emulator = await EmulatorProcess.StartAsync(
            fixture.SampleApp.BaseUri,
            noUi: false,
            platform: platform);

        Assert.Contains(
            $"Platform:  {displayName}",
            emulator.GetOutput(),
            StringComparison.Ordinal);

        IAPIResponse loginPage = await Page.APIRequest.GetAsync(
            new Uri(emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);
        string loginHtml = await loginPage.TextAsync();
        Assert.Contains(
            "<h1>Easy Auth Local Emulator</h1>",
            loginHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            $"<p class=\"login-platform-name\">For: {uiDisplayName}</p>",
            loginHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Local authentication",
            loginHtml,
            StringComparison.Ordinal);

        IAPIResponse logout = await Page.APIRequest.GetAsync(
            new Uri(emulator.BaseUri, "/.auth/logout").AbsoluteUri,
            new APIRequestContextOptions { MaxRedirects = 0 });
        Assert.Equal(302, logout.Status);
        Assert.Equal(expectedLogoutPath, logout.Headers["location"]);

        IAPIResponse customLogout = await Page.APIRequest.GetAsync(
            new Uri(
                emulator.BaseUri,
                "/.auth/logout?post_logout_redirect_uri=%2F").AbsoluteUri,
            new APIRequestContextOptions { MaxRedirects = 0 });
        Assert.Equal("/", customLogout.Headers["location"]);

        foreach (string path in new[]
                 {
                     "/.auth/logout/complete",
                     "/.auth/logout/done"
                 })
        {
            IAPIResponse completion = await Page.APIRequest.GetAsync(
                new Uri(emulator.BaseUri, path).AbsoluteUri);
            Assert.Equal(200, completion.Status);
            Assert.Contains(
                $"Emulating: {displayName}",
                await completion.TextAsync(),
                StringComparison.Ordinal);
        }

        IAPIResponse me = await Page.APIRequest.GetAsync(
            new Uri(emulator.BaseUri, "/.auth/me").AbsoluteUri);
        Assert.Equal("[]", await me.TextAsync());
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
