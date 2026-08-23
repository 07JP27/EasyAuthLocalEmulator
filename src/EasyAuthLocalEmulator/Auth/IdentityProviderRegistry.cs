using System.Collections.ObjectModel;

namespace EasyAuthLocalEmulator.Auth;

public sealed record ClaimMappings(
    string? DisplayName,
    string? UserName,
    string? UserId,
    string? TenantId);

public sealed record IdentityProviderDefinition(
    string RouteKey,
    string DisplayName,
    string DefaultAuthenticationType,
    string? DefaultIssuerTemplate,
    bool RequiresTenantId,
    ClaimMappings DefaultClaimMappings)
{
    public string? ResolveDefaultIssuer(string? tenantId)
    {
        if (DefaultIssuerTemplate is null)
        {
            return null;
        }

        return DefaultIssuerTemplate.Replace(
            "{tenantId}",
            tenantId ?? string.Empty,
            StringComparison.Ordinal);
    }
}

public sealed class IdentityProviderRegistry
{
    private const string ObjectIdentifierClaim =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string TenantIdentifierClaim =
        "http://schemas.microsoft.com/identity/claims/tenantid";

    private readonly ReadOnlyDictionary<string, IdentityProviderDefinition> _byRouteKey;

    public IdentityProviderRegistry()
    {
        IdentityProviderDefinition[] providers =
        [
            new(
                "aad",
                "Microsoft Entra ID",
                "aad",
                "https://login.microsoftonline.com/{tenantId}/v2.0",
                RequiresTenantId: true,
                new ClaimMappings(
                    "name",
                    "preferred_username",
                    ObjectIdentifierClaim,
                    TenantIdentifierClaim)),
            new(
                "facebook",
                "Facebook",
                "facebook",
                DefaultIssuerTemplate: null,
                RequiresTenantId: false,
                new ClaimMappings("name", "email", "id", TenantId: null)),
            new(
                "google",
                "Google",
                "google",
                "https://accounts.google.com",
                RequiresTenantId: false,
                new ClaimMappings("name", "email", "sub", TenantId: null)),
            new(
                "x",
                "X",
                "x",
                DefaultIssuerTemplate: null,
                RequiresTenantId: false,
                new ClaimMappings("name", "username", "id", TenantId: null)),
            new(
                "github",
                "GitHub",
                "github",
                DefaultIssuerTemplate: null,
                RequiresTenantId: false,
                new ClaimMappings("name", "login", "id", TenantId: null)),
            new(
                "apple",
                "Apple",
                "apple",
                "https://appleid.apple.com",
                RequiresTenantId: false,
                new ClaimMappings("name", "email", "sub", TenantId: null))
        ];

        Providers = Array.AsReadOnly(providers);
        _byRouteKey = new ReadOnlyDictionary<string, IdentityProviderDefinition>(
            providers.ToDictionary(provider => provider.RouteKey, StringComparer.Ordinal));
    }

    public static IdentityProviderRegistry Default { get; } = new();

    public IReadOnlyList<IdentityProviderDefinition> Providers { get; }

    public bool TryGet(
        string routeKey,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out IdentityProviderDefinition? provider)
    {
        return _byRouteKey.TryGetValue(routeKey, out provider);
    }

    public IdentityProviderDefinition GetRequired(string routeKey)
    {
        return TryGet(routeKey, out IdentityProviderDefinition? provider)
            ? provider
            : throw new ProfileValidationException(
                $"provider must be one of: {string.Join(", ", Providers.Select(item => item.RouteKey))}.",
                "provider");
    }
}
