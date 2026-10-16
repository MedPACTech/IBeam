namespace IBeam.Management.Identity.Services;

/// <summary>Only management catalog roles grant management operations; customer Owner/Admin names are ignored.</summary>
public static class ManagementIdentityPermissions
{
    public static IReadOnlyList<string> ForRoles(IEnumerable<string> roles)
        => roles.Contains(ManagementIdentityRoles.Administrator, StringComparer.Ordinal)
            ? [ManagementOperations.IdentityRead, ManagementApplicationOperations.Admin]
            : [ManagementOperations.IdentityRead];
}
