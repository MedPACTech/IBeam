using IBeam.Identity.Core.Entities;

namespace IBeam.Identity.Services.Otp;

public interface IOtpChallengeStore
{
    Task CreateAsync(OtpChallengeEntity entity, CancellationToken ct);
    Task<OtpChallengeEntity?> GetAsync(Guid challengeId, CancellationToken ct);

    Task IncrementAttemptsAsync(Guid challengeId, CancellationToken ct);

    Task UpdateCodeAsync(Guid challengeId, string newCodeHash, DateTimeOffset nextResendAfter, CancellationToken ct);

    Task MarkConsumedAsync(
        Guid challengeId,
        string verificationToken,
        DateTimeOffset consumedAt,
        DateTimeOffset verificationTokenExpiresAt,
        CancellationToken ct);
}
