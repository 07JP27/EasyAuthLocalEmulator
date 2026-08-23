namespace EasyAuthLocalEmulator.Auth;

public sealed class UserProfile
{
    public UserProfile(
        string provider,
        string authenticationType,
        string displayName,
        string userName,
        string userId,
        string? tenantId,
        string? issuer,
        string nameClaimType,
        string roleClaimType,
        ClaimMappings claimMappings,
        IEnumerable<string> roles,
        IEnumerable<EasyAuthClaim> claims)
    {
        Provider = provider;
        AuthenticationType = authenticationType;
        DisplayName = displayName;
        UserName = userName;
        UserId = userId;
        TenantId = tenantId;
        Issuer = issuer;
        NameClaimType = nameClaimType;
        RoleClaimType = roleClaimType;
        ClaimMappings = claimMappings;
        Roles = Array.AsReadOnly(roles.ToArray());
        Claims = Array.AsReadOnly(claims.ToArray());
    }

    public string Provider { get; }

    public string AuthenticationType { get; }

    public string DisplayName { get; }

    public string UserName { get; }

    public string UserId { get; }

    public string? TenantId { get; }

    public string? Issuer { get; }

    public string NameClaimType { get; }

    public string RoleClaimType { get; }

    public ClaimMappings ClaimMappings { get; }

    public IReadOnlyList<string> Roles { get; }

    public IReadOnlyList<EasyAuthClaim> Claims { get; }
}
