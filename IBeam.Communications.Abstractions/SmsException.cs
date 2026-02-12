namespace IBeam.Communications.Abstractions;

public abstract class SmsException : Exception
{
    protected SmsException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class SmsValidationException : SmsException
{
    public SmsValidationException(string message) : base(message) { }
}

public sealed class SmsConfigurationException : SmsException
{
    public SmsConfigurationException(string message) : base(message) { }
}

public sealed class SmsProviderException : SmsException
{
    public string Provider { get; }
    public string? ProviderCode { get; }
    public bool IsTransient { get; }

    public SmsProviderException(
        string provider,
        string message,
        bool isTransient,
        string? providerCode = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Provider = provider;
        ProviderCode = providerCode;
        IsTransient = isTransient;
    }
}
