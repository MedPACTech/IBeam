using IBeam.Identity.Core.Auth.Contracts;

namespace IBeam.Identity.Core.Auth.Interfaces;

public interface IOtpService
{
    Task RequestOtpAsync(RequestOtpRequest request, CancellationToken ct = default);

    Task<VerifyOtpResult> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct = default);
}
