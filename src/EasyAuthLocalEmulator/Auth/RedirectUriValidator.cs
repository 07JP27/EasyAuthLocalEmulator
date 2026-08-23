namespace EasyAuthLocalEmulator.Auth;

public sealed class RedirectUriValidator
{
    public bool TryValidate(string? value, string fallback, out string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            redirectUri = fallback;
            return true;
        }

        string candidate = value.Trim();

        try
        {
            if (!IsLocalPath(candidate) ||
                !IsLocalPath(Uri.UnescapeDataString(candidate)) ||
                !IsLocalPath(Uri.UnescapeDataString(Uri.UnescapeDataString(candidate))))
            {
                redirectUri = fallback;
                return false;
            }
        }
        catch (UriFormatException)
        {
            redirectUri = fallback;
            return false;
        }

        redirectUri = candidate;
        return true;
    }

    private static bool IsLocalPath(string value)
    {
        return value.Length > 0 &&
            value[0] == '/' &&
            (value.Length == 1 || (value[1] != '/' && value[1] != '\\')) &&
            !value.Contains('\\', StringComparison.Ordinal) &&
            !value.Any(char.IsControl);
    }
}
