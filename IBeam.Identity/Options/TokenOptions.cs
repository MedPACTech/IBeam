namespace IBeam.Identity.Options;

public sealed class JwtOptions
{
    public const string SectionName = "IBeam:Identity:Jwt";
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string SigningKey { get; init; } = "";
    public int AccessTokenMinutes { get; init; } = 60;
    public int PreTenantTokenMinutes { get; init; } = 10;
    public int RefreshTokenDays { get; init; } = 30;

    public int ClockSkewSeconds { get; init; } = 60;
    public string? KeyId { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
            throw new InvalidOperationException("JwtOptions.Issuer is required.");
        if (string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("JwtOptions.Audience is required.");
        if (string.IsNullOrWhiteSpace(SigningKey))
            throw new InvalidOperationException("JwtOptions.SigningKey is required.");
        if (AccessTokenMinutes <= 0)
            throw new InvalidOperationException("JwtOptions.AccessTokenMinutes must be > 0.");
        if (PreTenantTokenMinutes <= 0)
            throw new InvalidOperationException("JwtOptions.PreTenantTokenMinutes must be > 0.");
        if (RefreshTokenDays <= 0)
            throw new InvalidOperationException("JwtOptions.RefreshTokenDays must be > 0.");
    }
}
