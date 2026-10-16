using System.Text.Json.Serialization;

namespace IBeam.Management.Models;

/// <summary>Repository metadata, NOT an API DTO. Unsafe error message/exception/request payloads are never loaded.</summary>
public sealed record SystemErrorRecord
{
    [JsonIgnore] public string PartitionKey { get; init; } = "";
    [JsonIgnore] public string RowKey { get; init; } = "";
    [JsonIgnore] public DateTimeOffset OccurredAtUtc { get; init; }
    [JsonIgnore] public string Source { get; init; } = "";
    [JsonIgnore] public string TraceId { get; init; } = "";
    [JsonIgnore] public string? ManagementApplicationKey { get; init; }
    [JsonIgnore] public string? ManagementEnvironment { get; init; }
    public override string ToString() => "System error metadata (storage keys and untrusted text redacted)";
}

public sealed record SystemErrorPartitionQuery(
    DateOnly DayUtc, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Take, string? ContinuationToken);

public sealed record SystemErrorPartitionPage(
    IReadOnlyList<SystemErrorRecord> Records, int ScannedCount, string? ContinuationToken);

public interface ISystemErrorReadRepository
{
    /// <summary>Stable nonsecret physical-source fingerprint; changes invalidate protected cursors and record IDs.</summary>
    string SourceBinding { get; }
    Task<SystemErrorPartitionPage> ReadPartitionAsync(SystemErrorPartitionQuery query, CancellationToken ct = default);
    Task<SystemErrorRecord?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);
}

public sealed record SystemErrorReadItem(string Id, [property: JsonIgnore] SystemErrorRecord Record);
/// <summary>Internal reader result; a later API service must project allowlisted DTOs instead of serializing this.</summary>
public sealed record SystemErrorReadPage(
    IReadOnlyList<SystemErrorReadItem> Items, DiagnosticQueryWindow Query, string? NextCursor,
    int ScannedCount, int StorageRequests);

public interface IManagementSystemErrorReader
{
    Task<SystemErrorReadPage> ReadAsync(DiagnosticQuery query, CancellationToken ct = default);
    Task<SystemErrorRecord?> GetAsync(string recordId, DiagnosticQuery? originalQuery = null, CancellationToken ct = default);
}
