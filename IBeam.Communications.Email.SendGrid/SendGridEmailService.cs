using IBeam.Communications.Abstractions;
using IBeam.Communications.Abstractions.Policies;
using IBeam.Communications.Abstractions.Validation;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

using IBeamEmailAddress = IBeam.Communications.Abstractions.EmailAddress;

namespace IBeam.Communications.Email.SendGrid;

public sealed class SendGridEmailService : IEmailService
{
    private readonly SendGridEmailOptions _options;
    private readonly EmailOptions _defaults;
    private readonly ISendGridClient _client;

    public SendGridEmailService(IOptions<SendGridEmailOptions> options, IOptions<EmailOptions> defaults)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _defaults = defaults?.Value ?? throw new ArgumentNullException(nameof(defaults));

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new ArgumentException("ApiKey is required.", nameof(options));

        _client = new SendGridClient(_options.ApiKey);
    }

    public async Task SendAsync(EmailMessage message, EmailOptions? options = null, CancellationToken ct = default)
    {
        const string providerName = nameof(SendGridEmailService);

        EmailMessageValidator.Validate(message);

        var (fromAddress, fromName) = SenderResolution.ResolveEmailFrom(options, message, _defaults);
        IBeamEmailAddress from = new(fromAddress, fromName);

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
                throw new EmailProviderException(
                    provider: providerName,
                    message: $"SendGrid email send failed (Status={(int)response.StatusCode}): {body}",
                    isTransient: (int)response.StatusCode == 429 || (int)response.StatusCode >= 500,
                    providerCode: ((int)response.StatusCode).ToString());
            }
        }
        catch (EmailProviderException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EmailProviderException(
                provider: providerName,
                message: "SendGrid email send failed.",
                isTransient: true,
                inner: ex);
        }
    }
}
