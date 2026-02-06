using IBeam.Identity.Core.Otp.Contracts;

namespace IBeam.Identity.Core.Otp.Interfaces;

public interface IOtpService
{
    Task<CreateOtpChallengeResponse> CreateChallengeAsync(CreateOtpChallengeRequest req, CancellationToken ct);
    Task<VerifyOtpChallengeResponse> VerifyChallengeAsync(VerifyOtpChallengeRequest req, CancellationToken ct);
    Task<ResendOtpChallengeResponse> ResendChallengeAsync(Guid challengeId, CancellationToken ct);
}
