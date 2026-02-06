using IBeam.Communications.Email.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace IBeam.Communications.Email.Smtp;

public sealed class SmtpEmailService : IEmailService
{
    private readonly SmtpEmailOptions _options;

    public SmtpEmailService(IOptions<SmtpEmailOptions> options)
        => _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    public async Task SendAsync(EmailMessage message, EmailSendOptions? options = null, CancellationToken ct = default)
    {
        const string provider = nameof(SmtpEmailService);

        EmailDefaults.ValidateMessageForSend(provider, message);

        var from = ResolveFrom(provider, message, options);

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
            throw new EmailServiceException(provider, "SMTP email send failed.", ex);
        }
    }

    private EmailAddress ResolveFrom(string provider, EmailMessage message, EmailSendOptions? options)
    {
        if (options?.FromOverride is not null)
            return options.FromOverride;

        if (message.From is not null)
            return message.From;

        if (options?.UseDefaultFromIfMissing ?? true)
        {
            if (string.IsNullOrWhiteSpace(_options.DefaultFromAddress))
                throw new EmailValidationException(provider, "DefaultFromAddress is not configured.");

            return new EmailAddress(_options.DefaultFromAddress, _options.DefaultFromDisplayName);
        }

        throw new EmailValidationException(provider, "From is required (no sender provided and default sender disabled).");
    }

    private static MailAddress ToMailAddress(EmailAddress addr)
        => string.IsNullOrWhiteSpace(addr.DisplayName)
            ? new MailAddress(addr.Address)
            : new MailAddress(addr.Address, addr.DisplayName);
}
