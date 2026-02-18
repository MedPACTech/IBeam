namespace IBeam.Identity.Abstractions.Models;

public sealed record IdentityUser
(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    string? DisplayName = null
);
