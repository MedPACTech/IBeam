using System.Text.Json.Serialization;

namespace IBeam.Management.Models;

/// <summary>Internal repository record, NOT an API DTO. Optional raw fields are loaded only by an explicit raw read and never serialized.</summary>
public sealed record SystemErrorRecord
{
    [JsonIgnore] public string PartitionKey { get; init; } = "";
    [JsonIgnore] public string RowKey { get; init; } = "";
    [JsonIgnore] public DateTimeOffset OccurredAtUtc { get; init; }
    [JsonIgnore] public string Source { get; init; } = "";
    [JsonIgnore] public string TraceId { get; init; } = "";
    [JsonIgnore] public string? ManagementApplicationKey { get; init; }
    [JsonIgnore] public string? ManagementEnvironment { get; init; }
    [JsonIgnore] public string? Message { get; init; }
    [JsonIgnore] public string? Exception { get; init; }
    [JsonIgnore] public string? Path { get; init; }
    [JsonIgnore] public string? Method { get; init; }
    public override string ToString() => "System error metadata (storage keys and untrusted text redacted)";
}

public sealed record SystemErrorPartitionQuery(
    DateOnly DayUtc, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Take, string? ContinuationToken)
{
    public bool IncludeUnsanitized { get; init; }
}

public sealed record SystemErrorPartitionPage(
    IReadOnlyList<SystemErrorRecord> Records, int ScannedCount, string? ContinuationToken);

public interface ISystemErrorReadRepository
{
    /// <summary>Stable nonsecret physical-source fingerprint; changes invalidate protected cursors and record IDs.</summary>
    string SourceBinding { get; }
    Task<SystemErrorPartitionPage> ReadPartitionAsync(SystemErrorPartitionQuery query, CancellationToken ct = default);
    Task<SystemErrorRecord?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default);
    Task<SystemErrorRecord?> GetUnsanitizedAsync(string partitionKey, string rowKey, CancellationToken ct = default)
        => throw new NotSupportedException("The diagnostic adapter does not support unsanitized detail reads.");
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
