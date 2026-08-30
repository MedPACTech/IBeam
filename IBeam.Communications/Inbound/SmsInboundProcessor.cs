using IBeam.Communications.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Communications.Abstractions;

/// <summary>One received SMS, already parsed out of a provider webhook payload.</summary>
public sealed record SmsInboundMessage(
    string From,
    string To,
    string Body,
    DateTimeOffset? ReceivedUtc = null);

public sealed record SmsInboundProcessingResult(
    SmsInboundKeywordAction Action,
    bool ConsentRecorded,
    bool ReplySent);

public interface ISmsInboundProcessor
{
    Task<SmsInboundProcessingResult> ProcessAsync(SmsInboundMessage message, CancellationToken ct = default);
}

/// <summary>
/// Provider-neutral STOP/START/HELP compliance pipeline. Provider webhook adapters
/// parse their own payload shape and hand each received message here; this classifies
/// the keyword, records consent through the host app's ISmsConsentStore, and sends the
/// CTIA-required auto-reply via ISmsService. A failed reply send is logged and
/// swallowed so webhook senders do not retry the whole batch and re-run keyword
/// handling for every event in it.
/// </summary>
public sealed class SmsInboundProcessor : ISmsInboundProcessor
{
    public const string OptInSource = "inbound-start";

    private readonly ISmsConsentStore _consent;
    private readonly ISmsService _sms;
    private readonly IOptions<SmsInboundOptions> _options;
    private readonly ILogger<SmsInboundProcessor> _logger;

    public SmsInboundProcessor(
        ISmsConsentStore consent,
        ISmsService sms,
        IOptions<SmsInboundOptions> options,
        ILogger<SmsInboundProcessor> logger)
    {
        _consent = consent ?? throw new ArgumentNullException(nameof(consent));
        _sms = sms ?? throw new ArgumentNullException(nameof(sms));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SmsInboundProcessingResult> ProcessAsync(SmsInboundMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.From))
            return new SmsInboundProcessingResult(SmsInboundKeywordAction.None, false, false);

        var action = SmsInboundKeywordClassifier.Classify(message.Body);
        var options = _options.Value;
        switch (action)
        {
            case SmsInboundKeywordAction.OptOut:
                await _consent.RecordOptOutAsync(message.From, ct).ConfigureAwait(false);
                return new SmsInboundProcessingResult(action, true, await TryReplyAsync(message.From, options.EffectiveOptOutReply, ct).ConfigureAwait(false));

            case SmsInboundKeywordAction.OptIn:
                await _consent.RecordOptInAsync(message.From, OptInSource, ct).ConfigureAwait(false);
                return new SmsInboundProcessingResult(action, true, await TryReplyAsync(message.From, options.EffectiveOptInReply, ct).ConfigureAwait(false));

            case SmsInboundKeywordAction.Help:
                return new SmsInboundProcessingResult(action, false, await TryReplyAsync(message.From, options.EffectiveHelpReply, ct).ConfigureAwait(false));

            default:
                return new SmsInboundProcessingResult(SmsInboundKeywordAction.None, false, false);
        }
    }

    private async Task<bool> TryReplyAsync(string to, string body, CancellationToken ct)
    {
        try
        {
            await _sms.SendAsync(to, body, ct: ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS keyword auto-reply to {To}. Consent state was already recorded.", to);
            return false;
        }
    }
}
