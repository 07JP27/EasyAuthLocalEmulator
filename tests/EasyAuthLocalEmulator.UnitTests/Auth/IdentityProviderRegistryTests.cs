using EasyAuthLocalEmulator.Auth;

namespace EasyAuthLocalEmulator.UnitTests.Auth;

public sealed class IdentityProviderRegistryTests
{
    [Fact]
    public void ContainsOfficialBuiltInProviderRouteKeys()
    {
        string[] routeKeys = IdentityProviderRegistry.Default.Providers
            .Select(provider => provider.RouteKey)
            .ToArray();

        Assert.Equal(
            ["aad", "facebook", "google", "x", "github", "apple"],
            routeKeys);
        Assert.False(IdentityProviderRegistry.Default.TryGet("twitter", out _));
        Assert.False(IdentityProviderRegistry.Default.TryGet("AAD", out _));
    }

    [Theory]
    [InlineData("aad", "https://login.microsoftonline.com/tenant/v2.0")]
    [InlineData("google", "https://accounts.google.com")]
    [InlineData("apple", "https://appleid.apple.com")]
    public void ProvidesVerifiedIssuerDefaults(string routeKey, string expected)
    {
        IdentityProviderDefinition provider =
            IdentityProviderRegistry.Default.GetRequired(routeKey);

        Assert.Equal(expected, provider.ResolveDefaultIssuer("tenant"));
    }

    [Theory]
    [InlineData("facebook")]
    [InlineData("x")]
    [InlineData("github")]
    public void DoesNotGuessUnverifiedIssuerDefaults(string routeKey)
    {
        IdentityProviderDefinition provider =
            IdentityProviderRegistry.Default.GetRequired(routeKey);

        Assert.Null(provider.ResolveDefaultIssuer(null));
    }
}
