namespace IBeam.Identity.Abstractions.Exceptions;

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
