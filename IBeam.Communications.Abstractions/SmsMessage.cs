namespace IBeam.Communications.Abstractions;

/// <summary>
/// Transport model used by apps/services to send SMS.
/// FromPhoneNumber is optional (provider can supply a configured default).
/// </summary>
public sealed class SmsMessage
{
    public List<string> To { get; } = new();           // phone numbers (ideally E.164)
    public string Body { get; set; } = string.Empty;   // SMS content
    public string? FromPhoneNumber { get; set; }       // optional
}
