namespace IBeam.Identity.Models;

public sealed record OtpVerifyResult(
    bool Success,
    string? VerificationToken = null,
    DateTimeOffset? ExpiresAt = null,
    Guid? UserId = null);

