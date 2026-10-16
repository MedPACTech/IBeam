using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IBeam.Management.Services.Diagnostics;

/// <summary>Separate from the error reader: its own source isolation confirmation, budgets and protection key.</summary>
public sealed class SystemLogReaderOptions
{
    public const string SectionName = "IBeam:Management:Diagnostics:Logs";
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
    /// <summary>
    /// Extra Source values written by this deployment that may be shown and filtered verbatim, in addition to the
    /// IBeam writers' own sources. Anything else is shown as "System". Never list a source whose value carries data.
    /// </summary>
    public string[] AllowedSources { get; set; } = [];
    [JsonIgnore] public string ProtectionKey { get; set; } = "";
    public override string ToString() => "System log reader options (protection key redacted)";

    public bool IsValid()
    {
        if (!(SourceMode == DiagnosticSourceMode.DedicatedDeployment && DedicatedSourceConfirmed ||
              SourceMode == DiagnosticSourceMode.TaggedShared && TrustedTagsConfirmed)) return false;
        if (DefaultWindowHours is < 1 or > 168 || MaximumWindowHours < DefaultWindowHours || MaximumWindowHours > 168 ||
            DefaultPageSize < 1 || MaximumPageSize < DefaultPageSize || MaximumPageSize > 1000 ||
            ScanBudgetPerPage < MaximumPageSize || ScanBudgetPerPage > 10000 ||
            MaximumStorageRequestsPerPage is < 1 or > 32 || RequestTimeoutSeconds is < 1 or > 30 ||
            TokenLifetimeMinutes is < 1 or > 60) return false;
        if (AllowedSources is null || AllowedSources.Length > 32 ||
            AllowedSources.Any(x => x is null || !Regex.IsMatch(x, "^[A-Za-z][A-Za-z0-9._-]{0,127}$")) ||
            AllowedSources.Contains("System", StringComparer.Ordinal)) return false;
        try { return Convert.FromBase64String(ProtectionKey).Length == 32; }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException) { return false; }
    }
}
