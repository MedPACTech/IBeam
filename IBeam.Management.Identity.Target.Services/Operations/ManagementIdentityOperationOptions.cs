using System.Text.Json.Serialization;
namespace IBeam.Management.Identity.Target.Services.Operations;

public sealed class ManagementIdentityOperationOptions
{
    public const string SectionName = "IBeam:Management:CustomerIdentity:Operations";
    /// <summary>Stable HMAC key: retain across restarts and throughout the idempotency retention window.</summary>
    [JsonIgnore] public string FingerprintKey { get; set; } = "";
    public int RetentionDays { get; set; } = 90;
    public int LeaseSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 20;
    public int MaximumAttempts { get; set; } = 10;
    public bool IsValid() { try { return Convert.FromBase64String(FingerprintKey).Length == 32 && RetentionDays is >= 1 and <= 3650 &&
        LeaseSeconds is >= 30 and <= 300 && TimeoutSeconds is >= 1 and <= 30 && TimeoutSeconds < LeaseSeconds && MaximumAttempts is >= 1 and <= 100; } catch { return false; } }
    public override string ToString() => "Operation options (fingerprint key redacted)";
}
