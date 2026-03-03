using System.Text.RegularExpressions;

namespace IBeam.Communications.Abstractions.Options;

public sealed class SmsOptions
{
    public const string SectionName = "IBeam:Communications:Sms";

    /// <summary>Default "From" phone number (E.164 recommended).</summary>
    public string FromPhoneNumber { get; set; } = string.Empty;

    public bool DefaultToUs { get; set; } = true;

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(FromPhoneNumber))
            return false;

        //TODO: move to Util functions project, make compiled
        // E.164: + followed by 10-15 digits
        return Regex.IsMatch(FromPhoneNumber, @"^\+\d{10,15}$");
    }
}
