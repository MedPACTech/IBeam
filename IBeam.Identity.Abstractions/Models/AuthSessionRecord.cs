namespace IBeam.Identity.Abstractions.Models;

public sealed record AuthSessionRecord(
    string RefreshTokenHash,
    string SessionId,
    Guid UserId,
    Guid TenantId,
    string ClaimsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset RefreshTokenExpiresAt,
    DateTimeOffset? RevokedAt = null,
    string? DeviceInfo = null);
