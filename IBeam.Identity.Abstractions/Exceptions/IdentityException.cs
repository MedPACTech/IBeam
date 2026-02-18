namespace IBeam.Identity.Abstractions.Exceptions;

public abstract class IdentityException : Exception
{
    protected IdentityException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class IdentityValidationException : IdentityException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public IdentityValidationException(
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }
}

public sealed class IdentityUnauthorizedException : IdentityException
{
    public IdentityUnauthorizedException(string message = "Unauthorized.", Exception? inner = null)
        : base(message, inner) { }
}

public sealed class IdentityNotFoundException : IdentityException
{
    public IdentityNotFoundException(string message = "Not found.", Exception? inner = null)
        : base(message, inner) { }
}

public sealed class IdentityProviderException : IdentityException
{
    public string ProviderName { get; }
    public string? ProviderErrorCode { get; }

    public IdentityProviderException(
        string providerName,
        string message = "Identity provider failure.",
        string? providerErrorCode = null,
        Exception? inner = null)
        : base(message, inner)
    {
        ProviderName = providerName;
        ProviderErrorCode = providerErrorCode;
    }
}
