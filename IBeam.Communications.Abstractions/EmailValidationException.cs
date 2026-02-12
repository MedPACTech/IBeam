namespace IBeam.Communications.Abstractions;

public sealed class EmailValidationException : Exception
{
    public string Provider { get; }

    public EmailValidationException(string provider, string message)
        : base(message) => Provider = provider;

    public EmailValidationException(string provider, string message, Exception inner)
        : base(message, inner) => Provider = provider;
}