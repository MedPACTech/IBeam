namespace IBeam.Identity.Abstractions.Models;

public sealed record OtpVerifyRequest(
    string ChallengeId,
    string Code);
