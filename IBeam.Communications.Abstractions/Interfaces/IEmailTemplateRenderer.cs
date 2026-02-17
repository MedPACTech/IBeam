namespace IBeam.Communications.Abstractions;

public interface IEmailTemplateRenderer
{
    Task<RenderedEmailTemplate> RenderAsync(string templateName, object? model, CancellationToken ct = default);
}

