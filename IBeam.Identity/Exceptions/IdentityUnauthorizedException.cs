namespace IBeam.Identity.Exceptions;

public sealed class IdentityUnauthorizedException : IdentityException
{
    public IdentityUnauthorizedException(string message = "Unauthorized.", Exception? inner = null)
        : base(message, inner) { }
}
