using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Tenants;

internal sealed class AzureTableTenantProvisioningService : ITenantProvisioningService
{
    private readonly ITenantRoleStore _tenantRoles;

    public AzureTableTenantProvisioningService(
        Azure.Data.Tables.TableServiceClient svc,
        IOptions<AzureTableIdentityOptions> opts,
        ITenantRoleStore tenantRoles)
    {
        ArgumentNullException.ThrowIfNull(svc);
        ArgumentNullException.ThrowIfNull(opts);
        _tenantRoles = tenantRoles;
    }

    public async Task<Guid> CreateTenantForNewUserAsync(Guid userId, string? email, CancellationToken ct = default)
    {
        var tenantId = DeterministicGuid.Create(
            "IBeam.Identity.AzureTable.TenantForUser",
            userId.ToString("D"));
        var tenantName = !string.IsNullOrWhiteSpace(email)
            ? $"{email.Split('@')[0]}'s Workspace"
            : "Workspace";
        var ownerRoles = new[]
        {
            AzureTableTenantRoleStore.OwnerRoleName,
            AzureTableTenantRoleStore.AdminRoleName
        };

        await _tenantRoles.EnsureTenantMembershipAndGrantRolesAsync(
            new TenantMembershipRoleBootstrapRequest(
                TenantId: tenantId,
                UserId: userId,
                TenantName: tenantName,
                RoleNames: ownerRoles,
                SetAsDefault: true),
            ct).ConfigureAwait(false);

        return tenantId;
    }

    public async Task EnsureUserTenantMembershipAsync(
        Guid tenantId,
        Guid userId,
        string? tenantName = null,
        IReadOnlyList<string>? roleNames = null,
        bool setAsDefault = false,
        CancellationToken ct = default)
    {
        await _tenantRoles.EnsureTenantMembershipAndGrantRolesAsync(
            new TenantMembershipRoleBootstrapRequest(
                TenantId: tenantId,
                UserId: userId,
                TenantName: tenantName,
                RoleNames: roleNames,
                SetAsDefault: setAsDefault),
            ct).ConfigureAwait(false);
    }
}
