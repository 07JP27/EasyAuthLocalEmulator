using EasyAuthLocalEmulator.Auth;

namespace EasyAuthLocalEmulator.UnitTests;

internal static class TestData
{
    internal static UserProfile CreateProfile(
        IEnumerable<string>? roles = null,
        IEnumerable<EasyAuthClaim>? claims = null,
        string provider = "aad",
        string? authenticationType = null,
        string? userName = null,
        string? userId = null,
        string? tenantId = null,
        string? issuer = null,
        ClaimMappings? mappings = null)
    {
        IdentityProviderDefinition definition =
            IdentityProviderRegistry.Default.GetRequired(provider);
        string effectiveTenantId = tenantId ??
            (provider == "aad"
                ? "22222222-2222-2222-2222-222222222222"
                : string.Empty);

        return new UserProfile(
            provider,
            authenticationType ?? definition.DefaultAuthenticationType,
            "Alice Example",
            userName ?? "alice@example.com",
            userId ?? (provider == "aad"
                ? "11111111-1111-1111-1111-111111111111"
                : "provider-user-id"),
            string.IsNullOrEmpty(effectiveTenantId) ? null : effectiveTenantId,
            issuer is null
                ? definition.ResolveDefaultIssuer(effectiveTenantId)
                : (issuer.Length == 0 ? null : issuer),
            definition.DefaultClaimMappings.DisplayName ?? "name",
            "roles",
            mappings ?? definition.DefaultClaimMappings,
            roles ?? ["Admin", "Reader"],
            claims ?? [new EasyAuthClaim("department", "Engineering")]);
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }

    internal void Advance(TimeSpan duration)
    {
        _utcNow = _utcNow.Add(duration);
    }
}
