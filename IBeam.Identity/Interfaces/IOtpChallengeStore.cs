using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IOtpChallengeStore
{
    Task SaveAsync(OtpChallengeRecord record, CancellationToken ct = default);
    Task<OtpChallengeRecord?> GetAsync(string challengeId, CancellationToken ct = default);

    Task IncrementAttemptAsync(string challengeId, CancellationToken ct = default);

    Task MarkConsumedAsync(
        string challengeId,
        string verificationToken,
        DateTimeOffset verificationTokenExpiresAt,
        CancellationToken ct = default);
}
