namespace IBeam.Services.Abstractions;

public enum SelectAuditMode
{
    None = 0,
    DailyRollup = 1
}

public sealed class ServiceAuditOptions
{
    public const string SectionName = "IBeam:Services:Audit";

    public bool Enabled { get; set; } = false;

    public bool EnableSelectAudits { get; set; } = false;

    public SelectAuditMode SelectMode { get; set; } = SelectAuditMode.DailyRollup;

    /// <summary>
    /// If true, audit sink failures are surfaced to caller. Default false keeps business flow resilient.
    /// </summary>
    public bool FailOnAuditError { get; set; } = false;
}

