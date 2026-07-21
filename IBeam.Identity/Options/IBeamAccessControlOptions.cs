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
    public List<string> TenantManagementPermissionNames { get; set; } = ["identity.tenants.manage"];
    public List<string> TenantUserManagementPermissionNames { get; set; } =
    [
        "identity.tenantusers.manage",
        "identity.tenantinvites.manage",
        "identity.tenantinvites.create",
        "identity.tenantinvites.list",
        "identity.tenantinvites.get",
        "identity.tenantinvites.resend",
        "identity.tenantinvites.revoke",
        "identity.tenantroles.bootstrap",
        "identity.tenantroles.user.list"
    ];
    public List<string> TenantRoleManagementPermissionNames { get; set; } =
    [
        "identity.tenantroles.manage",
        "identity.tenantroles.list",
        "identity.tenantroles.get",
        "identity.tenantroles.create",
        "identity.tenantroles.update",
        "identity.tenantroles.delete",
        "identity.tenantroles.grant",
        "identity.tenantroles.revoke",
        "identity.tenantroles.user.list"
    ];
    public List<string> TenantAccessControlManagementPermissionNames { get; set; } =
    [
        "identity.accesscontrol.manage",
        "accesscontrol.resourceaccess.manage",
        "accesscontrol.resourceaccess.list",
        "accesscontrol.resourceaccess.grant",
        "accesscontrol.resourceaccess.update",
        "accesscontrol.resourceaccess.revoke",
        "accesscontrol.permissionroles.manage",
        "accesscontrol.permissionroles.list",
        "accesscontrol.permissionroles.upsert.name",
        "accesscontrol.permissionroles.upsert.id",
        "accesscontrol.permissionroles.delete.name",
        "accesscontrol.permissionroles.delete.id",
        "accesscontrol.serviceoperations.manage",
        "accesscontrol.serviceoperations.list",
        "accesscontrol.serviceoperations.upsert",
        "accesscontrol.serviceoperations.disable",
        "accesscontrol.serviceoperations.delete"
    ];
    public List<string> ApiCredentialManagementPermissionNames { get; set; } =
    [
        "identity.apicredentials.manage",
        "identity.apicredentials.create",
        "identity.apicredentials.list",
        "identity.apicredentials.get",
        "identity.apicredentials.update",
        "identity.apicredentials.roles.update",
        "identity.apicredentials.access.get",
        "identity.apicredentials.access.update",
        "identity.apicredentials.rotate",
        "identity.apicredentials.revoke",
        "identity.apicredentials.activate"
    ];
    public List<string> AuthAttemptManagementRoleNames { get; set; } = ["PlatformAdmin", "platform-admin", "Support"];
    public List<string> AuthAttemptManagementPermissionNames { get; set; } = ["identity:auth-attempts:unlock", "identity.authattempts.unlock"];
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
