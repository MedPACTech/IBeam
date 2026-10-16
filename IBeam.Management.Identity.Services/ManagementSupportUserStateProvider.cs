using IBeam.Identity.Interfaces;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Services;

public sealed class ManagementSupportUserStateProvider(IIdentityUserStore users, IIdentityTenantStore organizations,
    ITenantMembershipStore memberships, ITenantRoleStore roles, IOptions<ManagementIdentityOptions> options) : IManagementSupportUserStateProvider
{
    public async Task<ManagementSupportUserState?> GetAsync(Guid organizationId, Guid userId, CancellationToken ct = default)
    {
        if (organizationId != options.Value.OrganizationId || userId == Guid.Empty) return null;
        var organization = await organizations.FindByIdAsync(organizationId, ct);
        var user = await users.FindByIdAsync(userId, ct);
        var membership = await memberships.GetTenantForUserAsync(userId, organizationId, ct);
        if (organization is null || user is null || membership is null) return null;
        var assigned = (await roles.GetRolesForUserAsync(organizationId, userId, ct)).Where(x => x.TenantId == organizationId && x.IsActive).ToArray();
        var admin = assigned.Any(x => x.Name == ManagementIdentityRoles.Administrator);
        return new(organizationId, userId, organization.IsActive && membership.IsActive && user.TwoFactorEnabled &&
            (admin || assigned.Any(x => x.Name == ManagementIdentityRoles.Support)), admin);
    }
}
