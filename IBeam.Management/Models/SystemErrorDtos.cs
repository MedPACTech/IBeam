namespace IBeam.Management.Models;

/// <summary>Explicit public allowlist; no storage keys, request fields, raw trace/source or exception text.</summary>
public sealed record SystemErrorDto(
    string Id, DateTimeOffset OccurredAtUtc, string Source, string Summary, string? CorrelationId);

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
