using Azure;
using Azure.Communication.Sms;
using IBeam.Communications.Abstractions;
using IBeam.Communications.Abstractions.Options;
using IBeam.Communications.Abstractions.Policies;
using IBeam.Communications.Abstractions.Validation;
using Microsoft.Extensions.Options;

namespace IBeam.Communications.Sms.AzureCommunications;

public sealed class AzureCommunicationsSmsService : ISmsService
{
    private const string ProviderName = "AzureCommunicationsSms";
    private readonly SmsClient _client;
    private readonly SmsOptions _defaults;

    public AzureCommunicationsSmsService(
        IOptions<AzureCommunicationsSmsOptions> providerOptions,
        IOptions<SmsOptions> defaults)
    {
        var opt = providerOptions?.Value ?? throw new ArgumentNullException(nameof(providerOptions));
        _defaults = defaults?.Value ?? throw new ArgumentNullException(nameof(defaults));

        if (!AzureCommunicationsSmsConnectionStringValidator.IsValid(opt.ConnectionString))
            throw new SmsConfigurationException(AzureCommunicationsSmsConnectionStringValidator.FailureMessage);

        _client = new SmsClient(opt.ConnectionString);
    }

    public async Task SendAsync(SmsMessage message, SmsOptions? options = null, CancellationToken ct = default)
    {
        SmsMessageValidator.Validate(message);

        var from = SenderResolution.ResolveSmsFrom(options, message, _defaults);
        var body = message.Body.Trim();
        var recipientAddresses = message.To
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        var context = SmsSendContext.Create(from, recipientAddresses, body);

        try
        {
            foreach (var to in recipientAddresses)
            {
                var resp = await _client.SendAsync(from: from, to: to, message: body, cancellationToken: ct)
                    .ConfigureAwait(false);
                var result = resp.Value;

                if (!result.Successful)
                {
                    throw new SmsProviderException(
                        provider: ProviderName,
                        message: BuildProviderMessage(
                            context,
                            "submission",
                            "Azure Communications SMS send failed.",
                            to,
                            result.MessageId,
                            result.HttpStatusCode,
                            result.ErrorMessage),
                        isTransient: false,
                        providerCode: result.HttpStatusCode.ToString());
                }
            }
        }
        catch (SmsProviderException) { throw; }
        catch (RequestFailedException ex)
        {
            throw TranslateAzureException(ex, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SmsProviderException(
                provider: ProviderName,
                message: BuildProviderMessage(
                    context,
                    "submission",
                    "Unexpected Azure Communications SMS provider error.",
                    recipient: null,
                    messageId: null,
                    httpStatusCode: null,
                    providerErrorMessage: ex.Message),
                isTransient: true,
                providerCode: null,
                inner: ex);
        }
    }

    private static SmsProviderException TranslateAzureException(RequestFailedException ex, SmsSendContext context)
    {
        var status = ex.Status;
        var transient = status == 429 || status >= 500;

        var friendly = status switch
        {
            400 => "SMS request rejected (invalid parameters).",
            401 or 403 => "SMS provider authentication/authorization failed.",
            404 => "SMS provider endpoint/resource not found.",
            408 => "SMS provider timeout.",
            429 => "SMS provider rate limit exceeded.",
            _ when status >= 500 => "SMS provider temporary failure.",
            _ => "SMS provider error."
        };

        return new SmsProviderException(
            provider: ProviderName,
            message: BuildProviderMessage(
                context,
                "submission",
                friendly,
                recipient: null,
                messageId: null,
                httpStatusCode: status,
                providerErrorMessage: ex.Message),
            isTransient: transient,
            providerCode: ex.ErrorCode,
            inner: ex);
    }

    private static string BuildProviderMessage(
        SmsSendContext context,
        string stage,
        string summary,
        string? recipient,
        string? messageId,
        int? httpStatusCode,
        string? providerErrorMessage)
    {
        var details = new List<string>
        {
            $"Provider={ProviderName}",
            $"Stage={stage}",
            $"Sender={context.SenderAddress}",
            $"RecipientCount={context.RecipientCount}",
            $"Recipients={context.Recipients}",
            $"Body={context.BodyLabel}"
        };

        AddIfPresent(details, "Recipient", recipient);
        AddIfPresent(details, "MessageId", messageId);
        if (httpStatusCode.HasValue)
            details.Add($"HttpStatus={httpStatusCode.Value}");
        AddIfPresent(details, "ProviderErrorMessage", providerErrorMessage);

        return $"{summary} {string.Join("; ", details)}";
    }

    private static void AddIfPresent(List<string> details, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            details.Add($"{name}={Sanitize(value)}");
    }

    private static string Sanitize(string value)
        => string.Join(' ', value.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));

    private sealed record SmsSendContext(
        string SenderAddress,
        int RecipientCount,
        string Recipients,
        string BodyLabel)
    {
        public static SmsSendContext Create(string senderAddress, IReadOnlyCollection<string> recipients, string body)
        {
            var recipientLabel = recipients.Count == 0
                ? "<none>"
                : string.Join(",", recipients);

            return new SmsSendContext(
                senderAddress,
                recipients.Count,
                recipientLabel,
                ToSafeLabel(body));
        }

        private static string ToSafeLabel(string body)
        {
            var label = Sanitize(body);
            return label.Length <= 120 ? label : label[..120];
        }
    }
}
