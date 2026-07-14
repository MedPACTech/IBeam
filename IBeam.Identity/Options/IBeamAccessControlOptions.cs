using IBeam.Identity.Models;

namespace IBeam.Identity.Options;

public sealed class IBeamAccessControlOptions
{
    public const string SectionName = "IBeam:Identity:AccessControl";

    public List<AccessModuleDefinition> Modules { get; set; } = [];
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
