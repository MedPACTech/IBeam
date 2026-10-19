using System.Text.Json.Serialization;
namespace IBeam.Management.Identity.Target.Services.Tenants;

public sealed class ManagementTenantDirectoryOptions
{
    public const string SectionName = "IBeam:Management:CustomerIdentity:TenantDirectory";
    [JsonIgnore] public string ProtectionKey { get; set; } = "";
    public int TokenLifetimeMinutes { get; set; } = 15;
    public int TimeoutSeconds { get; set; } = 10;
    public bool IsValid()
    {
        try { return Convert.FromBase64String(ProtectionKey).Length == 32 && TokenLifetimeMinutes is >= 1 and <= 60 && TimeoutSeconds is >= 1 and <= 30; }
        catch { return false; }
    }
    public override string ToString() => "Tenant directory options (protection key redacted)";
}
