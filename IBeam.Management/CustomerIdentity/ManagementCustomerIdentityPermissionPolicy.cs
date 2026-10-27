namespace IBeam.Management.CustomerIdentity;

/// <summary>Pure operation matrix. Authorization still requires live human/credential state and target-owned policy.</summary>
public static class ManagementCustomerIdentityPermissionPolicy
{
    public static bool IsAssignable(string operation) => operation != ManagementCustomerIdentityOperations.Info &&
        ManagementCustomerIdentityOperations.All.Contains(operation, StringComparer.Ordinal);
    public static IReadOnlyList<string> RequiredFor(string operation) => operation switch
    {
        ManagementCustomerIdentityOperations.OnboardingCreate => Array.AsReadOnly(new[] { operation,
            ManagementCustomerIdentityOperations.TenantsCreate, ManagementCustomerIdentityOperations.InvitesManage,
            ManagementCustomerIdentityOperations.RolesAssign }),
        ManagementCustomerIdentityOperations.OnboardingRetry => Array.AsReadOnly(new[] { operation,
            ManagementCustomerIdentityOperations.OnboardingCreate, ManagementCustomerIdentityOperations.TenantsCreate,
            ManagementCustomerIdentityOperations.InvitesManage, ManagementCustomerIdentityOperations.RolesAssign }),
        ManagementCustomerIdentityOperations.UsersProvision => Array.AsReadOnly(new[] { operation,
            ManagementCustomerIdentityOperations.InvitesManage, ManagementCustomerIdentityOperations.RolesAssign }),
        _ when IsAssignable(operation) => Array.AsReadOnly(new[] { operation }),
        _ => throw new ManagementAccessDeniedException()
    };
    public static string DelegationFor(string operation) => IsAssignable(operation)
        ? "management.application-access.delegate.identity." + operation["management.identity.".Length..]
        : throw new ManagementAccessDeniedException();
    public static string DelegationFor(string operation, Guid applicationId) => applicationId != Guid.Empty
        ? DelegationFor(operation) + ":" + applicationId.ToString("N") : throw new ManagementAccessDeniedException();
}
public interface IManagementCustomerIdentityAuthorizationService
{
    /// <summary>Checks the complete operation matrix against live app assignment and verified connection capability state.</summary>
    Task RequireAsync(Guid applicationId, string operation, CancellationToken ct = default);
}
