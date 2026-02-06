namespace IBeam.Communications.Email.Abstractions;

public sealed record EmailAddress(string Address, string? DisplayName = null);
