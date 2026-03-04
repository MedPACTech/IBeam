using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;

namespace IBeam.Identity.Services.Tokens;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IAuthSessionStore _sessions;
    private readonly JwtSecurityTokenHandler _handler = new();
    private readonly IAuthEventPublisher _eventPublisher;
    private readonly IAuthLifecycleHook _lifecycleHook;
    private readonly IOptions<AuthEventOptions> _eventOptions;
    private readonly ILogger<JwtTokenService> _logger;

    public JwtTokenService(
        IOptions<JwtOptions> options,
        IAuthSessionStore sessions,
        IAuthEventPublisher eventPublisher,
        IAuthLifecycleHook lifecycleHook,
        IOptions<AuthEventOptions> eventOptions,
        ILogger<JwtTokenService> logger)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _lifecycleHook = lifecycleHook ?? throw new ArgumentNullException(nameof(lifecycleHook));
        _eventOptions = eventOptions ?? throw new ArgumentNullException(nameof(eventOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Validate(_options);
    }

    public JwtTokenService(IOptions<JwtOptions> options, IAuthSessionStore sessions)
        : this(
            options,
            sessions,
            new NoOpAuthEventPublisher(),
            new NoOpAuthLifecycleHook(),
            Microsoft.Extensions.Options.Options.Create(new AuthEventOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JwtTokenService>.Instance)
    {
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

        var effective = NormalizeAccessClaims(userId, tenantId, claims, sessionId);

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

        var result = new TokenResult(jwt, expiresAt, effective, refreshToken, refreshExpiresAt, sessionId);
        await EmitTokenIssuedAsync("access", userId, tenantId, sessionId, expiresAt, ct);
        return result;
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

        var existingClaims = JsonSerializer.Deserialize<List<ClaimItem>>(existing.ClaimsJson) ?? new List<ClaimItem>();
        var claims = NormalizeAccessClaims(existing.UserId, existing.TenantId, existingClaims, existing.SessionId);

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

        var rotatedEvent = new RefreshTokenRotatedEvent
        {
            AuthUserId = existing.UserId.ToString("D"),
            TenantId = existing.TenantId,
            SessionId = existing.SessionId,
            RefreshTokenExpiresAtUtc = newRefreshExpiresAt,
            TraceId = ResolveTraceId()
        };
        rotatedEvent.Metadata["idempotencyKey"] =
            $"{RefreshTokenRotatedEvent.TypeName}:{existing.UserId:D}:{existing.SessionId}";
        await InvokeLifecycleAndPublishAsync(rotatedEvent, (hook, evt, token) => hook.OnRefreshTokenRotatedAsync(evt, token), ct);

        var result = new TokenResult(jwt, expiresAt, claims, newRefreshToken, newRefreshExpiresAt, existing.SessionId);
        await EmitTokenIssuedAsync("refresh-rotated", existing.UserId, existing.TenantId, existing.SessionId, expiresAt, ct);
        return result;
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

        return RevokeWithEventAsync(userId, sessionId.Trim(), ct);
    }

    public async Task<TokenResult> CreatePreTenantTokenAsync(
        Guid userId,
        IReadOnlyList<ClaimItem> claims,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new IdentityValidationException("userId is required.");

        var minutes = _options.PreTenantTokenMinutes > 0 ? _options.PreTenantTokenMinutes : _options.AccessTokenMinutes;

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(minutes);

        var effective = NormalizePreTenantClaims(userId, claims);

        var jwtClaims = CreateJwtClaimsFromClaimItems(effective);
        var jwt = SignJwt(jwtClaims, now, expiresAt);

        var result = new TokenResult(jwt, expiresAt, effective);
        await EmitTokenIssuedAsync("pretenant", userId, null, null, expiresAt, ct);
        return result;
    }

    private async Task<bool> RevokeWithEventAsync(Guid userId, string sessionId, CancellationToken ct)
    {
        var revoked = await _sessions.RevokeBySessionIdAsync(userId, sessionId, ct);
        if (!revoked)
            return false;

        var evt = new SessionRevokedEvent
        {
            AuthUserId = userId.ToString("D"),
            SessionId = sessionId,
            TraceId = ResolveTraceId()
        };
        evt.Metadata["idempotencyKey"] = $"{SessionRevokedEvent.TypeName}:{userId:D}:{sessionId}";
        await InvokeLifecycleAndPublishAsync(evt, (hook, e, token) => hook.OnSessionRevokedAsync(e, token), ct);
        return true;
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

    private static List<ClaimItem> NormalizeAccessClaims(
        Guid userId,
        Guid tenantId,
        IReadOnlyList<ClaimItem>? claims,
        string sessionId)
    {
        var effective = claims is null || claims.Count == 0
            ? new List<ClaimItem>(4)
            : new List<ClaimItem>(claims);

        EnsureSingleClaim(effective, "sub", userId.ToString("D"));
        EnsureSingleClaim(effective, "uid", userId.ToString("D"));
        EnsureSingleClaim(effective, "tid", tenantId.ToString("D"));
        EnsureSingleClaim(effective, "sid", sessionId);

        return effective;
    }

    private static List<ClaimItem> NormalizePreTenantClaims(
        Guid userId,
        IReadOnlyList<ClaimItem>? claims)
    {
        var effective = claims is null || claims.Count == 0
            ? new List<ClaimItem>(3)
            : new List<ClaimItem>(claims);

        EnsureSingleClaim(effective, "sub", userId.ToString("D"));
        EnsureSingleClaim(effective, "uid", userId.ToString("D"));
        EnsureSingleClaim(effective, "pt", "1");

        return effective;
    }

    private static void EnsureSingleClaim(List<ClaimItem> claims, string claimType, string claimValue)
    {
        claims.RemoveAll(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase));
        claims.Add(new ClaimItem(claimType, claimValue));
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

    private async Task EmitTokenIssuedAsync(
        string tokenKind,
        Guid userId,
        Guid? tenantId,
        string? sessionId,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct)
    {
        var evt = new TokenIssuedEvent
        {
            TokenKind = tokenKind,
            AuthUserId = userId.ToString("D"),
            TenantId = tenantId,
            SessionId = sessionId,
            ExpiresAtUtc = expiresAtUtc,
            TraceId = ResolveTraceId()
        };
        evt.Metadata["idempotencyKey"] =
            $"{TokenIssuedEvent.TypeName}:{tokenKind}:{tenantId?.ToString("D") ?? "none"}:{userId:D}:{sessionId ?? "none"}";

        await InvokeLifecycleAndPublishAsync(evt, (hook, e, token) => hook.OnTokenIssuedAsync(e, token), ct);
    }

    private async Task InvokeLifecycleAndPublishAsync<TEvent>(
        TEvent evt,
        Func<IAuthLifecycleHook, TEvent, CancellationToken, Task> hookInvoker,
        CancellationToken ct)
        where TEvent : AuthLifecycleEventBase
    {
        try
        {
            await hookInvoker(_lifecycleHook, evt, ct);
            await _eventPublisher.PublishAsync(evt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit auth lifecycle event {EventType}. EventId={EventId}", evt.EventType, evt.EventId);
            if (_eventOptions.Value.StrictPublishFailures)
                throw;
        }
    }

    private static string? ResolveTraceId()
    {
        var current = Activity.Current;
        if (current is null)
            return null;

        return current.TraceId != default
            ? current.TraceId.ToString()
            : current.Id;
    }
}
