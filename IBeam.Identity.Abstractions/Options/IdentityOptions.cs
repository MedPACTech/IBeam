namespace IBeam.Identity.Abstractions.Options;

public sealed class IdentityOptions
{
    public const string SectionName = "IBeam:Identity";

    public TokenOptions Token { get; init; } = new();
    public OtpOptions Otp { get; init; } = new();
    public FeatureOptions Features { get; init; } = new();
}

public sealed class TokenOptions
{
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string SigningKey { get; init; } = ""; // or KeyId + Key material abstraction later
    public int AccessTokenMinutes { get; init; } = 60;
    public int ClockSkewSeconds { get; init; } = 60;
}

public sealed class OtpOptions
{
    public int CodeLength { get; init; } = 6;
    public int ExpirationMinutes { get; init; } = 5;
    public int MaxAttempts { get; init; } = 5;
}

public sealed class FeatureOptions
{
    public bool Otp { get; init; } = true;
    public bool TenantSelection { get; init; } = true;
    public bool ClaimsEnrichment { get; init; } = true;
}
