namespace IBeam.Identity.Models;

public sealed record AuthAttemptContext(
    string? IpAddress = null,
    string? UserAgent = null,
    string? DeviceId = null,
    string? Country = null,
    string? Region = null,
    string? City = null,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
