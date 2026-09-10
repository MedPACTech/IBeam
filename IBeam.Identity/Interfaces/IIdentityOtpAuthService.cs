using IBeam.Identity.Models;

public interface IIdentityOtpAuthService
{
    Task<OtpChallengeResult> StartOtpAsync(string destination, Guid? tenantId = null, CancellationToken ct = default);
    Task<AuthResultResponse> CompleteOtpAsync(
        string challengeId,
        string code,
        string destination,
        CancellationToken ct = default);

    /// <summary>Same as the 4-argument overload, but <paramref name="rememberDevice"/> requests a longer-lived session (see IBeam.Identity.Interfaces.ITokenService.CreateAccessTokenAsync).</summary>
    Task<AuthResultResponse> CompleteOtpAsync(
        string challengeId,
        string code,
        string destination,
        bool rememberDevice,
        CancellationToken ct = default);
}
