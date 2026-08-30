namespace IBeam.Communications.Abstractions.Options;

public sealed class SmsInboundOptions
{
    public const string SectionName = "IBeam:Communications:Sms:Inbound";

    /// <summary>
    /// Shared secret expected in the webhook query string (?code=...). Webhook senders
    /// like Event Grid cannot attach a bearer token, so the endpoint is protected by
    /// this secret instead. Empty means the guard rejects every request.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Product/brand name used in default reply copy, e.g. "Bindry".</summary>
    public string BrandName { get; set; } = string.Empty;

    /// <summary>Support contact used in the default HELP reply, e.g. "support@bindry.ai".</summary>
    public string SupportContact { get; set; } = string.Empty;

    /// <summary>Full override for the STOP confirmation reply; null composes a CTIA-standard default.</summary>
    public string? OptOutReplyText { get; set; }

    public string? OptInReplyText { get; set; }

    public string? HelpReplyText { get; set; }

    public string EffectiveOptOutReply =>
        OptOutReplyText ?? $"{BrandName}: You are unsubscribed and will receive no more messages. Reply START to resubscribe or HELP for help.";

    public string EffectiveOptInReply =>
        OptInReplyText ?? $"{BrandName}: You are resubscribed. Reply STOP to unsubscribe or HELP for help.";

    public string EffectiveHelpReply =>
        HelpReplyText ?? $"{BrandName}: Msg&data rates may apply. Support: {SupportContact}. Reply STOP to unsubscribe.";

    public bool Validate()
    {
        var hasAllOverrides =
            !string.IsNullOrWhiteSpace(OptOutReplyText) &&
            !string.IsNullOrWhiteSpace(OptInReplyText) &&
            !string.IsNullOrWhiteSpace(HelpReplyText);

        // The default copy leads with the brand, so either name the brand (and a
        // support contact for HELP) or override every reply outright.
        if (!hasAllOverrides && string.IsNullOrWhiteSpace(BrandName))
            return false;
        if (string.IsNullOrWhiteSpace(HelpReplyText) && string.IsNullOrWhiteSpace(SupportContact))
            return false;

        return true;
    }
}
