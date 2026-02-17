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
        IReadOnlyCollection<string> to,
        string subject,
        string templateName,
        object? model = null,
        EmailSendOptions? options = null,
        CancellationToken ct = default)
    {
        if (to == null || to.Count == 0)
            throw new ArgumentException("At least one recipient is required.", nameof(to));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(templateName))
            throw new ArgumentException("Template name is required.", nameof(templateName));

        var rendered = await _renderer.RenderAsync(templateName, model, ct);

        if (string.IsNullOrWhiteSpace(rendered.HtmlBody) && string.IsNullOrWhiteSpace(rendered.TextBody))
            throw new InvalidOperationException($"Template '{templateName}' rendered empty content.");

        var msg = new EmailMessage
        {
            Subject = subject,
            HtmlBody = rendered.HtmlBody,
            TextBody = rendered.TextBody
        };

        AddToRecipients(msg, to);

        await _email.SendAsync(msg, options, ct);
    }

    private static void AddToRecipients(EmailMessage msg, IReadOnlyCollection<string> to)
    {
        // Recommended EmailMessage shape: List<string> To
        foreach (var recipient in to)
        {
            msg.To.Add(recipient);
        }
    }

}
