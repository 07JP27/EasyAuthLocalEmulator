using EasyAuthLocalEmulator.Auth;
using EasyAuthLocalEmulator.Configuration;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EasyAuthLocalEmulator.Pages.Auth;

public sealed class LogoutCompleteModel
    : PageModel
{
    public LogoutCompleteModel(
        IdentityProviderRegistry providers,
        LocalAuthenticationService authentication,
        EmulatorOptions options)
    {
        PlatformDisplayName = options.PlatformDisplayName;
        Providers = authentication.NoUi && authentication.SelectedProfile is not null
            ? providers.Providers
                .Where(provider =>
                    provider.RouteKey.Equals(
                        authentication.SelectedProfile.Provider,
                        StringComparison.Ordinal))
                .ToArray()
            : providers.Providers;
    }

    public IReadOnlyList<IdentityProviderDefinition> Providers { get; }

    public string PlatformDisplayName { get; }

    public void OnGet()
    {
    }
}
