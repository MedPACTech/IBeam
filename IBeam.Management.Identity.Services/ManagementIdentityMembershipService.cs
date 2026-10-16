using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Identity.Services;

public sealed record ManagementLiveMember(IdentityUser User, TenantInfo Membership);

public sealed class ManagementIdentityMembershipService(
    IOptions<ManagementIdentityOptions> options,
    IIdentityUserStore users,
    IIdentityTenantStore organizations,
    ITenantMembershipStore memberships,
    ITenantRoleStore roles)
{
    public async Task<ManagementLiveMember> RequireAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty || organizationId != options.Value.OrganizationId)
            throw new ManagementAccessDeniedException();
        var user = await users.FindByIdAsync(userId, ct);
        var organization = await organizations.FindByIdAsync(organizationId, ct);
        var membership = await memberships.GetTenantForUserAsync(userId, organizationId, ct);
        if (user is null || !user.TwoFactorEnabled || organization is null || !organization.IsActive ||
            membership is null || !membership.IsActive)
            throw new ManagementAccessDeniedException();
        var assigned = (await roles.GetRolesForUserAsync(organizationId, userId, ct))
            .Where(x => x.TenantId == organizationId && x.IsActive).ToArray();
        if (!assigned.Any(x => x.Name is ManagementIdentityRoles.Administrator or ManagementIdentityRoles.Support))
            throw new ManagementAccessDeniedException();
        return new ManagementLiveMember(user, membership with
        {
            Roles = assigned.Select(x => x.Name).ToArray(),
            RoleIds = assigned.Select(x => x.RoleId).ToArray()
        });
    }

    public async Task<ManagementLiveMember> RequireByEmailAsync(string email, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(email, ct) ?? throw new ManagementAccessDeniedException();
        return await RequireAsync(user.UserId, options.Value.OrganizationId, ct);
    }
}
