namespace IBeam.Identity.Abstractions.Models;

public enum OtpPurpose
{
    EmailVerification,
    LoginMfa,
    PasswordReset
}

public sealed record OtpChallengeRequest(
    string Email,
    OtpPurpose Purpose,
    Guid? TenantId = null);

public sealed record OtpChallengeResult(
    string ChallengeId,
    DateTimeOffset ExpiresAt);

public sealed record OtpVerifyRequest(
    string ChallengeId,
    string Code);

public sealed record OtpVerifyResult(
    bool Success,
    Guid? UserId = null);
