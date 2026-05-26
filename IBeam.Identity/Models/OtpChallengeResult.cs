namespace IBeam.Identity.Models;

public sealed record OtpChallengeResult(
    string ChallengeId,
    DateTimeOffset ExpiresAt);
