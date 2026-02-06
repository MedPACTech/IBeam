using Azure;
using Azure.Communication.Email;
using IBeam.Communications.Email.Abstractions;
using Microsoft.Extensions.Options;

// Aliases to avoid EmailAddress ambiguity
using IBeamEmailAddress = IBeam.Communications.Email.Abstractions.EmailAddress;
using AzureEmailAddress = Azure.Communication.Email.EmailAddress;
using EmailMessage = IBeam.Communications.Email.Abstractions.EmailMessage;

namespace IBeam.Communications.Email.AzureCommunications;

public sealed class AzureCommunicationsEmailService : IEmailService
{
    private readonly AzureCommunicationsEmailOptions _options;
    private readonly EmailClient _client;

    public AzureCommunicationsEmailService(IOptions<AzureCommunicationsEmailOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new ArgumentException("ConnectionString is required.", nameof(options));

        _client = new EmailClient(_options.ConnectionString);
    }

    public async Task SendAsync(EmailMessage message, EmailSendOptions? options = null, CancellationToken ct = default)
    {
        const string provider = nameof(AzureCommunicationsEmailService);

        EmailDefaults.ValidateMessageForSend(provider, message);

        IBeamEmailAddress from = ResolveFrom(provider, message, options);

        var content = new EmailContent(message.Subject)
        {
            PlainText = string.IsNullOrWhiteSpace(message.TextBody) ? null : message.TextBody,
            Html = string.IsNullOrWhiteSpace(message.HtmlBody) ? null : message.HtmlBody
        };

        var recipients = new EmailRecipients(
            message.To
                .Select(t => new AzureEmailAddress(t.Address, t.DisplayName))
                .ToList()
        );

        var azureMessage = new Azure.Communication.Email.EmailMessage(
            senderAddress: from.Address,
            content: content,
            recipients: recipients
        );

        try
        {
            ct.ThrowIfCancellationRequested();

            // WaitUntil.Completed matches your earlier snippet
            _ = await _client.SendAsync(
                wait: WaitUntil.Completed,
                message: azureMessage,
                cancellationToken: ct
            ).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            throw new EmailServiceException(provider, $"ACS email send failed (Status={ex.Status}).", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EmailServiceException(provider, "ACS email send failed.", ex);
        }
    }

    private IBeamEmailAddress ResolveFrom(string provider, EmailMessage message, EmailSendOptions? options)
    {
        if (options?.FromOverride is not null)
            return options.FromOverride;

        if (message.From is not null)
            return message.From;

        if (options?.UseDefaultFromIfMissing ?? true)
        {
            if (string.IsNullOrWhiteSpace(_options.DefaultFromAddress))
                throw new EmailValidationException(provider, "DefaultFromAddress is not configured.");

            return new IBeamEmailAddress(_options.DefaultFromAddress, _options.DefaultFromDisplayName);
        }

        throw new EmailValidationException(provider, "From is required (no sender provided and default sender disabled).");
    }
}
