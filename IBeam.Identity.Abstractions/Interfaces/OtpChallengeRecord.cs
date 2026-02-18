using IBeam.Identity.Abstractions.Models;

namespace IBeam.Identity.Abstractions.Interfaces;

public sealed record OtpChallengeRecord(
    string ChallengeId,
    string Email,
    OtpPurpose Purpose,
    string CodeHash,
    DateTimeOffset ExpiresAt,
    int AttemptCount,
    Guid? TenantId,
    bool IsConsumed,
    string? VerificationToken,
    DateTimeOffset? VerificationTokenExpiresAt);

