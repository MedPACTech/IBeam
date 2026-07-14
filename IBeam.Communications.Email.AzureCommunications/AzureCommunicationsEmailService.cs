using Azure;
using Azure.Communication.Email;
using IBeam.Communications.Abstractions;
using IBeam.Communications.Abstractions.Policies;
using IBeam.Communications.Abstractions.Validation;
using Microsoft.Extensions.Options;
using EmailMessage = IBeam.Communications.Abstractions.EmailMessage;

namespace IBeam.Communications.Email.AzureCommunications;

public sealed class AzureCommunicationsEmailService : IEmailService
{
    private const string ProviderName = "AzureCommunications";
    private readonly EmailClient _client;
    private readonly EmailOptions _defaults;

    public AzureCommunicationsEmailService(
        IOptions<AzureCommunicationsEmailOptions> providerOptions,
        IOptions<EmailOptions> defaults)
    {
        var opt = providerOptions?.Value ?? throw new ArgumentNullException(nameof(providerOptions));
        _defaults = defaults?.Value ?? throw new ArgumentNullException(nameof(defaults));

        if (!AzureCommunicationsConnectionStringValidator.IsValid(opt.ConnectionString))
            throw new EmailConfigurationException(AzureCommunicationsConnectionStringValidator.FailureMessage);

        _client = new EmailClient(opt.ConnectionString);
    }

    public async Task SendAsync(EmailMessage message, EmailOptions? options = null, CancellationToken ct = default)
    {
        EmailMessageValidator.Validate(message);

        var (fromAddress, fromName) = SenderResolution.ResolveEmailFrom(options, message, _defaults);
        var recipientAddresses = message.To
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        var context = EmailSendContext.Create(fromAddress, recipientAddresses, message.Subject);

        try
        {
            var content = new EmailContent(message.Subject)
            {
                PlainText = message.TextBody,
                Html = message.HtmlBody
            };

            var toList = recipientAddresses
                .Select(x => new Azure.Communication.Email.EmailAddress(x))
                .ToList();

            var recipients = new EmailRecipients(toList);

            // Keep sender as raw email address for ACS
            var acsMessage = new Azure.Communication.Email.EmailMessage(
                senderAddress: fromAddress,
                recipients: recipients,
                content: content);

            var operation = await _client.SendAsync(WaitUntil.Completed, acsMessage, ct).ConfigureAwait(false);
            var result = operation.HasValue ? operation.Value : null;
            var rawResponse = operation.GetRawResponse();

            if (result is not null && result.Status == EmailSendStatus.Failed)
            {
                throw new EmailProviderException(
                    provider: ProviderName,
                    message: BuildProviderMessage(
                        context,
                        "provider processing",
                        "Azure Communications email send failed.",
                        rawResponse,
                        operation.Id,
                        result.Status.ToString(),
                        azureErrorCode: null,
                        azureErrorMessage: null),
                    isTransient: false,
                    providerCode: null);
            }
        }
        catch (EmailProviderException) { throw; }
        catch (RequestFailedException ex)
        {
            throw TranslateAzureException(ex, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EmailProviderException(
                provider: ProviderName,
                message: BuildProviderMessage(
                    context,
                    "submission",
                    "Unexpected Azure Communications email provider error.",
                    response: null,
                    operationId: null,
                    providerStatus: null,
                    azureErrorCode: null,
                    azureErrorMessage: ex.Message),
                isTransient: true,
                providerCode: null,
                inner: ex);
        }
    }

    private static EmailProviderException TranslateAzureException(RequestFailedException ex, EmailSendContext context)
    {
        var status = ex.Status;
        var transient = status == 429 || status >= 500;

        var friendly = status switch
        {
            400 => "Email request rejected (invalid parameters).",
            401 or 403 => "Email provider authentication/authorization failed.",
            404 => "Email provider endpoint/resource not found.",
            408 => "Email provider timeout.",
            429 => "Email provider rate limit exceeded.",
            _ when status >= 500 => "Email provider temporary failure.",
            _ => "Email provider error."
        };

        return new EmailProviderException(
            provider: ProviderName,
            message: BuildProviderMessage(
                context,
                "submission",
                friendly,
                ex.GetRawResponse(),
                operationId: null,
                providerStatus: null,
                azureErrorCode: ex.ErrorCode,
                azureErrorMessage: ex.Message),
            isTransient: transient,
            providerCode: ex.ErrorCode,
            inner: ex);
    }

    private static string BuildProviderMessage(
        EmailSendContext context,
        string stage,
        string summary,
        Response? response,
        string? operationId,
        string? providerStatus,
        string? azureErrorCode,
        string? azureErrorMessage)
    {
        var requestId = GetAzureRequestId(response);
        var httpStatus = response?.Status;
        var clientRequestId = response?.ClientRequestId;

        var details = new List<string>
        {
            $"Provider={ProviderName}",
            $"Stage={stage}",
            $"Sender={context.SenderAddress}",
            $"RecipientCount={context.RecipientCount}",
            $"Recipients={context.Recipients}",
            $"Subject={context.SubjectLabel}"
        };

        AddIfPresent(details, "OperationId", operationId);
        AddIfPresent(details, "AzureRequestId", requestId);
        AddIfPresent(details, "ClientRequestId", clientRequestId);
        if (httpStatus.HasValue)
            details.Add($"HttpStatus={httpStatus.Value}");
        AddIfPresent(details, "ProviderStatus", providerStatus);
        AddIfPresent(details, "AzureErrorCode", azureErrorCode);
        AddIfPresent(details, "AzureErrorMessage", azureErrorMessage);

        return $"{summary} {string.Join("; ", details)}";
    }

    private static string? GetAzureRequestId(Response? response)
    {
        if (response is null)
            return null;

        return response.Headers.TryGetValue("x-ms-request-id", out var requestId)
            ? requestId
            : null;
    }

    private static void AddIfPresent(List<string> details, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            details.Add($"{name}={Sanitize(value)}");
    }

    private static string Sanitize(string value)
        => string.Join(' ', value.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));

    private sealed record EmailSendContext(
        string SenderAddress,
        int RecipientCount,
        string Recipients,
        string SubjectLabel)
    {
        public static EmailSendContext Create(string senderAddress, IReadOnlyCollection<string> recipients, string subject)
        {
            var recipientLabel = recipients.Count == 0
                ? "<none>"
                : string.Join(",", recipients);

            return new EmailSendContext(
                senderAddress,
                recipients.Count,
                recipientLabel,
                ToSafeLabel(subject));
        }

        private static string ToSafeLabel(string subject)
        {
            var label = Sanitize(subject);
            return label.Length <= 120 ? label : label[..120];
        }
    }
}
