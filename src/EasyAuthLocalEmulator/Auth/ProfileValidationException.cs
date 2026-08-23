namespace EasyAuthLocalEmulator.Auth;

public sealed class ProfileValidationException : Exception
{
    public ProfileValidationException(string message)
        : base(message)
    {
    }
}
