namespace IBeam.Identity.Models;

public sealed record TenantRole(
    Guid TenantId,
    Guid RoleId,
    string Name,
    bool IsSystem,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null
);

public sealed record UserTenantRoleAssignment(
    Guid TenantId,
    Guid UserId,
    IReadOnlyList<TenantRole> Roles
);
