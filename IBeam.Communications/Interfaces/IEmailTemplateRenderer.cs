namespace IBeam.Communications.Abstractions;

public interface IEmailTemplateRenderer
{
    Task<RenderedEmailTemplate> RenderAsync(
        string templateName,
        object? model = null,
        CancellationToken ct = default);
}


