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

    // Sliding inactivity window: when set, each refresh extends the session by this many
    // minutes instead of RefreshTokenDays, so the session ends after that much inactivity.
    public int? SessionInactivityMinutes { get; init; }

    // Hard cap on total session age: sliding refreshes never extend a session beyond
    // CreatedAt + this many days. Unset means the window can slide indefinitely.
    public int? SessionAbsoluteLifetimeDays { get; init; }

    // "Remember this device" overrides, used only when a login explicitly opts in
    // (CreateAccessTokenAsync's rememberDevice parameter). Unset means a remembered session
    // gets the same lifetime as any other - these exist to let a remembered session outlive
    // RefreshTokenDays/SessionAbsoluteLifetimeDays without changing the default for everyone.
    public int? RememberedRefreshTokenDays { get; init; }
    public int? RememberedSessionAbsoluteLifetimeDays { get; init; }

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
        if (SessionInactivityMinutes is { } inactivity && inactivity < AccessTokenMinutes)
            throw new InvalidOperationException("JwtOptions.SessionInactivityMinutes must be >= AccessTokenMinutes; a shorter window cannot be enforced because issued access tokens stay valid for AccessTokenMinutes.");
        if (SessionAbsoluteLifetimeDays is <= 0)
            throw new InvalidOperationException("JwtOptions.SessionAbsoluteLifetimeDays must be > 0.");
        if (RememberedRefreshTokenDays is <= 0)
            throw new InvalidOperationException("JwtOptions.RememberedRefreshTokenDays must be > 0.");
        if (RememberedSessionAbsoluteLifetimeDays is <= 0)
            throw new InvalidOperationException("JwtOptions.RememberedSessionAbsoluteLifetimeDays must be > 0.");
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
