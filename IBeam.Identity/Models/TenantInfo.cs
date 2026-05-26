namespace IBeam.Identity.Models;

public sealed record TenantInfo(
    Guid TenantId,
    string Name,
    IReadOnlyList<string> Roles,
    bool IsActive,
    IReadOnlyList<Guid>? RoleIds = null
);

