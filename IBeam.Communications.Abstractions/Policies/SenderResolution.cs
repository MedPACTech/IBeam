using IBeam.Communications.Abstractions;
using IBeam.Communications.Abstractions.Options;

namespace IBeam.Communications.Abstractions.Policies;

public static class SenderResolution
{

    //TODO: Currently we provide no override options for SMS or Email, but we may want to in the future,


    public static (string FromAddress, string? FromName) ResolveEmailFrom(
        EmailOptions? options,
        EmailMessage message,
        EmailOptions defaults)
    {
        var fromAddress =
            FirstNonEmpty(options?.FromAddress) ??
            FirstNonEmpty(message.FromAddress) ??
            FirstNonEmpty(defaults.FromAddress);

        if (string.IsNullOrWhiteSpace(fromAddress))
            throw new EmailConfigurationException("No FromAddress provided and no default configured.");

        var fromName =
            FirstNonEmpty(options?.FromName) ??
            FirstNonEmpty(message.FromName) ??
            FirstNonEmpty(defaults.FromName);

        return (fromAddress!, fromName);
    }

    public static string ResolveSmsFrom(
        SmsOptions? options,
        SmsMessage message,
        SmsOptions defaults)
    {
        var from =
            FirstNonEmpty(options?.FromPhoneNumber) ??
            FirstNonEmpty(message.FromPhoneNumber) ??
            FirstNonEmpty(defaults.FromPhoneNumber);

        if (string.IsNullOrWhiteSpace(from))
            throw new SmsConfigurationException("No FromPhoneNumber provided and no default configured.");

        return from!;
    }

    private static string? FirstNonEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
