namespace IBeam.Communications.Email.Abstractions;

public interface IEmailTemplateRenderer
{
    Task<RenderedEmailTemplate> RenderAsync(string templateName, object? model, CancellationToken ct = default);
}

public sealed record RenderedEmailTemplate(string? Html, string? Text);
