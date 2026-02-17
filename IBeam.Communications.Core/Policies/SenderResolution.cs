using IBeam.Communications.Abstractions;
using IBeam.Communications.Core.Options;

namespace IBeam.Communications.Core.Policies;

public static class SenderResolution
{
    public static (string FromAddress, string? FromName) ResolveEmailFrom(
        EmailSendOptions? options,
        EmailMessage message,
        EmailDefaultsOptions defaults)
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
        SmsSendOptions? options,
        SmsMessage message,
        SmsDefaultsOptions defaults)
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
