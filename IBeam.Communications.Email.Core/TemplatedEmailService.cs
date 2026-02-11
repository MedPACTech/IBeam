using IBeam.Communications.Email.Abstractions;

namespace IBeam.Communications.Email.Core;

public sealed class TemplatedEmailService : ITemplatedEmailService
{
    private readonly IEmailService _email;
    private readonly IEmailTemplateRenderer _renderer;

    public TemplatedEmailService(IEmailService email, IEmailTemplateRenderer renderer)
    {
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public async Task SendTemplatedEmailAsync(
        string recipient,
        string subject,
        string templateName,
        object? model = null,
        EmailSendOptions? options = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            throw new ArgumentException("Recipient is required.", nameof(recipient));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(templateName))
            throw new ArgumentException("Template name is required.", nameof(templateName));

        var rendered = await _renderer.RenderAsync(templateName, model, ct);

        // Build EmailMessage based on your IBeam model
        var msg = new EmailMessage
        {
            Subject = subject,
            HtmlBody = rendered.Html,
            TextBody = rendered.Text
        };

        // IMPORTANT: adjust this to match your EmailMessage recipient API.
        // Common patterns:
        //   msg.To.Add(new EmailAddress(recipient));
        //   msg.To.Add(recipient);
        //   msg.To = new[] { recipient };
        AddToRecipient(msg, recipient);

        await _email.SendAsync(msg, options, ct);
    }

    private static void AddToRecipient(EmailMessage msg, string recipient)
    {
        // TODO: update once we see EmailMessage shape.
        // Placeholder example:
        msg.To = new[] { recipient };
    }
}
