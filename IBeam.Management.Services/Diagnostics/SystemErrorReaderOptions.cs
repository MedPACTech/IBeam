using System.Text.Json.Serialization;

namespace IBeam.Management.Services.Diagnostics;

public enum DiagnosticSourceMode { Unspecified, DedicatedDeployment, TaggedShared }

public sealed class SystemErrorReaderOptions
{
    public const string SectionName = "IBeam:Management:Diagnostics:Errors";
    public DiagnosticSourceMode SourceMode { get; set; }
    public bool DedicatedSourceConfirmed { get; set; }
    public bool TrustedTagsConfirmed { get; set; }
    public int DefaultWindowHours { get; set; } = 24;
    public int MaximumWindowHours { get; set; } = 168;
    public int DefaultPageSize { get; set; } = 50;
    public int MaximumPageSize { get; set; } = 100;
    public int ScanBudgetPerPage { get; set; } = 500;
    public int MaximumStorageRequestsPerPage { get; set; } = 16;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int TokenLifetimeMinutes { get; set; } = 30;
    [JsonIgnore] public string ProtectionKey { get; set; } = "";
    public override string ToString() => "System error reader options (protection key redacted)";

    public bool IsValid()
    {
        if (!(SourceMode == DiagnosticSourceMode.DedicatedDeployment && DedicatedSourceConfirmed ||
              SourceMode == DiagnosticSourceMode.TaggedShared && TrustedTagsConfirmed)) return false;
        if (DefaultWindowHours is < 1 or > 168 || MaximumWindowHours < DefaultWindowHours || MaximumWindowHours > 168 ||
            DefaultPageSize < 1 || MaximumPageSize < DefaultPageSize || MaximumPageSize > 1000 ||
            ScanBudgetPerPage < MaximumPageSize || ScanBudgetPerPage > 10000 ||
            MaximumStorageRequestsPerPage is < 1 or > 32 || RequestTimeoutSeconds is < 1 or > 30 ||
            TokenLifetimeMinutes is < 1 or > 60) return false;
        try { return Convert.FromBase64String(ProtectionKey).Length == 32; }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException) { return false; }
    }
}
