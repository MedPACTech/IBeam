namespace IBeam.Communications.Abstractions;

public sealed class SmsOptions
{
    /// <summary>Fallback default sender for SMS (E.164 recommended).</summary>
    public string? DefaultFromPhoneNumber { get; set; }
}
