namespace IBeam.Identity.Models;

public sealed record OtpVerifyRequest(
    string ChallengeId,
    string Code);
