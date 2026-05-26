namespace IBeam.Api.Abstractions;

public sealed class SystemLogRecord
{
    public string Source { get; set; } = string.Empty;
    public string Level { get; set; } = "Information";
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
