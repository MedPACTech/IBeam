namespace IBeam.Management.Models;

/// <summary>UTC [From, To) window. On continuation, omitted fields retain the protected original query.</summary>
public sealed record DiagnosticQuery
{
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public int? PageSize { get; init; }
    public string? Cursor { get; init; }
    public override string ToString() => "Diagnostic query (cursor redacted)";
}

public sealed record DiagnosticQueryWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc, int PageSize);

public sealed class DiagnosticQueryException : Exception
{
    public DiagnosticQueryException() : base("Diagnostic query exceeds the configured bounds or conflicts with its continuation.") { }
}

public sealed class DiagnosticTokenException : Exception
{
    public DiagnosticTokenException() : base("Diagnostic continuation or record identifier is invalid or expired.") { }
}
