namespace IBeam.Communications.Core.Options;

public sealed class SmsDefaultsOptions
{
    public const string SectionName = "IBeam:Communications:Sms:Defaults";

    /// <summary>Default "From" phone number (E.164 recommended).</summary>
    public string FromPhoneNumber { get; set; } = string.Empty;
}
