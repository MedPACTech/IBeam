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
        if (string.IsNullOrWhiteSpace(_options.TemplateDirectoryName))
            throw new ArgumentException("TemplateDirectoryName is required.", nameof(options));
    }

    public async Task<RenderedEmailTemplate> RenderAsync(string templateName, object? model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            throw new ArgumentException("Template name is required.", nameof(templateName));

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var templatePath = Path.Combine(baseDir, _options.TemplateDirectoryName, templateName);

        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Email template not found: {templateName}", templatePath);

        var html = await File.ReadAllTextAsync(templatePath, Encoding.UTF8, ct);

        // Backwards-compatible formatting:
        // - if model is object[] => replace {0},{1},...
        // - else if model is not null => replace {0} with model.ToString()
        if (model is object[] args)
        {
            for (var i = 0; i < args.Length; i++)
                html = html.Replace("{" + i + "}", args[i]?.ToString());
        }
        else if (model is not null)
        {
            html = html.Replace("{0}", model.ToString());
        }

        return new RenderedEmailTemplate(Html: html, Text: null);
    }
}
