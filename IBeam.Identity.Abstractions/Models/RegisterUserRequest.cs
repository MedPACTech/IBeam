namespace IBeam.Identity.Abstractions.Models;

public sealed record RegisterUserRequest(
    string Email,
    string Password,
    string? DisplayName = null);

public sealed record LoginRequest(
    string Email,
    string Password,
    Guid? TenantId = null);

public sealed record TokenRequest(
    Guid UserId,
    Guid TenantId,
    IReadOnlyList<ClaimItem>? AdditionalClaims = null);

public sealed record TokenResult(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<ClaimItem> Claims);
