using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace IBeam.Communications.Abstractions;

public sealed class FileSystemEmailTemplateRenderer : IEmailTemplateRenderer
{
    private static readonly Regex TokenRegex = new(@"\{\{\s*(?<key>[A-Za-z0-9_.-]+)\s*\}\}", RegexOptions.Compiled);
    private readonly EmailTemplateOptions _options;

    public FileSystemEmailTemplateRenderer(IOptions<EmailTemplateOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<RenderedEmailTemplate> RenderAsync(string templateName, object? model = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            throw new EmailTemplateException("Template name is required.");
        if (string.IsNullOrWhiteSpace(_options.BasePath))
            throw new EmailTemplateException("Email template base path is not configured.");

        var basePath = ResolveBasePath(_options.BasePath!);
        var htmlPath = Path.Combine(basePath, $"{templateName}{_options.HtmlExtension ?? ".html"}");
        var textPath = Path.Combine(basePath, $"{templateName}{_options.TextExtension ?? ".txt"}");

        var htmlExists = File.Exists(htmlPath);
        var textExists = File.Exists(textPath);
        if (!htmlExists && !textExists)
            throw new EmailTemplateNotFoundException(templateName);

        string? html = null;
        string? text = null;
        if (htmlExists) html = await File.ReadAllTextAsync(htmlPath, ct);
        if (textExists) text = await File.ReadAllTextAsync(textPath, ct);

        var data = ToDictionary(model);
        return new RenderedEmailTemplate(
            HtmlBody: ReplaceTokens(html, data),
            TextBody: ReplaceTokens(text, data));
    }

    private static string ResolveBasePath(string basePath)
        => Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(Directory.GetCurrentDirectory(), basePath);

    private static Dictionary<string, object?> ToDictionary(object? model)
    {
        if (model is null)
            return new(StringComparer.OrdinalIgnoreCase);

        if (model is IDictionary<string, object?> typedDictionary)
            return new Dictionary<string, object?>(typedDictionary, StringComparer.OrdinalIgnoreCase);

        if (model is IDictionary<string, object> dictionary)
            return dictionary.ToDictionary(k => k.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            map[prop.Name] = prop.GetValue(model);

        return map;
    }

    private static string? ReplaceTokens(string? content, IReadOnlyDictionary<string, object?> data)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        return TokenRegex.Replace(content, m =>
        {
            var key = m.Groups["key"].Value;
            return data.TryGetValue(key, out var value)
                ? Convert.ToString(value) ?? string.Empty
                : string.Empty;
        });
    }
}
