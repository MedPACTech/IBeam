using IBeam.Management;
using IBeam.Services.Abstractions;

namespace IBeam.Management.Services;

public sealed class ManagementIdentityService(
    IManagementPrincipalProvider principals,
    IServiceOperationExecutor operations) : IManagementIdentityService
{
    // Management has its own subject/deployment guard. Generic tenant-role authorization
    // must not interpret a customer owner/admin as management access.
    [IBeamOperation(ManagementOperations.IdentityRead, Permission = false)]
    public Task<ManagementIdentity> GetCurrentAsync(CancellationToken ct = default)
        => operations.ExecuteAsync(this, token =>
        {
            token.ThrowIfCancellationRequested();
            var identity = ManagementPrincipalGuard.RequireIdentity(
                principals.GetPrincipal(), ManagementSubjectTypes.Human, ManagementOperations.IdentityRead);
            var userId = ManagementPrincipalGuard.RequireHumanUserId(identity);
            var organizationId = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.OrganizationId);
            return Task.FromResult(new ManagementIdentity(userId, organizationId));
        }, new ServiceOperationExecutionOptions
        {
            OperationName = ManagementOperations.IdentityRead,
            AuditAction = ManagementOperations.IdentityRead,
            EntityName = nameof(ManagementIdentity),
            PermissionEnabled = false,
            AuditEnabled = true
        }, ct);
}
