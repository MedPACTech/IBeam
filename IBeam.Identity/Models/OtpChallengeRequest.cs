namespace IBeam.Identity.Models;

public sealed record OtpChallengeRequest(
    SenderChannel Channel,
    string Destination,
    SenderPurpose Purpose,
    Guid? TenantId);

