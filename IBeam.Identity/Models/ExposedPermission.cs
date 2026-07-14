namespace IBeam.Identity.Models;

public sealed record ExposedPermission(
    string? PermissionName,
    Guid? PermissionId,
    string Source,
    string Resource,
    string? Description = null,
    string? Label = null,
    string? Category = null,
    bool IsAssignable = true,
    string? ModuleKey = null,
    string? ResourceType = null,
    string? ResourceId = null,
    string? AccessLevel = null);
