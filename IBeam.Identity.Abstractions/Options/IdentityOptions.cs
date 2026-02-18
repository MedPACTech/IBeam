namespace IBeam.Identity.Abstractions.Options;

public sealed class IdentityOptions
{
    public const string SectionName = "IBeam:Identity";

    public TokenOptions Token { get; init; } = new();
    public OtpOptions Otp { get; init; } = new();
    public FeatureOptions Features { get; init; } = new();
}
