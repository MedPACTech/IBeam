namespace IBeam.Identity.Exceptions;

public abstract class IdentityException : Exception
{
    protected IdentityException(string message, Exception? inner = null) : base(message, inner) { }
}
