using IBeam.Communications.Abstractions;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

using IBeamEmailAddress = IBeam.Communications.Abstractions.EmailAddress;

namespace IBeam.Communications.Email.SendGrid;

public sealed class SendGridEmailService : IEmailService
{
    private readonly SendGridEmailOptions _options;
    private readonly ISendGridClient _client;

    public SendGridEmailService(IOptions<SendGridEmailOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new ArgumentException("ApiKey is required.", nameof(options));

        _client = new SendGridClient(_options.ApiKey);
    }

    public async Task SendAsync(EmailMessage message, EmailSendOptions? options = null, CancellationToken ct = default)
    {
        const string provider = nameof(SendGridEmailService);

        EmailDefaults.ValidateMessageForSend(provider, message);

        IBeamEmailAddress from = ResolveFrom(provider, message, options);

        var sgMessage = new SendGridMessage
        {
            From = SendGridAddressMapper.ToSendGrid(from),
            Subject = message.Subject
        };

        // bodies
        if (!string.IsNullOrWhiteSpace(message.TextBody))
            sgMessage.PlainTextContent = message.TextBody;

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
            sgMessage.HtmlContent = message.HtmlBody;

        // recipients
        sgMessage.AddTos(
            message.To
                .Select(addr => new IBeamEmailAddress(addr))
                .Select(SendGridAddressMapper.ToSendGrid)
                .ToList()
        );


        // sandbox
        if (_options.SandboxMode)
        {
            sgMessage.MailSettings ??= new MailSettings();
            sgMessage.MailSettings.SandboxMode = new SandboxMode { Enable = true };
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            var response = await _client.SendEmailAsync(sgMessage, ct).ConfigureAwait(false);

            if ((int)response.StatusCode >= 400)
            {
                var body = await response.Body.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new EmailServiceException(provider,
                    $"SendGrid email send failed (Status={(int)response.StatusCode}): {body}");
            }
        }
        catch (EmailServiceException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EmailServiceException(provider, "SendGrid email send failed.", ex);
        }
    }

    private IBeamEmailAddress ResolveFrom(string provider, EmailMessage message, EmailSendOptions? options)
    {
        if (options?.FromOverride is not null)
            return options.FromOverride;

        if (!string.IsNullOrWhiteSpace(message.FromAddress))
            return new IBeamEmailAddress(message.FromAddress, message.FromName);

        if (options?.UseDefaultFromIfMissing ?? true)
        {
            if (string.IsNullOrWhiteSpace(_options.DefaultFromAddress))
                throw new EmailValidationException(provider, "DefaultFromAddress is not configured.");

            return new IBeamEmailAddress(_options.DefaultFromAddress, _options.DefaultFromDisplayName);
        }

        throw new EmailValidationException(provider, "From is required (no sender provided and default sender disabled).");
    }

}
