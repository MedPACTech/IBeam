namespace IBeam.Identity.Models;

public sealed record IdentityUser
(
    Guid UserId,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber = null,
    bool PhoneConfirmed = false,
    bool TwoFactorEnabled = false,
    string? PreferredTwoFactorMethod = null
);
