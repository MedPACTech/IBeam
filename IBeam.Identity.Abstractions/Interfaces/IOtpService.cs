namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface IOtpService
{
    Task<OtpChallengeResult> CreateChallengeAsync(OtpChallengeRequest request, CancellationToken ct = default);
    Task<OtpVerifyResult> VerifyAsync(OtpVerifyRequest request, CancellationToken ct = default);
}
