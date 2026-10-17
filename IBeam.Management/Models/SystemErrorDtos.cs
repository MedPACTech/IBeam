using System.Text.Json.Serialization;
namespace IBeam.Management.Models;

/// <summary>Safe default fields plus an optional, explicitly marked non-Production raw object; no physical storage keys.</summary>
public sealed record SystemErrorDto(
    string Id, DateTimeOffset OccurredAtUtc, string Source, string Summary, string? CorrelationId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SystemErrorUnsanitized? Unsanitized { get; init; }
}

public sealed record SystemErrorPage(IReadOnlyList<SystemErrorDto> Items, int PageSize, string? ContinuationToken);

public interface ISystemErrorReaderService
{
    Task<SystemErrorPage> ListAsync(DiagnosticQuery query, CancellationToken ct = default);
    Task<SystemErrorDto> GetAsync(string errorId, CancellationToken ct = default);
}

public sealed class ManagementDiagnosticNotFoundException : Exception
{
    public ManagementDiagnosticNotFoundException() : base("Diagnostic record was not found.") { }
}

public sealed class ManagementDiagnosticUnavailableException : Exception
{
    public ManagementDiagnosticUnavailableException() : base("Diagnostic reads are temporarily unavailable.") { }
}
