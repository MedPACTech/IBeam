namespace IBeam.Identity.Interfaces;

using IBeam.Identity.Models;

public interface IOtpService
{
    Task<OtpChallengeResult> CreateChallengeAsync(OtpChallengeRequest request, CancellationToken ct = default);
    Task<OtpVerifyResult> VerifyAsync(OtpVerifyRequest request, CancellationToken ct = default);
}
