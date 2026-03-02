using IBeam.Identity.Abstractions.Models;

namespace IBeam.Identity.Abstractions.Options;

public sealed class IdentityEmailTemplateOptions
{
    public const string SectionName = "IBeam:Identity:EmailTemplates";

    public bool Enabled { get; init; } = false;
    public bool UseTemplatesForAllEmail { get; init; } = false;
    public bool FallbackToPlainIfMissingTemplate { get; init; } = true;
    public ExpirationDisplayMode ExpirationDisplay { get; init; } = ExpirationDisplayMode.UtcTimestamp;
    public Dictionary<string, IdentityEmailTemplateDefinition> PurposeTemplates { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetTemplate(SenderPurpose? purpose, out IdentityEmailTemplateDefinition template)
    {
        template = new IdentityEmailTemplateDefinition();
        if (!purpose.HasValue) return false;

        return PurposeTemplates.TryGetValue(purpose.Value.ToString(), out template!);
    }
}

public enum ExpirationDisplayMode
{
    UtcTimestamp = 0,
    MinutesRemaining = 1
}

public sealed class IdentityEmailTemplateDefinition
{
    public string TemplateName { get; init; } = string.Empty;
    public string? Subject { get; init; }
}
