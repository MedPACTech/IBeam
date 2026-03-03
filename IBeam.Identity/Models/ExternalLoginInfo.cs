namespace IBeam.Identity.Models;

public sealed record ExternalLoginInfo(
    Guid UserId,
    string Provider,
    string ProviderUserId,
    string? Email);
