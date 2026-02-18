namespace IBeam.Identity.Abstractions.Exceptions;

public sealed class IdentityNotFoundException : IdentityException
{
    public IdentityNotFoundException(string message = "Not found.", Exception? inner = null)
        : base(message, inner) { }
}
