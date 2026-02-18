namespace IBeam.Identity.Abstractions.Options;

public sealed class TokenOptions
{
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string SigningKey { get; init; } = "";
    public int AccessTokenMinutes { get; init; } = 60;

    // NEW: used for the “tenant selection required” token (pt=1)
    public int PreTenantTokenMinutes { get; init; } = 10;

    public int ClockSkewSeconds { get; init; } = 60;
    public string? KeyId { get; init; }

}
