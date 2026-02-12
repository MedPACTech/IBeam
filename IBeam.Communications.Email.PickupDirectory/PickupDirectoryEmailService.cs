using System.Net.Mail;
using IBeam.Communications.Abstractions;

// Alias to avoid ambiguity if other packages introduce EmailAddress types later
using IBeamEmailAddress = IBeam.Communications.Abstractions.EmailAddress;

namespace IBeam.Communications.Email.PickupDirectory;

public sealed class PickupDirectoryEmailService : IEmailService
{
    private readonly PickupDirectoryEmailOptions _options;

    public PickupDirectoryEmailService(Microsoft.Extensions.Options.IOptions<PickupDirectoryEmailOptions> options)
        => _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    public async Task SendAsync(EmailMessage message, EmailSendOptions? options = null, CancellationToken ct = default)
    {
        const string provider = nameof(PickupDirectoryEmailService);

        EmailDefaults.ValidateMessageForSend(provider, message);

        IBeamEmailAddress from = ResolveFrom(provider, message, options);

        if (string.IsNullOrWhiteSpace(_options.DirectoryPath))
            throw new EmailValidationException(provider, "DirectoryPath is not configured.");

        Directory.CreateDirectory(_options.DirectoryPath);

        using var mail = new MailMessage
        {
            Subject = message.Subject,
            From = ToMailAddress(from),
        };

        foreach (var to in message.To)
            mail.To.Add(ToMailAddress(provider, to));


        // body logic
        if (!string.IsNullOrWhiteSpace(message.TextBody) && !string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            mail.Body = message.TextBody!;
            mail.IsBodyHtml = false;
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody!, null, "text/plain"));
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody!, null, "text/html"));
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

        using var client = new SmtpClient
        {
            DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
            PickupDirectoryLocation = _options.DirectoryPath
        };

        try
        {
            ct.ThrowIfCancellationRequested();
            await client.SendMailAsync(mail).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EmailServiceException(provider, "PickupDirectory email write failed.", ex);
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

    private static MailAddress ToMailAddress(IBeamEmailAddress addr)
    => string.IsNullOrWhiteSpace(addr.DisplayName)
        ? new MailAddress(addr.Address)
        : new MailAddress(addr.Address, addr.DisplayName);

    private static MailAddress ToMailAddress(string provider, string addr)
    {
        if (string.IsNullOrWhiteSpace(addr))
            throw new EmailValidationException(provider, "Recipient email address is required.");

        try { return new MailAddress(addr.Trim()); }
        catch (FormatException ex)
        { throw new EmailValidationException(provider, $"Invalid recipient email address: '{addr}'."); }
    }

}
