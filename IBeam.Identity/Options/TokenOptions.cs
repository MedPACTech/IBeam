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
    public string SigningMode { get; init; } = JwtSigningModes.Symmetric;
    public string? PrivateKeyPem { get; init; }
    public List<JwtPreviousSigningKeyOptions> PreviousSigningKeys { get; init; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
            throw new InvalidOperationException("JwtOptions.Issuer is required.");
        if (string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("JwtOptions.Audience is required.");
        if (SigningMode == JwtSigningModes.Symmetric && string.IsNullOrWhiteSpace(SigningKey))
            throw new InvalidOperationException("JwtOptions.SigningKey is required.");
        if (SigningMode == JwtSigningModes.Asymmetric && string.IsNullOrWhiteSpace(PrivateKeyPem))
            throw new InvalidOperationException("JwtOptions.PrivateKeyPem is required for asymmetric signing.");
        if (SigningMode == JwtSigningModes.Asymmetric && string.IsNullOrWhiteSpace(KeyId))
            throw new InvalidOperationException("JwtOptions.KeyId is required for asymmetric signing.");
        if (SigningMode is not JwtSigningModes.Symmetric and not JwtSigningModes.Asymmetric)
            throw new InvalidOperationException("JwtOptions.SigningMode must be 'symmetric' or 'asymmetric'.");
        if (AccessTokenMinutes <= 0)
            throw new InvalidOperationException("JwtOptions.AccessTokenMinutes must be > 0.");
        if (PreTenantTokenMinutes <= 0)
            throw new InvalidOperationException("JwtOptions.PreTenantTokenMinutes must be > 0.");
        if (RefreshTokenDays <= 0)
            throw new InvalidOperationException("JwtOptions.RefreshTokenDays must be > 0.");
    }
}

public static class JwtSigningModes
{
    public const string Symmetric = "symmetric";
    public const string Asymmetric = "asymmetric";
}

public sealed class JwtPreviousSigningKeyOptions
{
    public string KeyId { get; init; } = string.Empty;
    public string PublicKeyPem { get; init; } = string.Empty;
    public DateTimeOffset PublishUntilUtc { get; init; }
}
