namespace IBeam.Communications.Abstractions;

public abstract class CommunicationsException : Exception
{
    protected CommunicationsException(string message, Exception? inner = null)
        : base(message, inner) { }
}

// ----------------------
// Email (capability-level)
// ----------------------

public sealed class EmailValidationException : CommunicationsException
{
    public EmailValidationException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public sealed class EmailConfigurationException : CommunicationsException
{
    public EmailConfigurationException(string message, Exception? inner = null)
        : base(message, inner) { }
}

// ----------------------
// Email (provider-level)
// ----------------------

public sealed class EmailProviderException : CommunicationsException
{
    public string Provider { get; }
    public bool IsTransient { get; }
    public string? ProviderCode { get; }

    public EmailProviderException(
        string provider,
        string message,
        bool isTransient,
        string? providerCode = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? "Unknown" : provider;
        IsTransient = isTransient;
        ProviderCode = providerCode;
    }
}

// ----------------------
// SMS (capability-level)
// ----------------------

public sealed class SmsValidationException : CommunicationsException
{
    public SmsValidationException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public sealed class SmsConfigurationException : CommunicationsException
{
    public SmsConfigurationException(string message, Exception? inner = null)
        : base(message, inner) { }
}

// ----------------------
// SMS (provider-level)
// ----------------------

public sealed class SmsProviderException : CommunicationsException
{
    public string Provider { get; }
    public bool IsTransient { get; }
    public string? ProviderCode { get; }

    public SmsProviderException(
        string provider,
        string message,
        bool isTransient,
        string? providerCode = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? "Unknown" : provider;
        IsTransient = isTransient;
        ProviderCode = providerCode;
    }
}
