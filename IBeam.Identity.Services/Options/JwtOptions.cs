//namespace IBeam.Identity.Services.Options;

//public sealed class JwtOptions
//{
//    // who issues the token (your auth service)
//    public string Issuer { get; init; } = string.Empty;

//    // who the token is intended for (your APIs/clients)
//    public string Audience { get; init; } = string.Empty;

//    // symmetric signing key for HMAC (store in user-secrets / Key Vault, not in git)
//    public string SigningKey { get; init; } = string.Empty;

//    // access token lifetime
//    public int AccessTokenMinutes { get; init; } = 60;

//    public int PreTenantTokenMinutes { get; init; } = 5;

//    // optional: let you rotate keys later without breaking running tokens
//    public string? KeyId { get; init; }

//    // optional: issuer/audience validation knobs (defaults are safest)
//    public bool ValidateIssuer { get; init; } = true;
//    public bool ValidateAudience { get; init; } = true;

//    // small clock skew to reduce “token not yet valid” edge cases
//    public int ClockSkewSeconds { get; init; } = 60;

//    public void Validate()
//    {
//        if (string.IsNullOrWhiteSpace(Issuer))
//            throw new InvalidOperationException("Jwt:Issuer is required.");

//        if (string.IsNullOrWhiteSpace(Audience))
//            throw new InvalidOperationException("Jwt:Audience is required.");

//        if (string.IsNullOrWhiteSpace(SigningKey))
//            throw new InvalidOperationException("Jwt:SigningKey is required.");

//        // HMAC key should be long enough. 32+ chars is a practical baseline.
//        if (SigningKey.Trim().Length < 32)
//            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");

//        if (AccessTokenMinutes <= 0)
//            throw new InvalidOperationException("Jwt:AccessTokenMinutes must be > 0.");

//        if (ClockSkewSeconds < 0)
//            throw new InvalidOperationException("Jwt:ClockSkewSeconds must be >= 0.");
            
//        if (PreTenantTokenMinutes <= 0)
//            throw new InvalidOperationException("Jwt:PreTenantTokenMinutes must be > 0.");
//    }
//}
