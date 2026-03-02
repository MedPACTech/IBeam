namespace IBeam.Identity.Abstractions.Options;

public sealed class FeatureOptions
{
    public bool Otp { get; init; } = true;
    public bool PasswordAuth { get; init; } = true;
    public bool TwoFactor { get; init; } = true;
    public bool OAuth { get; init; } = false;
    public bool TenantSelection { get; init; } = true;
    public bool ClaimsEnrichment { get; init; } = true;
}
