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
    private const string ProviderName = "AzureCommunicationServices";
    private readonly EmailClient _client;
    private readonly EmailOptions _defaults;

    public AzureCommunicationsEmailService(
        IOptions<AzureCommunicationsEmailOptions> providerOptions,
        IOptions<EmailOptions> defaults)
    {
        var opt = providerOptions?.Value ?? throw new ArgumentNullException(nameof(providerOptions));
        _defaults = defaults?.Value ?? throw new ArgumentNullException(nameof(defaults));

        if (string.IsNullOrWhiteSpace(opt.ConnectionString))
            throw new EmailConfigurationException("Azure Communications Email ConnectionString is required.");

        _client = new EmailClient(opt.ConnectionString);
    }

    public async Task SendAsync(EmailMessage message, EmailOptions? options = null, CancellationToken ct = default)
    {
        EmailMessageValidator.Validate(message);

        var (fromAddress, fromName) = SenderResolution.ResolveEmailFrom(options, message, _defaults);

        try
        {
            var content = new EmailContent(message.Subject)
            {
                PlainText = message.TextBody,
                Html = message.HtmlBody
            };

            var toList = message.To
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => new Azure.Communication.Email.EmailAddress(x))
                .ToList();

            var recipients = new EmailRecipients(toList);

            // Keep sender as raw email address for ACS
            var acsMessage = new Azure.Communication.Email.EmailMessage(
                senderAddress: fromAddress,
                recipients: recipients,
                content: content);

            await _client.SendAsync(WaitUntil.Completed, acsMessage, ct);
        }
        catch (RequestFailedException ex)
        {
            throw TranslateAzureException(ex);
        }
        catch (Exception ex)
        {
            throw new EmailProviderException(
                provider: ProviderName,
                message: "Unexpected email provider error.",
                isTransient: true,
                providerCode: null,
                inner: ex);
        }
    }

    private static EmailProviderException TranslateAzureException(RequestFailedException ex)
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
            message: friendly,
            isTransient: transient,
            providerCode: ex.ErrorCode,
            inner: ex);
    }
}
