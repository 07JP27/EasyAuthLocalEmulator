using System.ComponentModel.DataAnnotations;
using EasyAuthLocalEmulator.Auth;
using EasyAuthLocalEmulator.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EasyAuthLocalEmulator.Pages.Auth;

public sealed class LoginModel(
    EmulatorOptions options,
    LocalAuthenticationService authentication,
    PrincipalBuilder principalBuilder,
    RedirectUriValidator redirectValidator,
    IdentityProviderRegistry providerRegistry) : PageModel
{
    [BindProperty]
    public LoginProfileInput Input { get; set; } = new();

    [BindProperty]
    public string? PostLoginRedirectUri { get; set; }

    public string Provider { get; private set; } = "aad";

    public string ProviderDisplayName { get; private set; } = "Microsoft Entra ID";

    public string? SelectedPresetName { get; private set; }

    public IReadOnlyList<string> ProfileNames { get; private set; } = [];

    public IReadOnlyList<IdentityProviderDefinition> Providers =>
        providerRegistry.Providers;

    public IActionResult OnGet(
        string provider,
        string? profile,
        [FromQuery(Name = "post_login_redirect_uri")] string? postLoginRedirectUri)
    {
        if (!TrySelectProvider(provider, out IdentityProviderDefinition? definition))
        {
            return NotFound();
        }

        PostLoginRedirectUri = postLoginRedirectUri;
        if (!redirectValidator.TryValidate(
                PostLoginRedirectUri,
                "/",
                out string redirectUri))
        {
            return BadRequest("Invalid post_login_redirect_uri.");
        }

        PostLoginRedirectUri = redirectUri;

        if (authentication.NoUi)
        {
            UserProfile fixedProfile = authentication.SelectedProfile
                ?? throw new InvalidOperationException(
                    "No-UI mode requires a selected profile.");

            if (!fixedProfile.Provider.Equals(Provider, StringComparison.Ordinal))
            {
                return NotFound();
            }

            authentication.SignIn(HttpContext, fixedProfile);
            return LocalRedirect(redirectUri);
        }

        PopulateProfiles();
        SelectedPresetName = profile ??
            (options.SelectedProfile?.Provider == Provider
                ? options.SelectedProfileName
                : null);

        if (SelectedPresetName is not null)
        {
            if (!options.Profiles.TryGetValue(
                    SelectedPresetName,
                    out UserProfile? selectedProfile) ||
                !selectedProfile.Provider.Equals(Provider, StringComparison.Ordinal))
            {
                return NotFound();
            }

            Input = LoginProfileInput.FromProfile(selectedProfile);
        }
        else
        {
            Input = LoginProfileInput.CreateDefault(definition);
        }

        return Page();
    }

    public IActionResult OnPost(string provider)
    {
        if (!TrySelectProvider(provider, out _) || authentication.NoUi)
        {
            return NotFound();
        }

        PopulateProfiles();

        if (!redirectValidator.TryValidate(
                PostLoginRedirectUri,
                "/",
                out string redirectUri))
        {
            ModelState.AddModelError(
                nameof(PostLoginRedirectUri),
                "The post-login redirect URI must be a local path.");
        }

        UserProfile? profile = null;
        if (ModelState.IsValid)
        {
            try
            {
                profile = ProfileValidator.Create(
                    Input.ToConfiguration(Provider),
                    providerRegistry);
                _ = principalBuilder.Build(profile);
            }
            catch (ProfileValidationException exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
            }
        }

        if (!ModelState.IsValid || profile is null)
        {
            return Page();
        }

        authentication.SignIn(HttpContext, profile);
        return LocalRedirect(redirectUri);
    }

    private bool TrySelectProvider(
        string provider,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out IdentityProviderDefinition? definition)
    {
        if (!providerRegistry.TryGet(provider, out definition))
        {
            return false;
        }

        Provider = definition.RouteKey;
        ProviderDisplayName = definition.DisplayName;
        return true;
    }

    private void PopulateProfiles()
    {
        ProfileNames = options.Profiles
            .Where(item => item.Value.Provider.Equals(Provider, StringComparison.Ordinal))
            .Select(item => item.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed class LoginProfileInput
{
    [Required]
    [StringLength(256)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [StringLength(320)]
    [Display(Name = "User name / email")]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [StringLength(512)]
    [Display(Name = "User ID")]
    public string UserId { get; set; } = string.Empty;

    [StringLength(512)]
    [Display(Name = "Tenant ID")]
    public string? TenantId { get; set; }

    [StringLength(2048)]
    [Display(Name = "Issuer (iss)")]
    public string? Issuer { get; set; }

    [Required]
    [StringLength(128)]
    [Display(Name = "Authentication type")]
    public string AuthenticationType { get; set; } = string.Empty;

    [Required]
    [StringLength(512)]
    [Display(Name = "Name claim type")]
    public string NameClaimType { get; set; } = string.Empty;

    [Required]
    [StringLength(512)]
    [Display(Name = "Role claim type")]
    public string RoleClaimType { get; set; } = string.Empty;

    [Display(Name = "Display-name claim")]
    public string? DisplayNameClaimType { get; set; }

    [Display(Name = "User-name claim")]
    public string? UserNameClaimType { get; set; }

    [Display(Name = "User-ID claim")]
    public string? UserIdClaimType { get; set; }

    [Display(Name = "Tenant-ID claim")]
    public string? TenantIdClaimType { get; set; }

    public List<string?> Roles { get; set; } = [];

    public List<ClaimInput> Claims { get; set; } = [];

    public static LoginProfileInput CreateDefault(IdentityProviderDefinition provider)
    {
        string? tenantId = provider.RequiresTenantId
            ? "00000000-0000-0000-0000-000000000000"
            : null;

        return new LoginProfileInput
        {
            DisplayName = "Local Developer",
            UserName = provider.RouteKey switch
            {
                "x" => "local_developer",
                "github" => "local-developer",
                _ => "developer@local.test"
            },
            UserId = provider.RouteKey switch
            {
                "aad" => "00000000-0000-0000-0000-000000000001",
                "facebook" => "100000000000001",
                "google" => "google-subject-001",
                "x" => "1000000001",
                "github" => "10000001",
                "apple" => "apple-subject-001",
                _ => throw new InvalidOperationException("Unsupported identity provider.")
            },
            TenantId = tenantId,
            Issuer = provider.ResolveDefaultIssuer(tenantId),
            AuthenticationType = provider.DefaultAuthenticationType,
            NameClaimType = provider.DefaultClaimMappings.DisplayName ?? "name",
            RoleClaimType = "roles",
            DisplayNameClaimType = provider.DefaultClaimMappings.DisplayName,
            UserNameClaimType = provider.DefaultClaimMappings.UserName,
            UserIdClaimType = provider.DefaultClaimMappings.UserId,
            TenantIdClaimType = provider.DefaultClaimMappings.TenantId
        };
    }

    public static LoginProfileInput FromProfile(UserProfile profile)
    {
        return new LoginProfileInput
        {
            DisplayName = profile.DisplayName,
            UserName = profile.UserName,
            UserId = profile.UserId,
            TenantId = profile.TenantId,
            Issuer = profile.Issuer,
            AuthenticationType = profile.AuthenticationType,
            NameClaimType = profile.NameClaimType,
            RoleClaimType = profile.RoleClaimType,
            DisplayNameClaimType = profile.ClaimMappings.DisplayName,
            UserNameClaimType = profile.ClaimMappings.UserName,
            UserIdClaimType = profile.ClaimMappings.UserId,
            TenantIdClaimType = profile.ClaimMappings.TenantId,
            Roles = profile.Roles.Cast<string?>().ToList(),
            Claims = profile.Claims
                .Select(claim => new ClaimInput
                {
                    Type = claim.Type,
                    Value = claim.Value
                })
                .ToList()
        };
    }

    public ProfileConfiguration ToConfiguration(string provider)
    {
        return new ProfileConfiguration
        {
            Provider = provider,
            AuthenticationType = AuthenticationType,
            DisplayName = DisplayName,
            UserName = UserName,
            UserId = UserId,
            TenantId = TenantId,
            Issuer = Issuer ?? string.Empty,
            NameClaimType = NameClaimType,
            RoleClaimType = RoleClaimType,
            ClaimMappings = new ClaimMappingsConfiguration
            {
                DisplayName = OptionalString.Specified(DisplayNameClaimType),
                UserName = OptionalString.Specified(UserNameClaimType),
                UserId = OptionalString.Specified(UserIdClaimType),
                TenantId = OptionalString.Specified(TenantIdClaimType)
            },
            Roles = Roles,
            Claims = Claims
                .Select(claim => new EasyAuthClaim(
                    claim.Type ?? string.Empty,
                    claim.Value ?? string.Empty))
                .Cast<EasyAuthClaim?>()
                .ToList()
        };
    }
}

public sealed class ClaimInput
{
    [Display(Name = "Claim type")]
    public string? Type { get; set; }

    [Display(Name = "Claim value")]
    public string? Value { get; set; }
}
