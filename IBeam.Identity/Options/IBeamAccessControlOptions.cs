using IBeam.Identity.Models;

namespace IBeam.Identity.Options;

public sealed class IBeamAccessControlOptions
{
    public const string SectionName = "IBeam:Identity:AccessControl";

    public List<AccessModuleDefinition> Modules { get; set; } = [];
    public AccessResourceDefinitionCollection Resources { get; } = new();
    public List<AccessLevelDefinition> AccessLevels { get; set; } =
    [
        new(IBeam.Identity.Models.AccessLevels.View, 0, "View"),
        new(IBeam.Identity.Models.AccessLevels.Edit, 10, "Edit"),
        new(IBeam.Identity.Models.AccessLevels.Manage, 20, "Manage")
    ];

    public List<string> OwnerRoleNames { get; set; } = ["Owner"];
    public List<string> AdminRoleNames { get; set; } = ["Administrator", "Admin"];
    public List<string> ApplicationRoleNames { get; set; } = ["Application"];
    public bool OwnerHasUnrestrictedTenantAccess { get; set; } = true;
    public bool AdminHasUnrestrictedAccessExceptOwnerActions { get; set; } = true;
    public bool ApplicationRoleRequiresExplicitGrants { get; set; } = true;
    public AccessCatalogProviderTypeCollection ResourceCatalogProviders { get; } = new();
}

public sealed class AccessCatalogProviderTypeCollection : List<Type>
{
    public void Add<TProvider>()
        where TProvider : class
        => Add(typeof(TProvider));
}

public sealed class AccessResourceDefinition
{
    public Type? ClrType { get; set; }
    public string ResourceKey { get; set; } = string.Empty;
    public string PermissionPrefix { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? Module { get; set; }
    public List<string> DefaultAccessLevels { get; set; } = [AccessLevels.View, AccessLevels.Edit, AccessLevels.Manage];
    public string? IdPropertyName { get; set; }
    public string? ParentResourceType { get; set; }
    public string? ParentIdPropertyName { get; set; }
}

public sealed class AccessResourceDefinitionCollection : List<AccessResourceDefinition>
{
    public AccessResourceDefinitionCollection Add<TResource>(
        string resourceKey,
        string permissionPrefix,
        string? label = null,
        string? module = null,
        IReadOnlyList<string>? defaultAccessLevels = null,
        string? idPropertyName = null,
        string? parentResourceType = null,
        string? parentIdPropertyName = null)
    {
        Add(new AccessResourceDefinition
        {
            ClrType = typeof(TResource),
            ResourceKey = resourceKey,
            PermissionPrefix = permissionPrefix,
            Label = label,
            Module = module,
            DefaultAccessLevels = defaultAccessLevels?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList()
                ?? [AccessLevels.View, AccessLevels.Edit, AccessLevels.Manage],
            IdPropertyName = idPropertyName,
            ParentResourceType = parentResourceType,
            ParentIdPropertyName = parentIdPropertyName
        });

        return this;
    }
}
