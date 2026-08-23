using EasyAuthLocalEmulator.Auth;

namespace EasyAuthLocalEmulator.UnitTests.Auth;

public sealed class RedirectUriValidatorTests
{
    private readonly RedirectUriValidator _validator = new();

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/dashboard?tab=auth", "/dashboard?tab=auth")]
    [InlineData(null, "/fallback")]
    [InlineData("", "/fallback")]
    public void AcceptsLocalPathsOrFallback(string? value, string expected)
    {
        Assert.True(_validator.TryValidate(value, "/fallback", out string redirectUri));
        Assert.Equal(expected, redirectUri);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("/%2f%2fevil.example")]
    [InlineData("/%252f%252fevil.example")]
    [InlineData("/safe\r\nLocation: https://evil.example")]
    public void RejectsExternalOrAmbiguousPaths(string value)
    {
        Assert.False(_validator.TryValidate(value, "/", out string redirectUri));
        Assert.Equal("/", redirectUri);
    }
}
