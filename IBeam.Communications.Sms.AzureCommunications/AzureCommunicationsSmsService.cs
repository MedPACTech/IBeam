using Azure;
using Azure.Communication.Sms;
using IBeam.Communications.Abstractions;
using Microsoft.Extensions.Options;
//using AzureSmsSendOptions = Azure.Communication.Sms.SmsSendOptions;
using SmsSendOptions = IBeam.Communications.Abstractions.SmsSendOptions;


namespace IBeam.Communications.Sms.AzureCommunications;

public sealed class AzureCommunicationsSmsService : ISmsService
{
    private const string ProviderName = "AzureCommunicationServices";

    private readonly SmsClient _client;
    private readonly AzureCommunicationsSmsOptions _providerOptions;
    private readonly SmsOptions _smsOptions;

    public AzureCommunicationsSmsService(
        IOptions<AzureCommunicationsSmsOptions> providerOptions,
        IOptions<SmsOptions> smsOptions)
    {
        _providerOptions = providerOptions?.Value ?? throw new ArgumentNullException(nameof(providerOptions));
        _smsOptions = smsOptions?.Value ?? new SmsOptions();

        if (string.IsNullOrWhiteSpace(_providerOptions.ConnectionString))
            throw new SmsConfigurationException("Azure Communications SMS ConnectionString is required.");

        _client = new SmsClient(_providerOptions.ConnectionString);
    }

    public async Task SendAsync(SmsMessage message, SmsSendOptions? options = null, CancellationToken ct = default)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        var to = Normalize(message.ToPhoneNumber);
        var body = (message.Body ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(to))
            throw new SmsValidationException("ToPhoneNumber is required.");

        if (string.IsNullOrWhiteSpace(body))
            throw new SmsValidationException("Body is required.");

        var from = ResolveFrom(options?.FromPhoneNumber, message.FromPhoneNumber);

        try
        {
            // Azure SDK call: SendAsync(from, to, message)
            var resp = await _client.SendAsync(from: from, to: to, message: body, cancellationToken: ct);

            var result = resp.Value;

            if (!result.Successful)
            {
                throw new SmsProviderException(
                    provider: ProviderName,
                    message: string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? "SMS provider reported failure."
                        : result.ErrorMessage,
                    isTransient: false,                 // usually permanent when provider says “no”
                    providerCode: result.HttpStatusCode.ToString());
            }

        }
        catch (RequestFailedException ex)
        {
            throw TranslateAzureException(ex);
        }
        catch (Exception ex)
        {
            // Unknown error; treat as transient by default
            throw new SmsProviderException(
                provider: ProviderName,
                message: "Unexpected SMS provider error.",
                isTransient: true,
                providerCode: null,
                inner: ex);
        }
    }

    private string ResolveFrom(string? overrideFrom, string? messageFrom)
    {
        // Standard IBeam resolution rule:
        // override -> message -> provider default -> shared default -> error
        var from =
            FirstNonEmpty(overrideFrom) ??
            FirstNonEmpty(messageFrom) ??
            FirstNonEmpty(_providerOptions.DefaultFromPhoneNumber) ??
            FirstNonEmpty(_smsOptions.DefaultFromPhoneNumber);

        if (string.IsNullOrWhiteSpace(from))
            throw new SmsConfigurationException("No FromPhoneNumber provided and no DefaultFromPhoneNumber configured.");

        return from!;
    }

    private static string? FirstNonEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Normalize(string? phone)
        => (phone ?? string.Empty).Trim(); // later: E.164 normalization if you want

    private static SmsProviderException TranslateAzureException(RequestFailedException ex)
    {
        // Heuristic: 429 + 5xx transient, most other 4xx permanent
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
            message: friendly,
            isTransient: transient,
            providerCode: ex.ErrorCode,
            inner: ex);
    }
}
