namespace IBeam.Identity.Abstractions.Options;

public sealed class JwtOptions
{
    public const string SectionName = "IBeam:Identity:Jwt";
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string SigningKey { get; init; } = "";
    public int AccessTokenMinutes { get; init; } = 60;

    // used for the “tenant selection required” token (pt=1)
    public int PreTenantTokenMinutes { get; init; } = 10;

    public int ClockSkewSeconds { get; init; } = 60;
    public string? KeyId { get; init; }

    public void Validate()
    {
        //TODO: add validation logic (e.g. Issuer not empty, Audience not empty, SigningKey length, etc.)
        //throw new NotImplementedException();
    }
}
