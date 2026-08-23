using EasyAuthLocalEmulator.Auth;
using EasyAuthLocalEmulator.Configuration;

namespace EasyAuthLocalEmulator.UnitTests.Auth;

public sealed class ProfileValidatorTests
{
    [Theory]
    [InlineData("alice@example.com\r\nX-Injected: value")]
    [InlineData("alice\u0000@example.com")]
    public void RejectsControlCharactersInUpn(string upn)
    {
        ProfileConfiguration configuration = CreateValidConfiguration() with
        {
            Upn = upn
        };

        ProfileValidationException exception = Assert.Throws<ProfileValidationException>(() =>
            ProfileValidator.Create(configuration));

        Assert.Contains("control characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsControlCharactersInClaimValue()
    {
        ProfileConfiguration configuration = CreateValidConfiguration() with
        {
            Claims = [new EasyAuthClaim("department", "Engineering\nResearch")]
        };

        Assert.Throws<ProfileValidationException>(() =>
            ProfileValidator.Create(configuration));
    }

    [Theory]
    [InlineData("userId")]
    [InlineData("tenantId")]
    public void RejectsNonGuidIdentifiers(string field)
    {
        ProfileConfiguration valid = CreateValidConfiguration();
        ProfileConfiguration configuration = field == "userId"
            ? valid with { UserId = "not-a-guid" }
            : valid with { TenantId = "not-a-guid" };

        ProfileValidationException exception = Assert.Throws<ProfileValidationException>(() =>
            ProfileValidator.Create(configuration));

        Assert.Contains(field, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("facebook")]
    [InlineData("google")]
    [InlineData("x")]
    [InlineData("github")]
    [InlineData("apple")]
    public void AcceptsNonGuidUserIdForNonAadProviders(string provider)
    {
        ProfileConfiguration configuration = CreateValidConfiguration() with
        {
            Provider = provider,
            UserId = "provider-specific-user-id",
            TenantId = null,
            UserName = "alice@example.com",
            Upn = null
        };

        UserProfile profile = ProfileValidator.Create(configuration);

        Assert.Equal(provider, profile.Provider);
        Assert.Equal("provider-specific-user-id", profile.UserId);
    }

    [Fact]
    public void PreservesLegacyAadDefaults()
    {
        UserProfile profile = ProfileValidator.Create(CreateValidConfiguration());

        Assert.Equal("aad", profile.Provider);
        Assert.Equal("alice@example.com", profile.UserName);
        Assert.Equal(
            "https://login.microsoftonline.com/22222222-2222-2222-2222-222222222222/v2.0",
            profile.Issuer);
    }

    [Fact]
    public void NormalizesCustomIssClaimIntoIssuer()
    {
        ProfileConfiguration configuration = CreateValidConfiguration() with
        {
            Claims =
            [
                new EasyAuthClaim("iss", "https://issuer.example"),
                new EasyAuthClaim("department", "Engineering")
            ]
        };

        UserProfile profile = ProfileValidator.Create(configuration);

        Assert.Equal("https://issuer.example", profile.Issuer);
        Assert.DoesNotContain(profile.Claims, claim => claim.Type == "iss");
    }

    [Fact]
    public void RejectsIssuerAndCustomIssClaimTogether()
    {
        ProfileConfiguration configuration = CreateValidConfiguration() with
        {
            Issuer = "https://issuer.example",
            Claims = [new EasyAuthClaim("iss", "https://other.example")]
        };

        Assert.Throws<ProfileValidationException>(() =>
            ProfileValidator.Create(configuration));
    }

    [Fact]
    public void EmptyIssuerSuppressesProviderDefault()
    {
        UserProfile profile = ProfileValidator.Create(
            CreateValidConfiguration() with { Issuer = string.Empty });

        Assert.Null(profile.Issuer);
    }

    [Fact]
    public void RejectsUserNameAndLegacyUpnTogether()
    {
        ProfileConfiguration configuration = CreateValidConfiguration() with
        {
            UserName = "alice@example.com"
        };

        Assert.Throws<ProfileValidationException>(() =>
            ProfileValidator.Create(configuration));
    }

    [Fact]
    public void SupportsClaimMappingOverridesAndDisabling()
    {
        ProfileConfiguration configuration = CreateValidConfiguration() with
        {
            ClaimMappings = new ClaimMappingsConfiguration
            {
                DisplayName = OptionalString.Specified("display_name"),
                UserName = OptionalString.Specified(null),
                UserId = OptionalString.Specified("subject")
            }
        };

        UserProfile profile = ProfileValidator.Create(configuration);

        Assert.Equal("display_name", profile.ClaimMappings.DisplayName);
        Assert.Null(profile.ClaimMappings.UserName);
        Assert.Equal("subject", profile.ClaimMappings.UserId);
        Assert.Equal(
            "http://schemas.microsoft.com/identity/claims/tenantid",
            profile.ClaimMappings.TenantId);
    }

    private static ProfileConfiguration CreateValidConfiguration()
    {
        return new ProfileConfiguration
        {
            DisplayName = "Alice Example",
            Upn = "alice@example.com",
            UserId = "11111111-1111-1111-1111-111111111111",
            TenantId = "22222222-2222-2222-2222-222222222222",
            Roles = ["Admin"],
            Claims = [new EasyAuthClaim("department", "Engineering")]
        };
    }
}
