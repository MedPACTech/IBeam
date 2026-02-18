namespace IBeam.Identity.Abstractions.Models;

public sealed record OtpVerifyResult(
    bool Success,
    string? VerificationToken = null,
    DateTimeOffset? ExpiresAt = null,
    Guid? UserId = null);

