namespace IBeam.Identity.Options;

public sealed class PermissionAccessOptions
{
    public const string SectionName = "IBeam:Identity:PermissionAccess";

    public List<PermissionAccessMapEntry> Mappings { get; set; } = [];
    public List<PermissionCatalogEntry> Catalog { get; set; } = [];
}

public sealed class PermissionAccessMapEntry
{
    public Guid? TenantId { get; set; }
    public string? PermissionName { get; set; }
    public Guid? PermissionId { get; set; }
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
}

public sealed class PermissionCatalogEntry
{
    public string? PermissionName { get; set; }
    public Guid? PermissionId { get; set; }
    public string? Label { get; set; }
    public string? Resource { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsAssignable { get; set; } = true;
    public string? ModuleKey { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? AccessLevel { get; set; }
}
