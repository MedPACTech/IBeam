using IBeam.Communications.Abstractions;
using IBeam.Communications.Abstractions.Policies;
using IBeam.Communications.Abstractions.Validation;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace IBeam.Communications.Email.Smtp;

public sealed class SmtpEmailService : IEmailService
{
    private readonly SmtpEmailOptions _options;
    private readonly EmailOptions _defaults;

    public SmtpEmailService(IOptions<SmtpEmailOptions> options, IOptions<EmailOptions> defaults)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _defaults = defaults?.Value ?? throw new ArgumentNullException(nameof(defaults));
    }

    public async Task SendAsync(EmailMessage message, EmailOptions? options = null, CancellationToken ct = default)
    {
        const string providerName = nameof(SmtpEmailService);

        EmailMessageValidator.Validate(message);

        var (fromAddress, fromName) = SenderResolution.ResolveEmailFrom(options, message, _defaults);
        var from = new EmailAddress(fromAddress, fromName);

        using var mail = new MailMessage
        {
            Subject = message.Subject,
            From = ToMailAddress(from),
        };

        foreach (var to in message.To)
            mail.To.Add(ToMailAddress(to));


        // If both provided, add alternate views
        if (!string.IsNullOrWhiteSpace(message.TextBody) && !string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            mail.Body = message.TextBody!;
            mail.IsBodyHtml = false;

            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.TextBody!, null, "text/plain"));

            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.HtmlBody!, null, "text/html"));
        }
        else if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            mail.Body = message.HtmlBody!;
            mail.IsBodyHtml = true;
        }
        else
        {
            mail.Body = message.TextBody ?? "";
            mail.IsBodyHtml = false;
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            UseDefaultCredentials = _options.UseDefaultCredentials
        };

        if (!_options.UseDefaultCredentials && !string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        try
        {
            // SmtpClient does not accept CancellationToken; honor it before/after.
            ct.ThrowIfCancellationRequested();
            await client.SendMailAsync(mail).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EmailProviderException(
                provider: providerName,
                message: "SMTP email send failed.",
                isTransient: true,
                inner: ex);
        }
    }

    private static MailAddress ToMailAddress(EmailAddress addr)
        => string.IsNullOrWhiteSpace(addr.DisplayName)
            ? new MailAddress(addr.Address)
            : new MailAddress(addr.Address, addr.DisplayName);

    private static MailAddress ToMailAddress(string addr)
    {
        if (string.IsNullOrWhiteSpace(addr))
            throw new EmailValidationException("Recipient email address is required.");

        try { return new MailAddress(addr.Trim()); }
        catch (FormatException)
        { throw new EmailValidationException($"Invalid recipient email address: '{addr}'."); }
    }

}
