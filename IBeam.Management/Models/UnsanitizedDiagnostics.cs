using System.Text.Json.Serialization;
namespace IBeam.Management.Models;
/// <summary>Explicitly opted-in diagnostic text. Never attach to management audit or service log context.</summary>
public sealed record SystemErrorUnsanitized(string? Source, string? TraceId, string? Message, string? Exception, string? Path, string? Method)
{
    public override string ToString() => "Unsanitized error fields (redacted)";
}
public sealed record SystemLogUnsanitized(string? Source, string? Level, string? TraceId, string? Message, string? Detail)
{
    public override string ToString() => "Unsanitized log fields (redacted)";
}
