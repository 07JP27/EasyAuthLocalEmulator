namespace EasyAuthLocalEmulator.Configuration;

public sealed class InputValidationException : Exception
{
    public InputValidationException(string message)
        : base(message)
    {
    }

    public InputValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
