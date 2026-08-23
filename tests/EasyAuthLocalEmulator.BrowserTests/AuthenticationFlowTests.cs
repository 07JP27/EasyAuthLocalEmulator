using System.Text.Json;
using EasyAuthLocalEmulator.BrowserTests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace EasyAuthLocalEmulator.BrowserTests;

[Collection(BrowserTestGroup.Name)]
public sealed class AuthenticationFlowTests(BrowserFixture fixture) : PageTest
{
    [Fact]
    public async Task UserCanEditProfileLoginRefreshAndLogout()
    {
        await Page.GotoAsync(new Uri(fixture.Emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);

        await Expect(Page.GetByRole(
                AriaRole.Heading,
                new() { Name = "Local authentication" }))
            .ToBeVisibleAsync();
        ILocator providerInput = Page.GetByRole(
            AriaRole.Textbox,
            new() { Name = "Provider", Exact = true });
        await Expect(providerInput).ToHaveValueAsync("aad");
        await Expect(providerInput).ToHaveAttributeAsync("readonly", "");
        await Expect(Page.GetByLabel("Display name")).ToHaveValueAsync("Alice Example");
        await Expect(Page.GetByLabel("User name / email"))
            .ToHaveValueAsync("alice@example.com");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Add role" }).ClickAsync();
        await Page.Locator("#roles input").Last.FillAsync("Contributor");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add claim" }).ClickAsync();
        await Page.Locator("#claims input[name$='.Type']").Last.FillAsync("project");
        await Page.Locator("#claims input[name$='.Value']").Last.FillAsync("LocalAuth");
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();

        await Expect(Page).ToHaveURLAsync(
            new Uri(fixture.Emulator.BaseUri, "/").AbsoluteUri);
        await Expect(Page.Locator("#authentication-status")).ToHaveTextAsync("Authenticated");
        await Expect(Page.Locator("#principal-name")).ToHaveTextAsync("alice@example.com");
        await Expect(Page.Locator("#identity-provider")).ToHaveTextAsync("aad");
        await Expect(Page.Locator("#session-provider")).ToHaveTextAsync("aad");
        await Expect(Page.Locator("#session-user-name")).ToHaveTextAsync("alice@example.com");
        await Expect(Page.Locator("#session-user-id"))
            .ToHaveTextAsync("11111111-1111-1111-1111-111111111111");
        await Expect(Page.Locator("#encoded-principal")).Not.ToHaveTextAsync("Not present");
        await Expect(Page.Locator("#decoded-principal")).ToContainTextAsync("Alice Example");
        await Expect(Page.Locator("#decoded-principal")).ToContainTextAsync("LocalAuth");
        await Expect(Page.Locator(".app-bar a[href^='/.auth/login/']"))
            .ToHaveCountAsync(0);
        await Expect(Page.Locator(".endpoint-table tr")).ToHaveCountAsync(5);

        IAPIResponse meResponse = await Page.APIRequest.GetAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/me").AbsoluteUri);
        Assert.Equal(200, meResponse.Status);
        using JsonDocument me = JsonDocument.Parse(await meResponse.TextAsync());
        JsonElement identity = Assert.Single(me.RootElement.EnumerateArray());
        Assert.Equal("aad", identity.GetProperty("provider_name").GetString());
        Assert.False(identity.TryGetProperty("issuer", out _));
        Assert.Contains(
            identity.GetProperty("user_claims").EnumerateArray(),
            claim =>
                claim.GetProperty("typ").GetString() == "project" &&
                claim.GetProperty("val").GetString() == "LocalAuth");
        Assert.Contains(
            identity.GetProperty("user_claims").EnumerateArray(),
            claim => claim.GetProperty("typ").GetString() == "iss");

        IAPIResponse refreshResponse = await Page.APIRequest.GetAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/refresh").AbsoluteUri);
        Assert.Equal(200, refreshResponse.Status);

        ILocator signOut = Page.GetByRole(AriaRole.Link, new() { Name = "Sign out" });
        await Expect(signOut).ToHaveAttributeAsync(
            "href",
            "/.auth/logout");
        await signOut.ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            new Uri(
                fixture.Emulator.BaseUri,
                "/.auth/logout/complete").AbsoluteUri);
        await Expect(Page.GetByRole(
                AriaRole.Heading,
                new() { Name = "Signed out" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task LoginRequiresAntiforgeryAndShowsServerValidation()
    {
        IAPIResponse missingTokenResponse = await Page.APIRequest.PostAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);
        Assert.Equal(400, missingTokenResponse.Status);

        await Page.GotoAsync(new Uri(fixture.Emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);
        await Page.GetByLabel("User ID").FillAsync("not-a-guid");
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();

        await Expect(Page.Locator(".validation-summary-errors"))
            .ToContainTextAsync("userId must be a GUID");
        Assert.Contains("/.auth/login/aad", Page.Url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("google", "google-user", "google", "https://accounts.google.com")]
    [InlineData("github", "github-user", "github", null)]
    [InlineData("x", "x-user", "twitter", null)]
    [InlineData("apple", "apple-user", "apple", "https://appleid.apple.com")]
    public async Task ConfiguredProviderProfilesProduceConsistentContracts(
        string provider,
        string profileName,
        string authenticationType,
        string? expectedIssuer)
    {
        string loginUrl = new Uri(
            fixture.Emulator.BaseUri,
            $"/.auth/login/{provider}?profile={profileName}").AbsoluteUri;
        await Page.GotoAsync(loginUrl);

        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            new Uri(fixture.Emulator.BaseUri, "/").AbsoluteUri);

        await Expect(Page.Locator("#identity-provider"))
            .ToHaveTextAsync(authenticationType);

        IAPIResponse meResponse = await Page.APIRequest.GetAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/me").AbsoluteUri);
        using JsonDocument me = JsonDocument.Parse(await meResponse.TextAsync());
        JsonElement identity = Assert.Single(me.RootElement.EnumerateArray());
        Assert.Equal(authenticationType, identity.GetProperty("provider_name").GetString());
        Assert.False(identity.TryGetProperty("issuer", out _));

        JsonElement[] issuerClaims = identity
            .GetProperty("user_claims")
            .EnumerateArray()
            .Where(claim => claim.GetProperty("typ").GetString() == "iss")
            .ToArray();

        if (expectedIssuer is null)
        {
            Assert.Empty(issuerClaims);
        }
        else
        {
            Assert.Equal(
                expectedIssuer,
                Assert.Single(issuerClaims).GetProperty("val").GetString());
        }
    }

    [Fact]
    public async Task SupportsOnlyOfficialProviderRoutesAndFiltersPresets()
    {
        foreach (string provider in new[] { "aad", "facebook", "google", "x", "github", "apple" })
        {
            IAPIResponse response = await Page.APIRequest.GetAsync(
                new Uri(fixture.Emulator.BaseUri, $"/.auth/login/{provider}").AbsoluteUri);
            Assert.Equal(200, response.Status);
        }

        IAPIResponse twitter = await Page.APIRequest.GetAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/login/twitter").AbsoluteUri);
        IAPIResponse unknown = await Page.APIRequest.GetAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/login/unknown").AbsoluteUri);
        IAPIResponse mismatchedPreset = await Page.APIRequest.GetAsync(
            new Uri(
                fixture.Emulator.BaseUri,
                "/.auth/login/google?profile=alice-admin").AbsoluteUri);

        Assert.Equal(404, twitter.Status);
        Assert.Equal(404, unknown.Status);
        Assert.Equal(404, mismatchedPreset.Status);
    }

    [Theory]
    [InlineData(
        "aad",
        "https://login.microsoftonline.com/22222222-2222-2222-2222-222222222222/v2.0")]
    [InlineData("facebook", "")]
    [InlineData("google", "https://accounts.google.com")]
    [InlineData("x", "")]
    [InlineData("github", "")]
    [InlineData("apple", "https://appleid.apple.com")]
    public async Task LoginPageUsesOnlyVerifiedIssuerDefaults(
        string provider,
        string expectedIssuer)
    {
        await Page.GotoAsync(
            new Uri(fixture.Emulator.BaseUri, $"/.auth/login/{provider}").AbsoluteUri);

        await Expect(Page.GetByLabel("Issuer (iss)"))
            .ToHaveValueAsync(expectedIssuer);
    }

    [Fact]
    public async Task AadIssuerTracksTenantUntilExplicitlyCleared()
    {
        await Page.GotoAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);
        ILocator tenant = Page.GetByLabel("Tenant ID");
        ILocator issuer = Page.GetByLabel("Issuer (iss)");
        const string changedTenant = "33333333-3333-3333-3333-333333333333";
        const string finalTenant = "44444444-4444-4444-4444-444444444444";

        await tenant.FillAsync(changedTenant);
        await Expect(issuer).ToHaveValueAsync(
            $"https://login.microsoftonline.com/{changedTenant}/v2.0");

        await issuer.FillAsync(string.Empty);
        await tenant.FillAsync(finalTenant);
        await Expect(issuer).ToHaveValueAsync(string.Empty);
    }

    [Fact]
    public async Task ProviderChooserAndResetRestoreInitialForm()
    {
        await Page.GotoAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);
        int initialRoleCount = await Page.Locator("#roles [data-row]").CountAsync();
        int initialClaimCount = await Page.Locator("#claims [data-row]").CountAsync();

        await Page.GetByText("Change provider", new() { Exact = true }).ClickAsync();
        await Expect(Page.Locator(".provider-change nav a")).ToHaveCountAsync(6);
        await Expect(Page.Locator(".provider-change a[aria-current='page']"))
            .ToHaveTextAsync("Microsoft Entra ID");
        ILocator advancedSummary = Page.Locator(".advanced > summary");
        await Expect(advancedSummary).ToContainTextAsync("Optional");
        await advancedSummary.ClickAsync();
        await Expect(Page.Locator(".advanced")).ToHaveAttributeAsync("open", "");

        await Page.GetByLabel("Display name").FillAsync("Changed User");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add role" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add claim" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();

        await Expect(Page.GetByLabel("Display name")).ToHaveValueAsync("Alice Example");
        await Expect(Page.Locator("#roles [data-row]")).ToHaveCountAsync(initialRoleCount);
        await Expect(Page.Locator("#claims [data-row]")).ToHaveCountAsync(initialClaimCount);
        await Expect(Page.Locator(".advanced")).Not.ToHaveAttributeAsync("open", "");
    }

    [Fact]
    public async Task LoginFormDoesNotOverflowOnMobile()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/login/google").AbsoluteUri);

        bool hasHorizontalOverflow = await Page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");

        Assert.False(hasHorizontalOverflow);
    }

    [Fact]
    public async Task SampleDashboardDoesNotOverflowOnMobile()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync(new Uri(fixture.Emulator.BaseUri, "/").AbsoluteUri);

        bool hasHorizontalOverflow = await Page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");

        Assert.False(hasHorizontalOverflow);
        await Page.Locator(".sign-in-menu summary").ClickAsync();
        bool hasOpenMenuOverflow = await Page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.False(hasOpenMenuOverflow);

        await Expect(Page.GetByRole(
                AriaRole.Heading,
                new() { Name = "Identity diagnostics" }))
            .ToBeVisibleAsync();
        await Expect(Page.Locator("#authentication-status")).ToBeVisibleAsync();
        await Expect(Page.Locator("#encoded-principal")).ToBeVisibleAsync();
        await Expect(Page.Locator("#decoded-principal")).ToBeVisibleAsync();
        await Expect(Page.Locator(".endpoint-table tr")).ToHaveCountAsync(5);
    }
}
