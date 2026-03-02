namespace IBeam.Identity.Abstractions.Models;

public sealed record TokenResult(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<ClaimItem> Claims,
    string? RefreshToken = null,
    DateTimeOffset? RefreshTokenExpiresAt = null,
    string? SessionId = null);
