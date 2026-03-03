namespace IBeam.Communications.Abstractions;

public abstract class CommunicationsException : Exception
{
    protected CommunicationsException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public abstract class ValidationExceptionBase : CommunicationsException
{
    protected ValidationExceptionBase(string message, Exception? inner = null)
        : base(message, inner) { }
}


// ----------------------
// Email (capability-level)
// ----------------------

public sealed class EmailValidationException : ValidationExceptionBase
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

public sealed class EmailProviderException : ProviderException
{
    public EmailProviderException(
        string provider,
        string message,
        bool isTransient,
        string? providerCode = null,
        Exception? inner = null)
        : base(provider, message, isTransient, providerCode, inner)
    {
    }
}


// ----------------------
// SMS (capability-level)
// ----------------------

public sealed class SmsValidationException : ValidationExceptionBase
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

public sealed class SmsProviderException : ProviderException
{
    public SmsProviderException(
        string provider,
        string message,
        bool isTransient,
        string? providerCode = null,
        Exception? inner = null)
        : base(provider, message, isTransient, providerCode, inner)
    {
    }
}


public abstract class ProviderException : CommunicationsException
{
    public string Provider { get; }
    public bool IsTransient { get; }
    public string? ProviderCode { get; }

    protected ProviderException(
        string provider,
        string message,
        bool isTransient,
        string? providerCode = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Provider = string.IsNullOrWhiteSpace(provider)
            ? "Unknown"
            : provider;

        IsTransient = isTransient;
        ProviderCode = providerCode;
    }
}

public sealed class EmailTemplateException : CommunicationsException
{
    public EmailTemplateException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public sealed class EmailTemplateNotFoundException : CommunicationsException
{
    public string TemplateName { get; }

    public EmailTemplateNotFoundException(string templateName, Exception? inner = null)
        : base($"Email template '{templateName}' was not found.", inner)
    {
        TemplateName = templateName;
    }
}
