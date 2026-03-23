namespace IBeam.Services.Abstractions;

public sealed class ServiceSelectAuditRollup
{
    public DateOnly DateUtc { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public string ServiceName { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public ServiceAuditOperation Operation { get; set; }

    public Guid? TenantId { get; set; }

    public string? ActorId { get; set; }

    public string QuerySignature { get; set; } = string.Empty;

    public DateTimeOffset FirstSeenUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;

    public int Count { get; set; } = 1;
}

