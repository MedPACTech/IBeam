namespace IBeam.Identity.Abstractions.Models;

public sealed record OtpChallengeResult(
    string ChallengeId,
    DateTimeOffset ExpiresAt);
