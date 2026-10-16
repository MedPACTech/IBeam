using System.Security.Claims;
using IBeam.Identity.Interfaces;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Identity.Services;

public sealed class ManagementSupportAccessService(
    ManagementIdentitySessionService sessions,
    ITenantMembershipStore memberships,
    IAuthSessionStore sessionStore,
    ITenantRoleStore roles,
    IOptions<ManagementIdentityOptions> options,
    IServiceOperationExecutor operations)
{
    [IBeamOperation("management.support-users.disable", Permission = false)]
    public Task DisableAsync(ClaimsPrincipal actor, Guid userId, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            var administrator = await sessions.ValidateAccessAsync(actor, token);
            if (!administrator.Membership.Roles.Contains(ManagementIdentityRoles.Administrator, StringComparer.Ordinal) ||
                userId == administrator.User.UserId)
                throw new ManagementAccessDeniedException();
            if (!actor.HasClaim(ManagementClaimTypes.Permission, ManagementSupportOperations.UsersAdmin))
                throw new ManagementAccessDeniedException();
            var organizationId = options.Value.OrganizationId;
            var targetRoles = await roles.GetRolesForUserAsync(organizationId, userId, token);
            // Routine support administration cannot disable any management administrator, avoiding concurrent last-admin loss.
            if (targetRoles.Any(x => x.IsActive && x.Name == ManagementIdentityRoles.Administrator))
                throw new ManagementAccessDeniedException();
            var target = await memberships.GetTenantForUserAsync(userId, organizationId, token);
            if (target is null) throw new ManagementAccessDeniedException();
            await memberships.DisableTenantMembershipAsync(organizationId, userId, "Management support access disabled.", token);
            foreach (var session in await sessionStore.GetByUserAsync(userId, token))
                if (session.TenantId == organizationId)
                    await sessionStore.RevokeBySessionIdAsync(userId, session.SessionId, token);
        }, new ServiceOperationExecutionOptions
        {
            OperationName = "management.support-users.disable",
            AuditAction = "management.support-users.disable",
            EntityId = userId,
            TenantId = options.Value.OrganizationId,
            PermissionEnabled = false,
            AuditEnabled = true
        }, ct);
}
