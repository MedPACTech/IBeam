using IBeam.Communications.Abstractions;

namespace IBeam.Communications.Email.Templating;

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

        if (string.IsNullOrWhiteSpace(rendered.Html) && string.IsNullOrWhiteSpace(rendered.Text))
            throw new InvalidOperationException($"Template '{templateName}' rendered empty content.");

        var msg = new EmailMessage
        {
            Subject = subject,
            HtmlBody = rendered.Html,
            TextBody = rendered.Text
        };

        AddToRecipient(msg, recipient);

        await _email.SendAsync(msg, options, ct);
    }

    private static void AddToRecipient(EmailMessage msg, string recipient)
    {
        // Recommended EmailMessage shape: List<string> To
        msg.To.Add(recipient);
    }
}
