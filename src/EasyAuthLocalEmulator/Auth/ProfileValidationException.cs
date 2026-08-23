namespace EasyAuthLocalEmulator.Auth;

public sealed class ProfileValidationException : Exception
{
    public ProfileValidationException(string message, string? fieldPath = null)
        : base(message)
    {
        FieldPath = fieldPath;
    }

    public string? FieldPath { get; }
}
