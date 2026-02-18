namespace IBeam.Identity.Abstractions.Options;

public sealed class FeatureOptions
{
    public bool Otp { get; init; } = true;
    public bool TenantSelection { get; init; } = true;
    public bool ClaimsEnrichment { get; init; } = true;
}
