namespace IBeam.Identity.Abstractions.Models;

public sealed record OtpChallengeRecord(
    string ChallengeId,
    string Destination, // email address or phone number
    OtpPurpose Purpose,
    string CodeHash,
    DateTimeOffset ExpiresAt,
    int AttemptCount,
    Guid? TenantId,
    bool IsConsumed,
    string? VerificationToken,
    DateTimeOffset? VerificationTokenExpiresAt);

