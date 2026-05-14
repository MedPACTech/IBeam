using IBeam.Identity.Options;

namespace IBeam.Identity.Services.Authorization;

public sealed class PermissionCatalogBuilder
{
    private readonly List<PermissionCatalogEntry> _entries = [];

    public PermissionCatalogBuilder AddPermission(
        string permissionName,
        string? resource = null,
        string? description = null)
    {
        _entries.Add(new PermissionCatalogEntry
        {
            PermissionName = permissionName,
            Resource = resource,
            Description = description
        });
        return this;
    }

    public PermissionCatalogBuilder AddPermission(
        Guid permissionId,
        string? resource = null,
        string? description = null)
    {
        _entries.Add(new PermissionCatalogEntry
        {
            PermissionId = permissionId,
            Resource = resource,
            Description = description
        });
        return this;
    }

    public IReadOnlyList<PermissionCatalogEntry> Build()
        => _entries.ToList();
}
