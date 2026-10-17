namespace IBeam.Management;

public sealed class ManagementDiagnosticsOptions
{
    public const string SectionName = "IBeam:Management:Diagnostics";
    public string ApplicationKey { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
}
