namespace IBeam.Identity.Abstractions.Models;

public sealed record OtpChallengeRequest(
    OtpChannel Channel,
    string Destination,
    OtpPurpose Purpose,
    Guid? TenantId);

