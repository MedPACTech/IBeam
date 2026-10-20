namespace IBeam.Identity.Exceptions;
public sealed class IdentityConcurrencyException() : IdentityException("The tenant has changed; read its current version before retrying.");
