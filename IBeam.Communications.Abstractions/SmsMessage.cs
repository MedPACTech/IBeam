namespace IBeam.Communications.Abstractions;

public sealed class SmsMessage
{
    /// <summary>Destination phone number (recommend E.164).</summary>
    public string ToPhoneNumber { get; set; } = string.Empty;

    /// <summary>Message text.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Optional per-message sender. If null, resolved from options/defaults.</summary>
    public string? FromPhoneNumber { get; set; }

    /// <summary>Optional correlation id for logs/trace.</summary>
    public string? CorrelationId { get; set; }
}
