using EasyAuthLocalEmulator.Configuration;

namespace EasyAuthLocalEmulator.Auth;

public static class ProfileValidator
{
    public const int MaximumRoles = 100;
    public const int MaximumClaims = 256;

    public static UserProfile Create(
        ProfileConfiguration configuration,
        IdentityProviderRegistry? registry = null)
    {
        IdentityProviderRegistry providers = registry ?? IdentityProviderRegistry.Default;
        string providerKey = OptionalOrDefault(configuration.Provider, "provider", "aad", 64);
        IdentityProviderDefinition provider = providers.GetRequired(providerKey);
        string authenticationType = OptionalOrDefault(
            configuration.AuthenticationType,
            "authenticationType",
            provider.DefaultAuthenticationType,
            128);
        string displayName = Required(
            configuration.DisplayName,
            "displayName",
            maximumLength: 256);
        string userName = ResolveUserName(configuration);
        string userId = Required(configuration.UserId, "userId", maximumLength: 512);
        string? tenantId = NormalizeOptional(configuration.TenantId, "tenantId", 512);

        if (provider.RequiresTenantId)
        {
            userId = NormalizeGuid(userId, "userId");
            tenantId = NormalizeGuid(
                tenantId ?? throw new ProfileValidationException(
                    "tenantId is required.",
                    "tenantId"),
                "tenantId");
        }

        ClaimMappings mappings = ResolveMappings(provider, configuration.ClaimMappings);
        string nameClaimType = OptionalOrDefault(
            configuration.NameClaimType,
            "nameClaimType",
            mappings.DisplayName ?? "name",
            512);
        string roleClaimType = OptionalOrDefault(
            configuration.RoleClaimType,
            "roleClaimType",
            "roles",
            512);
        string[] roles = ValidateRoles(configuration.Roles);
        EasyAuthClaim[] customClaims = ValidateClaims(configuration.Claims);
        (string? issuer, EasyAuthClaim[] claims) = ResolveIssuer(
            configuration.Issuer,
            provider.ResolveDefaultIssuer(tenantId),
            customClaims);

        return new UserProfile(
            provider.RouteKey,
            authenticationType,
            displayName,
            userName,
            userId,
            tenantId,
            issuer,
            nameClaimType,
            roleClaimType,
            mappings,
            roles,
            claims);
    }

    internal static void RejectControlCharacters(string value, string field)
    {
        if (value.Any(char.IsControl))
        {
            throw new ProfileValidationException(
                $"{field} cannot contain control characters.",
                field);
        }
    }

    private static string ResolveUserName(ProfileConfiguration configuration)
    {
        if (configuration.UserName is not null && configuration.Upn is not null)
        {
            throw new ProfileValidationException(
                "userName and the legacy upn property cannot both be specified.",
                "userName");
        }

        return Required(
            configuration.UserName ?? configuration.Upn,
            "userName",
            maximumLength: 320);
    }

    private static ClaimMappings ResolveMappings(
        IdentityProviderDefinition provider,
        ClaimMappingsConfiguration? configuration)
    {
        if (configuration is null)
        {
            return provider.DefaultClaimMappings;
        }

        return new ClaimMappings(
            ResolveMapping(
                configuration.DisplayName,
                provider.DefaultClaimMappings.DisplayName,
                "claimMappings.displayName"),
            ResolveMapping(
                configuration.UserName,
                provider.DefaultClaimMappings.UserName,
                "claimMappings.userName"),
            ResolveMapping(
                configuration.UserId,
                provider.DefaultClaimMappings.UserId,
                "claimMappings.userId"),
            ResolveMapping(
                configuration.TenantId,
                provider.DefaultClaimMappings.TenantId,
                "claimMappings.tenantId"));
    }

    private static string? ResolveMapping(
        OptionalString mapping,
        string? defaultValue,
        string field)
    {
        if (!mapping.IsSpecified)
        {
            return defaultValue;
        }

        if (string.IsNullOrWhiteSpace(mapping.Value))
        {
            return null;
        }

        return Required(mapping.Value, field, 512);
    }

    private static (string? Issuer, EasyAuthClaim[] Claims) ResolveIssuer(
        string? configuredIssuer,
        string? defaultIssuer,
        EasyAuthClaim[] claims)
    {
        EasyAuthClaim[] issuerClaims = claims
            .Where(claim => claim.Type.Equals("iss", StringComparison.Ordinal))
            .ToArray();

        if (issuerClaims.Length > 1)
        {
            throw new ProfileValidationException(
                "claims cannot contain more than one iss claim.",
                "claims");
        }

        if (configuredIssuer is not null && issuerClaims.Length > 0)
        {
            throw new ProfileValidationException(
                "issuer and a custom iss claim cannot both be specified.",
                "issuer");
        }

        string? issuer;
        if (configuredIssuer is not null)
        {
            issuer = configuredIssuer.Length == 0
                ? null
                : ValidateIssuer(configuredIssuer);
        }
        else if (issuerClaims.Length == 1)
        {
            issuer = ValidateIssuer(issuerClaims[0].Value);
        }
        else
        {
            issuer = defaultIssuer;
        }

        return (
            issuer,
            claims.Where(claim => !claim.Type.Equals("iss", StringComparison.Ordinal)).ToArray());
    }

    private static string ValidateIssuer(string issuer)
    {
        string normalized = Required(issuer, "issuer", 2048);

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ProfileValidationException(
                "issuer must be an absolute HTTP or HTTPS URI without user info or a fragment.",
                "issuer");
        }

        return normalized;
    }

    private static string[] ValidateRoles(List<string?>? configuredRoles)
    {
        if (configuredRoles is null)
        {
            throw new ProfileValidationException("roles cannot be null.", "roles");
        }

        if (configuredRoles.Count > MaximumRoles)
        {
            throw new ProfileValidationException(
                $"roles cannot contain more than {MaximumRoles} entries.",
                "roles");
        }

        return configuredRoles
            .Select((role, index) => Required(role, $"roles[{index}]", 256))
            .ToArray();
    }

    private static EasyAuthClaim[] ValidateClaims(List<EasyAuthClaim?>? configuredClaims)
    {
        if (configuredClaims is null)
        {
            throw new ProfileValidationException("claims cannot be null.", "claims");
        }

        if (configuredClaims.Count > MaximumClaims)
        {
            throw new ProfileValidationException(
                $"claims cannot contain more than {MaximumClaims} entries.",
                "claims");
        }

        return configuredClaims
            .Select((claim, index) => ValidateClaim(claim, index))
            .ToArray();
    }

    private static EasyAuthClaim ValidateClaim(EasyAuthClaim? claim, int index)
    {
        if (claim is null)
        {
            throw new ProfileValidationException(
                $"claims[{index}] cannot be null.",
                $"claims[{index}]");
        }

        string type = Required(claim.Type, $"claims[{index}].typ", 512);
        string value = claim.Value
            ?? throw new ProfileValidationException(
                $"claims[{index}].val is required.",
                $"claims[{index}].val");

        RejectControlCharacters(value, $"claims[{index}].val");

        if (value.Length > 4096)
        {
            throw new ProfileValidationException(
                $"claims[{index}].val cannot exceed 4096 characters.",
                $"claims[{index}].val");
        }

        return new EasyAuthClaim(type, value);
    }

    private static string NormalizeGuid(string value, string field)
    {
        return Guid.TryParse(value, out Guid parsed)
            ? parsed.ToString("D")
            : throw new ProfileValidationException(
                $"{field} must be a GUID for provider aad.",
                field);
    }

    private static string OptionalOrDefault(
        string? value,
        string field,
        string defaultValue,
        int maximumLength)
    {
        return value is null
            ? defaultValue
            : Required(value, field, maximumLength);
    }

    private static string? NormalizeOptional(string? value, string field, int maximumLength)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, field, maximumLength);
    }

    private static string Required(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ProfileValidationException($"{field} is required.", field);
        }

        string normalized = value.Trim();
        RejectControlCharacters(normalized, field);

        if (normalized.Length > maximumLength)
        {
            throw new ProfileValidationException(
                $"{field} cannot exceed {maximumLength} characters.",
                field);
        }

        return normalized;
    }
}
