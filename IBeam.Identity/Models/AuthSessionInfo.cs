namespace IBeam.Identity.Models;

public sealed record AuthSessionInfo(
    string SessionId,
    Guid TenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset RefreshTokenExpiresAt,
    DateTimeOffset? RevokedAt,
    string? DeviceInfo);
