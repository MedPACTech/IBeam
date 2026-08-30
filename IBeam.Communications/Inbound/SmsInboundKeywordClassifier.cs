namespace IBeam.Communications.Abstractions;

public enum SmsInboundKeywordAction
{
    None,
    OptOut,
    OptIn,
    Help
}

/// <summary>
/// CTIA-recommended keyword handling for inbound SMS replies. Keywords are
/// case-insensitive, exact-match on the trimmed message body: anything else
/// (for example a stray reply to an OTP code) is None and gets no auto-reply.
/// </summary>
public static class SmsInboundKeywordClassifier
{
    private static readonly HashSet<string> OptOutKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT"
    };

    private static readonly HashSet<string> OptInKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "START", "UNSTOP", "YES"
    };

    private static readonly HashSet<string> HelpKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "HELP", "INFO"
    };

    public static SmsInboundKeywordAction Classify(string? messageBody)
    {
        var body = messageBody?.Trim();
        if (string.IsNullOrEmpty(body))
            return SmsInboundKeywordAction.None;

        if (OptOutKeywords.Contains(body)) return SmsInboundKeywordAction.OptOut;
        if (OptInKeywords.Contains(body)) return SmsInboundKeywordAction.OptIn;
        if (HelpKeywords.Contains(body)) return SmsInboundKeywordAction.Help;
        return SmsInboundKeywordAction.None;
    }
}
