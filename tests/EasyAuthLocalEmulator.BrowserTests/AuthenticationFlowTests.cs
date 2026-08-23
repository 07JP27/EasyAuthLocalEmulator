using System.Text.Json;
using System.Text.RegularExpressions;
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
                new() { Name = "Easy Auth Local Emulator", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(Page).ToHaveTitleAsync("Sign in · Easy Auth Local Emulator");
        await Expect(Page.Locator(".login-platform-name"))
            .ToHaveTextAsync("For: Azure App Service");
        await Expect(Page.GetByText("Local authentication", new() { Exact = true }))
            .ToHaveCountAsync(0);
        ILocator providerInput = Page.GetByRole(
            AriaRole.Textbox,
            new() { Name = "Provider", Exact = true });
        await Expect(providerInput).ToHaveValueAsync("aad");
        await Expect(providerInput).ToHaveAttributeAsync("readonly", "");
        await Expect(Page.Locator("#provider-name + .help")).ToHaveCountAsync(0);
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
            "/.auth/logout?post_logout_redirect_uri=/");
        await signOut.ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            new Uri(fixture.Emulator.BaseUri, "/").AbsoluteUri);
        await Expect(Page.Locator("#authentication-status")).ToHaveTextAsync("Anonymous");
    }

    [Fact]
    public async Task LoginRequiresAntiforgeryAndShowsUserIdValidationInline()
    {
        IAPIResponse missingTokenResponse = await Page.APIRequest.PostAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);
        Assert.Equal(400, missingTokenResponse.Status);

        await Page.GotoAsync(new Uri(fixture.Emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);
        ILocator userId = Page.GetByLabel("User ID");
        await userId.FillAsync("not-a-guid");
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();

        await Expect(Page.Locator("#user-id-error"))
            .ToContainTextAsync("userId must be a GUID");
        await Expect(userId).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(userId).ToHaveClassAsync(new Regex("input-validation-error"));
        await Expect(Page.Locator(".form-message.validation-summary-errors"))
            .ToHaveCountAsync(0);
        Assert.Contains("/.auth/login/aad", Page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatingAndAdvancedErrorsAppearBesideTheirInputs()
    {
        await Page.GotoAsync(new Uri(fixture.Emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Add role" }).ClickAsync();
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();

        ILocator roleRow = Page.Locator("#roles [data-row]").Last;
        ILocator roleInput = roleRow.Locator("input");
        await Expect(roleRow.Locator(".field-validation-error"))
            .ToHaveTextAsync("Enter a role, or remove this row.");
        await Expect(roleInput).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(roleInput).ToHaveClassAsync(new Regex("input-validation-error"));
        await ExpectControlsToAlignAsync(
            roleInput,
            roleRow.GetByRole(AriaRole.Button, new() { Name = "Remove" }));
        await Expect(Page.Locator(".form-row").First.Locator(".field-validation-error"))
            .ToHaveCountAsync(0);
        await Expect(Page.Locator(".form-message.validation-summary-errors"))
            .ToHaveCountAsync(0);

        await roleInput.FillAsync("Reader");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add claim" }).ClickAsync();
        await Page.Locator("#claims input[name$='.Value']").Last.FillAsync("LocalAuth");
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();

        ILocator claimRow = Page.Locator("#claims [data-row]").Last;
        ILocator claimType = claimRow.Locator("input[name$='.Type']");
        await Expect(claimRow.Locator(".field-validation-error"))
            .ToHaveTextAsync("Enter a claim type, or remove this row.");
        await Expect(claimType).ToHaveAttributeAsync("aria-invalid", "true");
        await ExpectControlsToAlignAsync(
            claimType,
            claimRow.Locator("input[name$='.Value']"),
            claimRow.GetByRole(AriaRole.Button, new() { Name = "Remove" }));

        await claimType.FillAsync("project");
        ILocator claimValue = claimRow.Locator("input[name$='.Value']");
        await claimValue.FillAsync(new string('x', 4097));
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();

        claimRow = Page.Locator("#claims [data-row]").Last;
        claimType = claimRow.Locator("input[name$='.Type']");
        claimValue = claimRow.Locator("input[name$='.Value']");
        await Expect(claimRow.Locator("[data-error-for='claim-value']"))
            .ToHaveTextAsync("Claim value cannot exceed 4096 characters.");
        await ExpectControlsToAlignAsync(
            claimType,
            claimValue,
            claimRow.GetByRole(AriaRole.Button, new() { Name = "Remove" }));

        await claimValue.FillAsync("LocalAuth");
        await Page.Locator(".advanced > summary").ClickAsync();
        ILocator authenticationType = Page.GetByLabel("Authentication type");
        await authenticationType.FillAsync(string.Empty);
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();

        await Expect(Page.Locator(".advanced")).ToHaveAttributeAsync("open", "open");
        await Expect(Page.Locator("#authentication-type-error"))
            .ToContainTextAsync("Authentication type");
        await Expect(authenticationType).ToHaveAttributeAsync("aria-invalid", "true");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();
        await Expect(Page.Locator(".advanced")).ToHaveAttributeAsync("open", "open");

        await authenticationType.FillAsync("aad");
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();
        await Expect(Page).ToHaveURLAsync(
            new Uri(fixture.Emulator.BaseUri, "/").AbsoluteUri);
    }

    [Fact]
    public async Task GlobalValidationAppearsBesideSignInActions()
    {
        await Page.GotoAsync(
            new Uri(
                fixture.Emulator.BaseUri,
                "/.auth/login/aad?post_login_redirect_uri=/").AbsoluteUri);

        await Page.Locator("#PostLoginRedirectUri").EvaluateAsync(
            "(element) => { element.value = 'https://example.com'; }");
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();

        ILocator summary = Page.Locator(".form-message.validation-summary-errors");
        await Expect(summary)
            .ToContainTextAsync("post-login redirect URI must be a local path");
        Assert.True(await summary.EvaluateAsync<bool>(
            "(element) => element.nextElementSibling?.classList.contains('form-actions') === true"));
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
            await Page.GotoAsync(
                new Uri(fixture.Emulator.BaseUri, $"/.auth/login/{provider}").AbsoluteUri);
            await Expect(Page.GetByRole(
                    AriaRole.Textbox,
                    new() { Name = "Provider", Exact = true }))
                .ToHaveValueAsync(provider);
            await Expect(Page.Locator("#provider-name + .help")).ToHaveCountAsync(0);
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
    public async Task ProviderIsReadOnlyAndResetRestoresInitialForm()
    {
        await Page.GotoAsync(
            new Uri(fixture.Emulator.BaseUri, "/.auth/login/aad").AbsoluteUri);
        int initialRoleCount = await Page.Locator("#roles [data-row]").CountAsync();
        int initialClaimCount = await Page.Locator("#claims [data-row]").CountAsync();

        await Expect(Page.GetByRole(
                AriaRole.Textbox,
                new() { Name = "Provider", Exact = true }))
            .ToHaveValueAsync("aad");
        await Expect(Page.GetByText("Change provider", new() { Exact = true }))
            .ToHaveCountAsync(0);
        await Expect(Page.Locator(".auth-form a[href^='/.auth/login/']"))
            .ToHaveCountAsync(0);
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
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add role" }).ClickAsync();
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = "Sign in", Exact = true })
            .ClickAsync();
        await Expect(Page.Locator("#roles .field-validation-error")).ToBeVisibleAsync();
        ILocator roleRow = Page.Locator("#roles [data-row]").Last;
        await Expect(roleRow.Locator(".field-validation-error"))
            .ToHaveTextAsync("Enter a role, or remove this row.");
        await ExpectControlsToAlignAsync(
            roleRow.Locator("input"),
            roleRow.GetByRole(AriaRole.Button, new() { Name = "Remove" }));

        bool hasHorizontalOverflow = await Page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");

        Assert.False(hasHorizontalOverflow);
    }

    private static async Task ExpectControlsToAlignAsync(
        params ILocator[] controls)
    {
        LocatorBoundingBoxResult? firstBox = await controls[0].BoundingBoxAsync();
        Assert.NotNull(firstBox);

        foreach (ILocator control in controls.Skip(1))
        {
            LocatorBoundingBoxResult? box = await control.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.InRange(Math.Abs(box.Y - firstBox.Y), 0, 0.5);
            Assert.InRange(Math.Abs(box.Height - firstBox.Height), 0, 0.5);
        }
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
        await Expect(Page.Locator(".app-name"))
            .ToHaveTextAsync("Easy Auth Local emulator sample app");
        await Expect(Page.GetByText("Local emulator", new() { Exact = true }))
            .ToHaveCountAsync(0);
        await Expect(Page.GetByText(
                "Inspect the identity and headers received by this application.",
                new() { Exact = true }))
            .ToHaveCountAsync(0);
        await Expect(Page.GetByText(
                "Connected through the local Easy Auth emulator",
                new() { Exact = false }))
            .ToHaveCountAsync(0);
        await Expect(Page.Locator("#authentication-status")).ToBeVisibleAsync();
        await Expect(Page.Locator("#encoded-principal")).ToBeVisibleAsync();
        await Expect(Page.Locator("#decoded-principal")).ToBeVisibleAsync();
        await Expect(Page.Locator(".endpoint-table tr")).ToHaveCountAsync(5);
    }
}
