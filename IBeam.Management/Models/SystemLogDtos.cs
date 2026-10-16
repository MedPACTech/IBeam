using System.Text.Json.Serialization;
namespace IBeam.Management.Models;

/// <summary>
/// UTC [From, To) window plus optional exact filters. On continuation, omitted fields retain the protected original
/// query; a different value is rejected rather than widening the scan.
/// </summary>
public sealed record SystemLogQuery
{
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public int? PageSize { get; init; }
    /// <summary>An approved source name (see SystemLogReaderOptions.AllowedSources); unknown names are rejected.</summary>
    public string? Source { get; init; }
    /// <summary>Trace, Debug, Information, Warning, Error or Critical.</summary>
    public string? Level { get; init; }
    /// <summary>Exact raw trace identifier, for example the traceId a customer received in an API error envelope.</summary>
    public string? TraceId { get; init; }
    public string? Cursor { get; init; }
    public override string ToString() => "System log query (filters and cursor redacted)";
}

/// <summary>Safe default fields with optional marked non-Production raw text; no storage keys, actor/device/IP, tenant or change payloads.</summary>
public sealed record SystemLogDto(
    string Id, DateTimeOffset OccurredAtUtc, string Level, string Source, string Summary, string? CorrelationId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SystemLogUnsanitized? Unsanitized { get; init; }
}

public sealed record SystemLogPage(IReadOnlyList<SystemLogDto> Items, int PageSize, string? ContinuationToken);

public interface ISystemLogReaderService
{
    Task<SystemLogPage> ListAsync(SystemLogQuery query, CancellationToken ct = default);
    Task<SystemLogDto> GetAsync(string logId, CancellationToken ct = default);
}

public static class SystemLogLevels
{
    public static readonly IReadOnlyList<string> All =
        Array.AsReadOnly(new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical" });
}
