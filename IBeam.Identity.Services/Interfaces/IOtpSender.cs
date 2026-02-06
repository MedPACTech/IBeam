using IBeam.Identity.Core.Otp.Contracts;

namespace IBeam.Identity.Services.Otp;

public interface IOtpSender
{
    Task SendAsync(OtpChannel channel, string destination, string code, OtpPurpose purpose, CancellationToken ct);
}
