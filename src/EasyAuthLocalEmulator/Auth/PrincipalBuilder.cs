using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace EasyAuthLocalEmulator.Auth;

public sealed class PrincipalBuilder
{
    public const int MaximumPrincipalJsonBytes = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public PrincipalSnapshot Build(UserProfile profile)
    {
        ValidateHeaderValue(profile.AuthenticationType, "authenticationType");
        ValidateHeaderValue(profile.UserId, "userId");
        ValidateHeaderValue(profile.UserName, "userName");

        IReadOnlyList<EasyAuthClaim> claims = BuildClaims(profile);
        ClientPrincipal principal = new(
            profile.AuthenticationType,
            claims,
            profile.NameClaimType,
            profile.RoleClaimType);
        byte[] principalJson = JsonSerializer.SerializeToUtf8Bytes(principal, SerializerOptions);

        if (principalJson.Length > MaximumPrincipalJsonBytes)
        {
            throw new ProfileValidationException(
                $"The generated principal cannot exceed {MaximumPrincipalJsonBytes} UTF-8 bytes.");
        }

        string encodedPrincipal = Convert.ToBase64String(principalJson);
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            [EasyAuthHeaderNames.Principal] = encodedPrincipal,
            [EasyAuthHeaderNames.PrincipalId] = profile.UserId,
            [EasyAuthHeaderNames.PrincipalName] = profile.UserName,
            [EasyAuthHeaderNames.IdentityProvider] = profile.AuthenticationType
        };
        EasyAuthIdentity identity = new(
            profile.AuthenticationType,
            profile.UserId,
            claims,
            AccessToken: null,
            AuthenticationToken: null,
            ExpiresOn: null,
            IdToken: null,
            RefreshToken: null);

        return new PrincipalSnapshot(
            principal,
            identity,
            encodedPrincipal,
            new ReadOnlyDictionary<string, string>(headers));
    }

    public string DecodePrincipalJson(string encodedPrincipal)
    {
        byte[] bytes = Convert.FromBase64String(encodedPrincipal);
        return Encoding.UTF8.GetString(bytes);
    }

    private static ReadOnlyCollection<EasyAuthClaim> BuildClaims(UserProfile profile)
    {
        List<EasyAuthClaim> claims = [];
        AddMappedClaim(
            claims,
            profile.ClaimMappings.DisplayName,
            profile.DisplayName);
        AddMappedClaim(
            claims,
            profile.ClaimMappings.UserName,
            profile.UserName);
        AddMappedClaim(
            claims,
            profile.ClaimMappings.UserId,
            profile.UserId);
        AddMappedClaim(
            claims,
            profile.ClaimMappings.TenantId,
            profile.TenantId);

        if (!string.IsNullOrEmpty(profile.Issuer))
        {
            claims.Add(new EasyAuthClaim("iss", profile.Issuer));
        }

        claims.AddRange(
            profile.Roles.Select(role => new EasyAuthClaim(profile.RoleClaimType, role)));
        claims.AddRange(profile.Claims);
        return Array.AsReadOnly(claims.ToArray());
    }

    private static void AddMappedClaim(
        List<EasyAuthClaim> claims,
        string? claimType,
        string? value)
    {
        if (claimType is not null && value is not null)
        {
            claims.Add(new EasyAuthClaim(claimType, value));
        }
    }

    private static void ValidateHeaderValue(string value, string field)
    {
        ProfileValidator.RejectControlCharacters(value, field);
    }
}
