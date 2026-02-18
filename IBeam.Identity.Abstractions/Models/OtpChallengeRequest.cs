namespace IBeam.Identity.Abstractions.Models;

public sealed record OtpChallengeRequest(
    string Email,
    OtpPurpose Purpose,
    Guid? TenantId = null);

