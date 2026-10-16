using System.Text.Json.Serialization;
namespace IBeam.Management.Services.Diagnostics;
public sealed class PortalDiagnosticsOptions
{
    public const string SectionName = "IBeam:Management:PortalDiagnostics";
    [JsonIgnore] public string ProtectionKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 10;
    public int TokenLifetimeMinutes { get; set; } = 15;
    public int MaximumResponseBytes { get; set; } = 262144;
    public bool IsValid()
    {
        try { return TimeoutSeconds is >= 1 and <= 30 && TokenLifetimeMinutes is >= 1 and <= 60 &&
            MaximumResponseBytes is >= 32768 and <= 1048576 && Convert.FromBase64String(ProtectionKey).Length == 32; }
        catch { return false; }
    }
    public override string ToString() => "Portal diagnostics settings (key redacted)";
}
