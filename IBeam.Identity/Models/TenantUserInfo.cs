namespace IBeam.Identity.Models;

public sealed record TenantUserInfo(
    Guid TenantId,
    Guid UserId,
    IReadOnlyList<string> Roles,
    bool IsActive,
    IReadOnlyList<Guid>? RoleIds = null,
    string? UserDisplayName = null,
    string? Email = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? DisabledAt = null,
    string? DisabledReason = null);
