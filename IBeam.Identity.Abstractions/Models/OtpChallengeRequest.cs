namespace IBeam.Identity.Abstractions.Models;

public sealed record OtpChallengeRequest(
    SenderChannel Channel,
    string Destination,
    SenderPurpose Purpose,
    Guid? TenantId);

