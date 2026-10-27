using IBeam.Management.Operations;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Identity.Services;

/// <summary>Only management catalog roles grant management operations; customer Owner/Admin names are ignored.</summary>
public static class ManagementIdentityPermissions
{
    public static IReadOnlyList<string> CustomerDelegationsFor(Guid userId, IEnumerable<string> roles, ManagementIdentityOptions options)
        => CustomerDelegationsFor(userId, roles, options, ManagementOperationRegistry.BuiltIn);
    public static IReadOnlyList<string> CustomerDelegationsFor(Guid userId, IEnumerable<string> roles, ManagementIdentityOptions options, ManagementOperationRegistry registry)
        => roles.Contains(ManagementIdentityRoles.Administrator, StringComparer.Ordinal)
            ? options.CustomerDelegations.Where(x => x.UserId == userId).SelectMany(x => x.Operations.Select(operation =>
                registry.DelegationFor(operation, x.ApplicationId))).Distinct(StringComparer.Ordinal).ToArray()
            : [];
    public static bool ValidCustomerDelegations(ManagementIdentityOptions options) => ValidCustomerDelegations(options, ManagementOperationRegistry.BuiltIn);
    public static bool ValidCustomerDelegations(ManagementIdentityOptions options, ManagementOperationRegistry registry) => options.CustomerDelegations is { Count: <= 1000 } &&
        options.CustomerDelegations.All(x => x is not null && x.UserId != Guid.Empty && x.ApplicationId != Guid.Empty &&
            x.Operations is { Length: > 0 } && x.Operations.Length <= registry.Operations.Count && x.Operations.All(registry.IsAssignable) &&
            x.Operations.Distinct(StringComparer.Ordinal).Count() == x.Operations.Length) &&
        options.CustomerDelegations.Select(x => (x.UserId, x.ApplicationId)).Distinct().Count() == options.CustomerDelegations.Count;
    public static IReadOnlyList<string> ForRoles(IEnumerable<string> roles)
        => roles.Contains(ManagementIdentityRoles.Administrator, StringComparer.Ordinal)
            ? [ManagementOperations.IdentityRead, ManagementApplicationOperations.Admin, ManagementSupportOperations.UsersAdmin, ManagementSupportOperations.AccessAdmin, ManagementAccessDelegation.Errors, ManagementAccessDelegation.Logs]
            : [ManagementOperations.IdentityRead];
}
