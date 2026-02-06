namespace IBeam.Communications.Email.Abstractions;

public sealed class EmailValidationException : EmailServiceException
{
    public EmailValidationException(string provider, string message)
        : base(provider, message) { }
}
