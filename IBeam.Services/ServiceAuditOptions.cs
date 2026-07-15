namespace IBeam.Services.Abstractions;

public enum SelectAuditMode
{
    None = 0,
    DailyRollup = 1
}

public enum ServiceAuditDefaultMode
{
    None = 0,
    AuditWrites = 1
}

public sealed class ServiceAuditOptions
{
    public const string SectionName = "IBeam:Services:Audit";

    public bool Enabled { get; set; } = false;

    public ServiceAuditDefaultMode DefaultMode { get; set; } = ServiceAuditDefaultMode.AuditWrites;

    public bool EnableSelectAudits { get; set; } = false;

    public SelectAuditMode SelectMode { get; set; } = SelectAuditMode.DailyRollup;

    public bool CaptureBefore { get; set; } = true;

    public bool CaptureAfter { get; set; } = true;

    public Dictionary<string, ServiceAuditServiceOptions> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// If true, audit sink failures are surfaced to caller. Default false keeps business flow resilient.
    /// </summary>
    public bool FailOnAuditError { get; set; } = false;
}

public sealed class ServiceAuditServiceOptions
{
    public bool? Enabled { get; set; }

    public string? EntityName { get; set; }

    public Dictionary<string, ServiceAuditOperationOptions> Operations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ServiceAuditOperationOptions
{
    public bool? Enabled { get; set; }

    public string? Action { get; set; }

    public bool? CaptureBefore { get; set; }

    public bool? CaptureAfter { get; set; }
}

