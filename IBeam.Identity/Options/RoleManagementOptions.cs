namespace IBeam.Identity.Options;

public enum PermissionManagementMode
{
    HardCoded = 0,
    Repository = 1,
    Configuration = 2,
    Hybrid = 3
}

public sealed class RoleManagementOptions
{
    public const string SectionName = "IBeam:Identity:RoleManagement";

    public PermissionManagementMode PermissionMode { get; set; } = PermissionManagementMode.Hybrid;
    public bool AllowTenantRoleCreation { get; set; } = true;
    public bool AllowTenantRoleMutation { get; set; } = true;
    public bool AllowTenantPermissionMapMutation { get; set; } = true;
}
