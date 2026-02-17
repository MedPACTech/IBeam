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
            throw new EmailValidationException("At least one recipient is required.");

        if (string.IsNullOrWhiteSpace(subject))
            throw new EmailValidationException("Subject is required.");

        if (string.IsNullOrWhiteSpace(templateName))
            throw new EmailValidationException("Template name is required.");

        RenderedEmailTemplate rendered;
        try
        {
            rendered = await _renderer.RenderAsync(templateName, model, ct);
        }
        catch (EmailTemplateNotFoundException)
        {
            throw; // keep it specific
        }
        catch (Exception ex)
        {
            throw new EmailTemplateException($"Failed to render template '{templateName}'.", ex);
        }

        if (string.IsNullOrWhiteSpace(rendered.HtmlBody) &&
            string.IsNullOrWhiteSpace(rendered.TextBody))
        {
            throw new EmailTemplateException($"Template '{templateName}' rendered empty content.");
        }

        var message = new EmailMessage
        {
            Subject = subject,
            HtmlBody = rendered.HtmlBody,
            TextBody = rendered.TextBody
        };

        foreach (var r in to)
        {
            message.To.Add(r);
        }

        // or, if To is List<string>
        message.To.AddRange(to);

        await _email.SendAsync(message, options, ct);
    }
}
