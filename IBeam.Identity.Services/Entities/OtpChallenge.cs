using IBeam.Identity.Abstractions.Models;

namespace IBeam.Identity.Services.Entities;

public sealed class OtpChallengeEntity
{
    public Guid ChallengeId { get; set; }
    public SenderPurpose Purpose { get; set; }
    public SenderChannel Channel { get; set; }

    public string Destination { get; set; } = "";
    public string? TenantHint { get; set; }

    public string CodeHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }

    public DateTimeOffset ResendAfter { get; set; }

    public bool IsConsumed { get; set; }
    public string? VerificationToken { get; set; }
    public DateTimeOffset? VerificationTokenExpiresAt { get; set; }
}
