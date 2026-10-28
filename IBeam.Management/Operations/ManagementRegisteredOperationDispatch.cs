using System.Text.Json.Serialization;

namespace IBeam.Management.Operations;

/// <summary>Server-internal request binding. Never serialize or log actor assertions, headers or payloads.</summary>
public sealed class ManagementOperationInvocation
{
    public required string Operation { get; init; }
    public required string Method { get; init; }
    [JsonIgnore] public required string Path { get; init; }
    [JsonIgnore] public ReadOnlyMemory<byte> Body { get; init; }
    [JsonIgnore] public string? IdempotencyKey { get; init; }
    [JsonIgnore] public string? IfMatch { get; init; }
    [JsonIgnore] public required string Assertion { get; init; }
    public bool ReceiptLookup { get; init; }
    public override string ToString() => "Registered Management invocation (binding redacted)";
}
public sealed class ManagementOperationDispatchResult
{
    [JsonIgnore] public required ReadOnlyMemory<byte> Body { get; init; }
    public bool Unknown { get; init; }
    public override string ToString() => "Registered Management result (payload redacted)";
}
public interface IManagementRegisteredOperationDispatcher
{
    Task<ManagementOperationDispatchResult> ExecuteAsync(ManagementOperationInvocation request, CancellationToken ct = default);
}
