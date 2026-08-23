using System.Text.Json.Serialization;

namespace EasyAuthLocalEmulator.Auth;

public sealed record ClientPrincipal(
    [property: JsonPropertyName("auth_typ")] string AuthenticationType,
    [property: JsonPropertyName("claims")] IReadOnlyList<EasyAuthClaim> Claims,
    [property: JsonPropertyName("name_typ")] string NameClaimType,
    [property: JsonPropertyName("role_typ")] string RoleClaimType);

public sealed record EasyAuthIdentity(
    [property: JsonPropertyName("provider_name")] string ProviderName,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_claims")] IReadOnlyList<EasyAuthClaim> UserClaims,
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("authentication_token")] string? AuthenticationToken,
    [property: JsonPropertyName("expires_on")] string? ExpiresOn,
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken);

public sealed record PrincipalSnapshot(
    ClientPrincipal Principal,
    EasyAuthIdentity Identity,
    string EncodedPrincipal,
    IReadOnlyDictionary<string, string> Headers);
