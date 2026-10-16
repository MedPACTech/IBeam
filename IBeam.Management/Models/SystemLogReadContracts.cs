using System.Text.Json.Serialization;

namespace IBeam.Management.Models;

/// <summary>
/// Repository metadata, NOT an API DTO. Only operational "System" rows from SYSTEM|DAY partitions are mapped; message,
/// detail, actor/device/IP, tenant and before/after/original/transformed JSON columns are never loaded.
/// </summary>
public sealed record SystemLogMetadata
{
    [JsonIgnore] public string PartitionKey { get; init; } = "";
    [JsonIgnore] public string RowKey { get; init; } = "";
    [JsonIgnore] public DateTimeOffset OccurredAtUtc { get; init; }
    [JsonIgnore] public string Source { get; init; } = "";
    [JsonIgnore] public string Level { get; init; } = "";
    [JsonIgnore] public string TraceId { get; init; } = "";
    [JsonIgnore] public string? ManagementApplicationKey { get; init; }
    [JsonIgnore] public string? ManagementEnvironment { get; init; }
    public override string ToString() => "System log metadata (storage keys and untrusted text redacted)";
}

/// <summary>Filters are exact matches pushed to storage; null means unfiltered.</summary>
public sealed record SystemLogPartitionQuery(
    DateOnly DayUtc, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Take, string? ContinuationToken,
    string? Source = null, string? Level = null, string? TraceId = null)
{
    public override string ToString() => "System log partition query (filters and continuation redacted)";
}

public sealed record SystemLogPartitionPage(
    IReadOnlyList<SystemLogMetadata> Records, int ScannedCount, string? ContinuationToken);

public interface ISystemLogReadRepository
{
    /// <summary>Stable nonsecret physical-source fingerprint; changes invalidate protected cursors and record IDs.</summary>
    string SourceBinding { get; }
    Task<SystemLogPartitionPage> ReadPartitionAsync(SystemLogPartitionQuery query, CancellationToken ct = default);
    Task<SystemLogMetadata?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);
}

/// <summary>The frozen query a cursor or record ID is bound to.</summary>
public sealed record SystemLogQueryWindow(
    DateTimeOffset FromUtc, DateTimeOffset ToUtc, int PageSize, string? Source, string? Level, string? TraceId)
{
    public override string ToString() => "System log query window (filters redacted)";
}

public sealed record SystemLogReadItem(string Id, [property: JsonIgnore] SystemLogMetadata Record);
/// <summary>Internal reader result; the public service projects allowlisted DTOs instead of serializing this.</summary>
public sealed record SystemLogReadPage(
    IReadOnlyList<SystemLogReadItem> Items, [property: JsonIgnore] SystemLogQueryWindow Query, string? NextCursor,
    int ScannedCount, int StorageRequests);

public interface IManagementSystemLogReader
{
    Task<SystemLogReadPage> ReadAsync(SystemLogQuery query, CancellationToken ct = default);
    Task<SystemLogMetadata?> GetAsync(string recordId, CancellationToken ct = default);
}
