namespace IBeam.Identity.Abstractions.Options;

public sealed class TokenOptions
{
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string SigningKey { get; init; } = ""; // or KeyId + Key material abstraction later
    public int AccessTokenMinutes { get; init; } = 60;
    public int ClockSkewSeconds { get; init; } = 60;
}
