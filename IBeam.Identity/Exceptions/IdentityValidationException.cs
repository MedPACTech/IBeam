namespace IBeam.Identity.Exceptions;

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
