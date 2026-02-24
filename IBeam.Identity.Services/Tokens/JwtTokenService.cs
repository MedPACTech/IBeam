using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Abstractions.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Identity.Services.Tokens;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        Validate(_options);
    }

    public Task<TokenResult> CreateAccessTokenAsync(
        Guid userId,
        Guid tenantId,
        IReadOnlyList<ClaimItem> claims,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new IdentityValidationException("userId is required.");
        if (tenantId == Guid.Empty) throw new IdentityValidationException("tenantId is required.");

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        // Effective claim items (what we return on TokenResult)
        var effective = new List<ClaimItem>(capacity: (claims?.Count ?? 0) + 3)
        {
            new("sub", userId.ToString("D")),
            new("uid", userId.ToString("D")),
            new("tid", tenantId.ToString("D")),
        };

        if (claims is not null && claims.Count > 0)
            effective.AddRange(claims);

        // JWT claims (what we sign)
        var jwtClaims = CreateJwtClaimsFromClaimItems(effective);

        var jwt = SignJwt(jwtClaims, now, expiresAt);

        return Task.FromResult(new TokenResult(jwt, expiresAt, effective));
    }

    public Task<TokenResult> CreatePreTenantTokenAsync(
        Guid userId,
        IReadOnlyList<ClaimItem> claims,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new IdentityValidationException("userId is required.");

        // If you added PreTenantTokenMinutes to TokenOptions, use it.
        // Otherwise, you can temporarily reuse AccessTokenMinutes.
        var minutes = _options.PreTenantTokenMinutes > 0 ? _options.PreTenantTokenMinutes : _options.AccessTokenMinutes;

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(minutes);

        var effective = new List<ClaimItem>(capacity: (claims?.Count ?? 0) + 3)
        {
            new("sub", userId.ToString("D")),
            new("uid", userId.ToString("D")),
            new("pt", "1"),
        };

        if (claims is not null && claims.Count > 0)
            effective.AddRange(claims);

        var jwtClaims = CreateJwtClaimsFromClaimItems(effective);

        var jwt = SignJwt(jwtClaims, now, expiresAt);

        return Task.FromResult(new TokenResult(jwt, expiresAt, effective));
    }

    private static List<Claim> CreateJwtClaimsFromClaimItems(IReadOnlyList<ClaimItem> items)
    {
        var list = new List<Claim>(items.Count);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Type)) continue;
            if (item.Value is null) continue;

            list.Add(new Claim(item.Type, item.Value));
        }

        return list;
    }

    private string SignJwt(List<Claim> claims, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        var signingKey = new SymmetricSecurityKey(keyBytes);

        // You said you added KeyId for future use ✅
        if (!string.IsNullOrWhiteSpace(_options.KeyId))
            signingKey.KeyId = _options.KeyId;

        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);

        return _handler.WriteToken(token);
    }

    private static void Validate(JwtOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.Issuer))
            throw new IdentityValidationException("TokenOptions.Issuer is required.");
        if (string.IsNullOrWhiteSpace(o.Audience))
            throw new IdentityValidationException("TokenOptions.Audience is required.");
        if (string.IsNullOrWhiteSpace(o.SigningKey))
            throw new IdentityValidationException("TokenOptions.SigningKey is required.");
        if (o.AccessTokenMinutes <= 0)
            throw new IdentityValidationException("TokenOptions.AccessTokenMinutes must be > 0.");

        // Only validate this if you added it; otherwise remove this check.
        if (o.PreTenantTokenMinutes <= 0)
            throw new IdentityValidationException("TokenOptions.PreTenantTokenMinutes must be > 0.");
    }
}
