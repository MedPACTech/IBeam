namespace IBeam.Identity.Models;

public sealed record ExposedPermission(
    string? PermissionName,
    Guid? PermissionId,
    string Source,
    string Resource,
    string? Description = null);
