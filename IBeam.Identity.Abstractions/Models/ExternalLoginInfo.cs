namespace IBeam.Identity.Abstractions.Models;

public sealed record ExternalLoginInfo(
    Guid UserId,
    string Provider,
    string ProviderUserId,
    string? Email);
