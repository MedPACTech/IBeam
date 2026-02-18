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

public interface IOtpChallengeStore
{
    Task SaveAsync(OtpChallengeRecord record, CancellationToken ct = default);
    Task<OtpChallengeRecord?> GetAsync(string challengeId, CancellationToken ct = default);
    Task IncrementAttemptAsync(string challengeId, CancellationToken ct = default);
    Task MarkConsumedAsync(string challengeId, CancellationToken ct = default);
}
