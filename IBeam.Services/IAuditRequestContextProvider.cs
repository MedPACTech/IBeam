namespace IBeam.Services.Abstractions;

public interface IAuditRequestContextProvider
{
    AuditRequestContext GetContext();
}

public sealed class AuditRequestContext
{
    public string? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? DeviceId { get; set; }
}

public sealed class NoOpAuditRequestContextProvider : IAuditRequestContextProvider
{
    public AuditRequestContext GetContext() => new();
}
