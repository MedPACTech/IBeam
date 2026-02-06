namespace IBeam.Communications.Email.Abstractions;

public class EmailServiceException : Exception
{
    public string Provider { get; }

    public EmailServiceException(string provider, string message, Exception? inner = null)
        : base(message, inner)
    {
        Provider = provider;
    }
}
