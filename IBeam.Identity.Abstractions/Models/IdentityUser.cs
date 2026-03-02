namespace IBeam.Identity.Abstractions.Models;

public sealed record IdentityUser
(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    string? PhoneNumber = null,
    bool PhoneConfirmed = false,
    string? DisplayName = null,
    bool TwoFactorEnabled = false,
    string? PreferredTwoFactorMethod = null
);
