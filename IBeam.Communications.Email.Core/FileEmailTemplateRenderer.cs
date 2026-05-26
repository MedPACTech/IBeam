using IBeam.Communications.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace IBeam.Communications.Email.Templating;

public sealed class FileEmailTemplateRenderer : IEmailTemplateRenderer
{
    private readonly EmailTemplateOptions _options;

    public FileEmailTemplateRenderer(IOptions<EmailTemplateOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<RenderedEmailTemplate> RenderAsync(
        string templateName,
        object? model = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            throw new EmailValidationException("Template name is required.");

        if (string.IsNullOrWhiteSpace(_options.BasePath))
            throw new EmailTemplateException("EmailTemplateOptions.BasePath is not configured.");

        // Normalize and prevent path traversal
        if (templateName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            templateName.Contains("..", StringComparison.Ordinal))
        {
            throw new EmailValidationException("Invalid template name.");
        }

        var basePath = Path.GetFullPath(_options.BasePath);

        var htmlPath = Path.Combine(basePath, templateName + _options.HtmlExtension);
        var textPath = Path.Combine(basePath, templateName + _options.TextExtension);

        string? html = null;
        string? text = null;

        try
        {
            if (File.Exists(htmlPath))
                html = await File.ReadAllTextAsync(htmlPath, Encoding.UTF8, ct);

            if (File.Exists(textPath))
                text = await File.ReadAllTextAsync(textPath, Encoding.UTF8, ct);
        }
        catch (Exception ex)
        {
            throw new EmailTemplateException($"Failed reading template '{templateName}' from files.", ex);
        }

        if (html is null && text is null)
            throw new EmailTemplateNotFoundException(templateName);

        html = ApplySimpleFormatting(html, model);
        text = ApplySimpleFormatting(text, model);

        if (string.IsNullOrWhiteSpace(html) && string.IsNullOrWhiteSpace(text))
            throw new EmailTemplateException($"Template '{templateName}' rendered empty content.");

        return new RenderedEmailTemplate(
            html,
            text
        );
    }

    private static string? ApplySimpleFormatting(string? input, object? model)
    {
        if (string.IsNullOrEmpty(input) || model is null)
            return input;

        // Minimal compatibility formatting:
        // - object[] => {0},{1},...
        // - otherwise => {0} only
        if (model is object[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                input = input.Replace("{" + i + "}", args[i]?.ToString(), StringComparison.Ordinal);
            }

            return input;
        }

        return input.Replace("{0}", model.ToString(), StringComparison.Ordinal);
    }
}
