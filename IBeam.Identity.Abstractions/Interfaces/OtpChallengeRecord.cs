namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public sealed record OtpChallengeRecord(
    string ChallengeId,
    string Email,
    OtpPurpose Purpose,
    string CodeHash,
    DateTimeOffset ExpiresAt,
    int AttemptCount,
    Guid? TenantId);
