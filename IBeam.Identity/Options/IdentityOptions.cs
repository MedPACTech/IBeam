namespace IBeam.Identity.Options;

public sealed class IdentityOptions
{
    public const string SectionName = "IBeam:Identity";

    public JwtOptions Token { get; init; } = new();
    public OtpOptions Otp { get; init; } = new();
    public OAuthOptions OAuth { get; init; } = new();
    public FeatureOptions Features { get; init; } = new();
}
