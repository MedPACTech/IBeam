namespace IBeam.Communications.Abstractions;

public sealed class TemplatedEmailService : ITemplatedEmailService
{
    private readonly IEmailTemplateRenderer _renderer;
    private readonly IEmailService _emailService;

    public TemplatedEmailService(IEmailTemplateRenderer renderer, IEmailService emailService)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
    }

    public async Task SendTemplatedEmailAsync(
        IReadOnlyCollection<string> to,
        string subject,
        string templateName,
        object? model = null,
        EmailOptions? options = null,
        CancellationToken ct = default)
    {
        if (to is null || to.Count == 0)
            throw new EmailValidationException("At least one recipient is required.");
        if (string.IsNullOrWhiteSpace(subject))
            throw new EmailValidationException("Subject is required.");
        if (string.IsNullOrWhiteSpace(templateName))
            throw new EmailTemplateException("Template name is required.");

        var rendered = await _renderer.RenderAsync(templateName, model, ct);
        var message = new EmailMessage
        {
            Subject = subject,
            HtmlBody = rendered.HtmlBody,
            TextBody = rendered.TextBody
        };

        foreach (var recipient in to.Where(x => !string.IsNullOrWhiteSpace(x)))
            message.To.Add(recipient.Trim());

        await _emailService.SendAsync(message, options, ct);
    }
}
