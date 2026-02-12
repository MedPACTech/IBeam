namespace IBeam.Communications.Abstractions;

public sealed class SmsSendOptions
{
    /// <summary>Per-call override sender. Highest priority.</summary>
    public string? FromPhoneNumber { get; set; }

    /// <summary>Optional provider metadata for future use.</summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
