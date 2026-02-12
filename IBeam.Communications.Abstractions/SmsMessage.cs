using System;
namespace IBeam.Communications.Abstractions;

public sealed class SmsMessage
{
    // Required
    public string ToPhoneNumber { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    // Optional: may be provided per-message; otherwise resolve from options/defaults
    public string? FromPhoneNumber { get; set; }

    // Optional metadata you can pass through internally (not required)
    public string? CorrelationId { get; set; }
}
