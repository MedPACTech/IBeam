using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity.Delegation;
namespace IBeam.Management.Services.CustomerIdentity;
public sealed class ManagementPortalCustomerMembershipService(PortalMutationRuntime runtime, PortalMutationReferences references,
    IManagementCustomerIdentityClient client) : IManagementPortalCustomerMembershipService
{
    public Task<ManagementPortalCustomerMembershipResult> SetEnabledAsync(Guid app, Guid tenant, Guid user, ChangeManagementCustomerMembership request, string version, string key, CancellationToken ct = default)
        => Change(app, tenant, user, request, null, version, key, ct);
    public Task<ManagementPortalCustomerMembershipResult> SetRolesAsync(Guid app, Guid tenant, Guid user, ChangeManagementCustomerRoles request, string version, string key, CancellationToken ct = default)
        => Change(app, tenant, user, null, request, version, key, ct);
    private Task<ManagementPortalCustomerMembershipResult> Change(Guid app, Guid tenant, Guid user, ChangeManagementCustomerMembership? status,
        ChangeManagementCustomerRoles? assignment, string version, string key, CancellationToken ct)
    {
        var authority = assignment is null ? ManagementCustomerIdentityOperations.MembershipsManage : ManagementCustomerIdentityOperations.RolesAssign;
        var action = "management.portal.customer-identity." + (assignment is null ? "memberships.manage" : "roles.assign");
        return runtime.Run(this, app, action, async token => {
            await runtime.Authorize(app, authority, token);
            if (tenant == Guid.Empty || user == Guid.Empty || status is null && assignment is null || !ManagementDelegationBinding.ValidIdempotencyKey(key) ||
                string.IsNullOrWhiteSpace(version) || version == "*" || version.Length > 256 || version.Any(char.IsControl)) throw new ManagementCustomerIdentityInvalidRequestException();
            if (assignment is not null && (assignment.RoleIds is null || assignment.RoleIds.Count > 8 || assignment.RoleIds.Any(x => x == Guid.Empty) ||
                assignment.RoleIds.Distinct().Count() != assignment.RoleIds.Count || assignment.PolicyKey is not { Length: > 0 and <= 64 } ||
                assignment.PolicyKey.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_')))) throw new ManagementCustomerIdentityInvalidRequestException();
            var connection = await runtime.Resolve(app, authority, token); var state = references.Create(new(authority, tenant, user), key);
            ManagementCustomerMembershipResult? result = null;
            using (var attempt = runtime.Attempt(token)) {
                try { result = assignment is null ? await client.SetMembershipAsync(connection, tenant, user, status!, version, key, attempt.Token) :
                    await client.SetCustomerRolesAsync(connection, tenant, user, assignment, version, key, attempt.Token); }
                catch (Exception ex) when (PortalMutationRuntime.Uncertain(ex, token)) { }
            }
            await runtime.Recheck(connection, authority, token);
            var tracking = references.Result(connection, state, result?.Operation);
            await runtime.Outcome(this, app, action + "." + tracking.Status, tenant, token);
            return new ManagementPortalCustomerMembershipResult(tracking, result?.Membership);
        }, ct);
    }
}
