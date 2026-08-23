using System.Text;
using System.Text.Json;
using EasyAuthLocalEmulator.Auth;

namespace EasyAuthLocalEmulator.UnitTests.Auth;

public sealed class PrincipalBuilderTests
{
    private readonly PrincipalBuilder _builder = new();

    [Fact]
    public void BuildsAppServicePrincipalAndHeaders()
    {
        UserProfile profile = TestData.CreateProfile(
            roles: ["Admin", "Admin"],
            claims:
            [
                new EasyAuthClaim("department", "Engineering"),
                new EasyAuthClaim("department", "Research"),
                new EasyAuthClaim("greeting", "こんにちは")
            ]);

        PrincipalSnapshot snapshot = _builder.Build(profile);
        string json = Encoding.UTF8.GetString(
            Convert.FromBase64String(snapshot.EncodedPrincipal));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement claims = root.GetProperty("claims");

        Assert.Equal("aad", root.GetProperty("auth_typ").GetString());
        Assert.Equal("name", root.GetProperty("name_typ").GetString());
        Assert.Equal("roles", root.GetProperty("role_typ").GetString());
        Assert.Equal(10, claims.GetArrayLength());
        Assert.Equal(2, claims.EnumerateArray().Count(
            claim => claim.GetProperty("typ").GetString() == "department"));
        Assert.Equal(
            "11111111-1111-1111-1111-111111111111",
            snapshot.Headers[EasyAuthHeaderNames.PrincipalId]);
        Assert.Equal("alice@example.com", snapshot.Headers[EasyAuthHeaderNames.PrincipalName]);
        Assert.Equal("aad", snapshot.Headers[EasyAuthHeaderNames.IdentityProvider]);
        Assert.Equal("aad", snapshot.Identity.ProviderName);
        Assert.Contains(
            snapshot.Identity.UserClaims,
            claim =>
                claim.Type == "iss" &&
                claim.Value ==
                "https://login.microsoftonline.com/22222222-2222-2222-2222-222222222222/v2.0");
        Assert.Same(snapshot.Principal.Claims, snapshot.Identity.UserClaims);
    }

    [Fact]
    public void RejectsPrincipalLargerThanConfiguredLimit()
    {
        EasyAuthClaim[] claims = Enumerable.Range(0, 32)
            .Select(index => new EasyAuthClaim($"claim-{index}", new string('x', 4096)))
            .ToArray();
        UserProfile profile = TestData.CreateProfile(claims: claims);

        ProfileValidationException exception = Assert.Throws<ProfileValidationException>(() =>
            _builder.Build(profile));

        Assert.Contains("cannot exceed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsControlCharactersAtHeaderBoundary()
    {
        UserProfile profile = new(
            "aad",
            "aad",
            "Alice Example",
            "alice@example.com\r\nX-Injected: value",
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222",
            "https://login.microsoftonline.com/tenant/v2.0",
            "name",
            "roles",
            IdentityProviderRegistry.Default.GetRequired("aad").DefaultClaimMappings,
            [],
            []);

        Assert.Throws<ProfileValidationException>(() => _builder.Build(profile));
    }

    [Theory]
    [InlineData("aad", "aad")]
    [InlineData("facebook", "facebook")]
    [InlineData("google", "google")]
    [InlineData("x", "x")]
    [InlineData("github", "github")]
    [InlineData("apple", "apple")]
    public void UsesProviderAuthenticationTypeAcrossContracts(
        string provider,
        string authenticationType)
    {
        UserProfile profile = TestData.CreateProfile(
            provider: provider,
            authenticationType: authenticationType);

        PrincipalSnapshot snapshot = _builder.Build(profile);

        Assert.Equal(authenticationType, snapshot.Principal.AuthenticationType);
        Assert.Equal(authenticationType, snapshot.Identity.ProviderName);
        Assert.Equal(
            authenticationType,
            snapshot.Headers[EasyAuthHeaderNames.IdentityProvider]);
    }

    [Fact]
    public void SupportsTwitterAuthenticationTypeOverrideForX()
    {
        UserProfile profile = TestData.CreateProfile(
            provider: "x",
            authenticationType: "twitter",
            issuer: string.Empty);

        PrincipalSnapshot snapshot = _builder.Build(profile);

        Assert.Equal("twitter", snapshot.Principal.AuthenticationType);
        Assert.Equal("twitter", snapshot.Identity.ProviderName);
        Assert.Equal(
            "twitter",
            snapshot.Headers[EasyAuthHeaderNames.IdentityProvider]);
    }

    [Fact]
    public void OmitsDisabledMappingsAndIssuer()
    {
        UserProfile profile = TestData.CreateProfile(
            provider: "github",
            issuer: string.Empty,
            mappings: new ClaimMappings(
                DisplayName: "name",
                UserName: null,
                UserId: "id",
                TenantId: null));

        PrincipalSnapshot snapshot = _builder.Build(profile);

        Assert.DoesNotContain(snapshot.Principal.Claims, claim => claim.Type == "login");
        Assert.DoesNotContain(snapshot.Principal.Claims, claim => claim.Type == "iss");
        Assert.Contains(snapshot.Principal.Claims, claim => claim.Type == "id");
    }

    [Fact]
    public void DoesNotSerializeTopLevelIssuerInMeIdentity()
    {
        PrincipalSnapshot snapshot = _builder.Build(TestData.CreateProfile());

        string json = JsonSerializer.Serialize(snapshot.Identity);

        Assert.DoesNotContain("\"issuer\"", json, StringComparison.Ordinal);
        Assert.Contains("\"typ\":\"iss\"", json, StringComparison.Ordinal);
    }
}
