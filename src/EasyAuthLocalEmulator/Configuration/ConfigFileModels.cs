using System.Text.Json.Serialization;
using EasyAuthLocalEmulator.Auth;

namespace EasyAuthLocalEmulator.Configuration;

public sealed class EmulatorConfigurationFile
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    [JsonPropertyName("sessionLifetime")]
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(8);

    [JsonPropertyName("profiles")]
    public Dictionary<string, ProfileConfiguration>? Profiles { get; init; } = [];
}

public sealed record ProfileConfiguration
{
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("authenticationType")]
    public string? AuthenticationType { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    [JsonPropertyName("upn")]
    public string? Upn { get; init; }

    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("issuer")]
    public string? Issuer { get; init; }

    [JsonPropertyName("nameClaimType")]
    public string? NameClaimType { get; init; }

    [JsonPropertyName("roleClaimType")]
    public string? RoleClaimType { get; init; }

    [JsonPropertyName("claimMappings")]
    public ClaimMappingsConfiguration? ClaimMappings { get; init; }

    [JsonPropertyName("roles")]
    public List<string?>? Roles { get; init; } = [];

    [JsonPropertyName("claims")]
    public List<EasyAuthClaim?>? Claims { get; init; } = [];
}

public sealed record ClaimMappingsConfiguration
{
    [JsonPropertyName("displayName")]
    public OptionalString DisplayName { get; init; }

    [JsonPropertyName("userName")]
    public OptionalString UserName { get; init; }

    [JsonPropertyName("userId")]
    public OptionalString UserId { get; init; }

    [JsonPropertyName("tenantId")]
    public OptionalString TenantId { get; init; }
}
