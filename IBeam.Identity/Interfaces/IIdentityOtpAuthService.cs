using IBeam.Identity.Models;

public interface IIdentityOtpAuthService
{
    Task<OtpChallengeResult> StartOtpAsync(string destination, Guid? tenantId = null, CancellationToken ct = default);
    Task<AuthResultResponse> CompleteOtpAsync(
        string challengeId,
        string code,
        string destination,
        string? displayName = null,
        CancellationToken ct = default);
}
