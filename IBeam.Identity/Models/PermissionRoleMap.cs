namespace IBeam.Identity.Models;

public sealed record PermissionRoleMap(
    Guid TenantId,
    string? PermissionName,
    Guid? PermissionId,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<Guid> RoleIds,
    bool IsActive,
    DateTimeOffset UpdatedAt);
