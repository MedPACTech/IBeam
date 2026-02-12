namespace IBeam.Communications.Sms.AzureCommunications;

public sealed class AzureCommunicationsSmsOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Optional provider-level default sender (E.164). If null, falls back to SmsOptions.DefaultFromPhoneNumber.
    /// </summary>
    public string? DefaultFromPhoneNumber { get; set; }
}
