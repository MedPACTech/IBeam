using IBeam.Identity.Abstractions.Models;

public interface IIdentityOtpAuthService
{
    Task<OtpChallengeResult> RegisterUserViaOtpAsync(string destination, Guid? tenantId = null, CancellationToken ct = default);
    Task<AuthResultResponse> CompleteUserRegistrationViaOtpAsync(
        string challengeId,
        string code,
        string destination,
        string? displayName = null,
        CancellationToken ct = default);

    // OTP login
   // Task<OtpChallengeResult> BeginOtpLoginAsync(string destination, CancellationToken ct = default);
   // Task<AuthResultResponse> CompleteOtpLoginAsync(string challengeId, string code, CancellationToken ct = default);
}
