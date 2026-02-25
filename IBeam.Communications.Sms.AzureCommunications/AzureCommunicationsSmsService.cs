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
    private const string ProviderName = "AzureCommunicationServices";
    private readonly SmsClient _client;
    private readonly SmsOptions _defaults;

    public AzureCommunicationsSmsService(
        IOptions<AzureCommunicationsSmsOptions> providerOptions,
        IOptions<SmsOptions> defaults)
    {
        var opt = providerOptions?.Value ?? throw new ArgumentNullException(nameof(providerOptions));
        _defaults = defaults?.Value ?? throw new ArgumentNullException(nameof(defaults));

        if (string.IsNullOrWhiteSpace(opt.ConnectionString))
            throw new SmsConfigurationException("Azure Communications SMS ConnectionString is required.");

        _client = new SmsClient(opt.ConnectionString);
    }

    public async Task SendAsync(SmsMessage message, SmsOptions? options = null, CancellationToken ct = default)
    {
        SmsMessageValidator.Validate(message);

        var from = SenderResolution.ResolveSmsFrom(options, message, _defaults);
        var body = message.Body.Trim();

        try
        {
            foreach (var to in message.To.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()))
            {
                var resp = await _client.SendAsync(from: from, to: to, message: body, cancellationToken: ct);
                var result = resp.Value;

                if (!result.Successful)
                {
                    throw new SmsProviderException(
                        provider: ProviderName,
                        message: string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "SMS provider reported failure."
                            : result.ErrorMessage,
                        isTransient: false,
                        providerCode: result.HttpStatusCode.ToString());
                }
            }
        }
        catch (RequestFailedException ex)
        {
            throw TranslateAzureException(ex);
        }
        catch (Exception ex)
        {
            throw new SmsProviderException(
                provider: ProviderName,
                message: "Unexpected SMS provider error.",
                isTransient: true,
                providerCode: null,
                inner: ex);
        }
    }

    private static SmsProviderException TranslateAzureException(RequestFailedException ex)
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
            message: friendly,
            isTransient: transient,
            providerCode: ex.ErrorCode,
            inner: ex);
    }
}
