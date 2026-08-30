using System.Text.Json;
using System.Text.Json.Serialization;
using IBeam.Communications.Abstractions;
using Microsoft.Extensions.Logging;

namespace IBeam.Communications.Sms.AzureCommunications;

/// <summary>
/// Echo body for the one-time Event Grid subscription-validation handshake.
/// Serialize this as the webhook's 200 response when it is non-null.
/// </summary>
public sealed record EventGridValidationResponse(
    [property: JsonPropertyName("validationResponse")] string ValidationResponse);

public sealed record AzureSmsInboundWebhookResult(
    EventGridValidationResponse? ValidationResponse,
    IReadOnlyList<SmsInboundProcessingResult> Processed);

public interface IAzureSmsInboundWebhookHandler
{
    /// <summary>
    /// Handles one Event Grid webhook POST body (a JSON array of events): answers the
    /// subscription-validation handshake and routes Microsoft.Communication.SMSReceived
    /// events through the STOP/START/HELP compliance pipeline. The host controller
    /// stays thin: guard the shared secret, pass the raw body here, and return the
    /// validation response when present.
    /// </summary>
    Task<AzureSmsInboundWebhookResult> HandleAsync(string requestBody, CancellationToken ct = default);
}

public sealed class AzureEventGridSmsInboundHandler : IAzureSmsInboundWebhookHandler
{
    public const string SubscriptionValidationEventType = "Microsoft.EventGrid.SubscriptionValidationEvent";
    public const string SmsReceivedEventType = "Microsoft.Communication.SMSReceived";

    private readonly ISmsInboundProcessor _processor;
    private readonly ILogger<AzureEventGridSmsInboundHandler> _logger;

    public AzureEventGridSmsInboundHandler(
        ISmsInboundProcessor processor,
        ILogger<AzureEventGridSmsInboundHandler> logger)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AzureSmsInboundWebhookResult> HandleAsync(string requestBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestBody))
            return new AzureSmsInboundWebhookResult(null, []);

        using var document = JsonDocument.Parse(requestBody);
        var events = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : [document.RootElement];

        EventGridValidationResponse? validation = null;
        var processed = new List<SmsInboundProcessingResult>();

        foreach (var eventElement in events)
        {
            var eventType = GetString(eventElement, "eventType");
            if (!eventElement.TryGetProperty("data", out var data))
                continue;

            if (string.Equals(eventType, SubscriptionValidationEventType, StringComparison.OrdinalIgnoreCase))
            {
                var code = GetString(data, "validationCode");
                if (!string.IsNullOrWhiteSpace(code))
                    validation = new EventGridValidationResponse(code);
                continue;
            }

            if (!string.Equals(eventType, SmsReceivedEventType, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Ignoring Event Grid event of type {EventType}.", eventType);
                continue;
            }

            var message = new SmsInboundMessage(
                From: GetString(data, "from") ?? string.Empty,
                To: GetString(data, "to") ?? string.Empty,
                Body: GetString(data, "message") ?? string.Empty,
                ReceivedUtc: TryGetDate(data, "receivedTimestamp"));

            processed.Add(await _processor.ProcessAsync(message, ct).ConfigureAwait(false));
        }

        return new AzureSmsInboundWebhookResult(validation, processed);
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? TryGetDate(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.String &&
           value.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : null;
}
