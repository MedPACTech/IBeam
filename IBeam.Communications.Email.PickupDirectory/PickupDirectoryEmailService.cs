using System.Net.Mail;
using IBeam.Communications.Abstractions;
using IBeam.Communications.Abstractions.Policies;
using IBeam.Communications.Abstractions.Validation;
using Microsoft.Extensions.Options;

// Alias to avoid ambiguity if other packages introduce EmailAddress types later
using IBeamEmailAddress = IBeam.Communications.Abstractions.EmailAddress;

namespace IBeam.Communications.Email.PickupDirectory;

public sealed class PickupDirectoryEmailService : IEmailService
{
    private readonly PickupDirectoryEmailOptions _options;
    private readonly EmailOptions _defaults;

    public PickupDirectoryEmailService(
        IOptions<PickupDirectoryEmailOptions> options,
        IOptions<EmailOptions> defaults)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _defaults = defaults?.Value ?? throw new ArgumentNullException(nameof(defaults));
    }

    public async Task SendAsync(EmailMessage message, EmailOptions? options = null, CancellationToken ct = default)
    {
        const string providerName = nameof(PickupDirectoryEmailService);

        EmailMessageValidator.Validate(message);

        var (fromAddress, fromName) = SenderResolution.ResolveEmailFrom(options, message, _defaults);

        if (string.IsNullOrWhiteSpace(_options.DirectoryPath))
            throw new EmailConfigurationException("PickupDirectory DirectoryPath is not configured.");

        Directory.CreateDirectory(_options.DirectoryPath);

        using var mail = new MailMessage
        {
            Subject = message.Subject,
            From = ToMailAddress(new IBeamEmailAddress(fromAddress, fromName)),
        };

        foreach (var to in message.To)
            mail.To.Add(ToMailAddress(to));


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
            throw new EmailProviderException(
                provider: providerName,
                message: "PickupDirectory email write failed.",
                isTransient: false,
                inner: ex);
        }
    }

    private static MailAddress ToMailAddress(IBeamEmailAddress addr)
    => string.IsNullOrWhiteSpace(addr.DisplayName)
        ? new MailAddress(addr.Address)
        : new MailAddress(addr.Address, addr.DisplayName);

    private static MailAddress ToMailAddress(string addr)
    {
        if (string.IsNullOrWhiteSpace(addr))
            throw new EmailValidationException("Recipient email address is required.");

        try { return new MailAddress(addr.Trim()); }
        catch (FormatException ex)
        { throw new EmailValidationException($"Invalid recipient email address: '{addr}'.", ex); }
    }

}
