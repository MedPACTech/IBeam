using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private readonly IAuthSessionStore _sessions;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtTokenService(IOptions<JwtOptions> options, IAuthSessionStore sessions)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        Validate(_options);
    }

    public async Task<TokenResult> CreateAccessTokenAsync(
        Guid userId,
        Guid tenantId,
        IReadOnlyList<ClaimItem> claims,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new IdentityValidationException("userId is required.");
        if (tenantId == Guid.Empty) throw new IdentityValidationException("tenantId is required.");

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var sessionId = Guid.NewGuid().ToString("D");

        var effective = new List<ClaimItem>(capacity: (claims?.Count ?? 0) + 4)
        {
            new("sub", userId.ToString("D")),
            new("uid", userId.ToString("D")),
            new("tid", tenantId.ToString("D")),
            new("sid", sessionId),
        };

        if (claims is not null && claims.Count > 0)
            effective.AddRange(claims);

        var jwtClaims = CreateJwtClaimsFromClaimItems(effective);
        var jwt = SignJwt(jwtClaims, now, expiresAt);

        var refreshToken = CreateRefreshToken();
        var refreshHash = HashRefreshToken(refreshToken);
        var refreshExpiresAt = now.AddDays(_options.RefreshTokenDays);

        var session = new AuthSessionRecord(
            RefreshTokenHash: refreshHash,
            SessionId: sessionId,
            UserId: userId,
            TenantId: tenantId,
            ClaimsJson: JsonSerializer.Serialize(effective),
            CreatedAt: now,
            LastSeenAt: now,
            RefreshTokenExpiresAt: refreshExpiresAt,
            RevokedAt: null,
            DeviceInfo: null);

        await _sessions.SaveAsync(session, ct);

        return new TokenResult(jwt, expiresAt, effective, refreshToken, refreshExpiresAt, sessionId);
    }

    public async Task<TokenResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new IdentityValidationException("Refresh token is required.");

        var oldHash = HashRefreshToken(refreshToken);
        var existing = await _sessions.GetByRefreshTokenHashAsync(oldHash, ct);
        if (existing is null)
            throw new IdentityUnauthorizedException("Invalid refresh token.");

        var now = DateTimeOffset.UtcNow;
        if (existing.RevokedAt.HasValue || existing.RefreshTokenExpiresAt <= now)
            throw new IdentityUnauthorizedException("Refresh token is expired or revoked.");

        var claims = JsonSerializer.Deserialize<List<ClaimItem>>(existing.ClaimsJson) ?? new List<ClaimItem>();
        claims.RemoveAll(c => string.Equals(c.Type, "sid", StringComparison.OrdinalIgnoreCase));
        claims.Add(new ClaimItem("sid", existing.SessionId));

        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var jwtClaims = CreateJwtClaimsFromClaimItems(claims);
        var jwt = SignJwt(jwtClaims, now, expiresAt);

        var newRefreshToken = CreateRefreshToken();
        var newHash = HashRefreshToken(newRefreshToken);
        var newRefreshExpiresAt = now.AddDays(_options.RefreshTokenDays);

        var rotated = existing with
        {
            RefreshTokenHash = newHash,
            LastSeenAt = now,
            RefreshTokenExpiresAt = newRefreshExpiresAt,
            ClaimsJson = JsonSerializer.Serialize(claims)
        };

        await _sessions.DeleteByRefreshTokenHashAsync(oldHash, ct);
        await _sessions.SaveAsync(rotated, ct);

        return new TokenResult(jwt, expiresAt, claims, newRefreshToken, newRefreshExpiresAt, existing.SessionId);
    }

    public async Task<IReadOnlyList<AuthSessionInfo>> GetUserSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");

        var sessions = await _sessions.GetByUserAsync(userId, ct);
        return sessions
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new AuthSessionInfo(
                SessionId: s.SessionId,
                TenantId: s.TenantId,
                CreatedAt: s.CreatedAt,
                LastSeenAt: s.LastSeenAt,
                RefreshTokenExpiresAt: s.RefreshTokenExpiresAt,
                RevokedAt: s.RevokedAt,
                DeviceInfo: s.DeviceInfo))
            .ToList();
    }

    public Task<bool> RevokeSessionAsync(Guid userId, string sessionId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new IdentityValidationException("SessionId is required.");

        return _sessions.RevokeBySessionIdAsync(userId, sessionId.Trim(), ct);
    }

    public Task<TokenResult> CreatePreTenantTokenAsync(
        Guid userId,
        IReadOnlyList<ClaimItem> claims,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new IdentityValidationException("userId is required.");

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

    private static string CreateRefreshToken()
    {
        var bytes = new byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashRefreshToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
        if (o.PreTenantTokenMinutes <= 0)
            throw new IdentityValidationException("TokenOptions.PreTenantTokenMinutes must be > 0.");
        if (o.RefreshTokenDays <= 0)
            throw new IdentityValidationException("TokenOptions.RefreshTokenDays must be > 0.");
    }
}
