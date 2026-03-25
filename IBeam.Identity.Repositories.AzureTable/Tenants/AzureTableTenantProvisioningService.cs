using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Tenants;

internal sealed class AzureTableTenantProvisioningService : ITenantProvisioningService
{
    private readonly TableServiceClient _svc;
    private readonly AzureTableIdentityOptions _opts;
    private readonly ITenantRoleStore _tenantRoles;

    public AzureTableTenantProvisioningService(
        TableServiceClient svc,
        IOptions<AzureTableIdentityOptions> opts,
        ITenantRoleStore tenantRoles)
    {
        _svc = svc;
        _opts = opts.Value;
        _tenantRoles = tenantRoles;
    }

    private TableClient TenantsTable()
        => _svc.GetTableClient(_opts.FullTableName(_opts.TenantsTableName));

    private TableClient TenantUsersTable()
        => _svc.GetTableClient(_opts.FullTableName(_opts.TenantUsersTableName));

    private TableClient UserTenantsTable()
        => _svc.GetTableClient(_opts.FullTableName(_opts.UserTenantsTableName));

    public async Task<Guid> CreateTenantForNewUserAsync(Guid userId, string? email, CancellationToken ct = default)
    {
        var tenantId = Guid.NewGuid();

        var tenantName = !string.IsNullOrWhiteSpace(email)
            ? $"{email.Split('@')[0]}'s Workspace"
            : "Workspace";

        var now = DateTimeOffset.UtcNow;
        var userIdStr = userId.ToString("D");

        // 1) Create tenant row
        await TenantsTable().AddEntityAsync(new TenantEntity
        {
            PartitionKey = TenantEntity.TenantsPartitionKey, // "TEN" if you kept the const
            RowKey = tenantId.ToString("D"),
            Name = tenantName,
            NormalizedName = tenantName.Trim().ToUpperInvariant(),
            OwnerUserId = userIdStr,
            Status = "Active",
            CreatedAt = now
        }, ct).ConfigureAwait(false);

        // 2) Seed tenant roles and assign creator to defaults.
        await _tenantRoles.EnsureDefaultRolesAsync(tenantId, ct).ConfigureAwait(false);
        var defaultRoles = await _tenantRoles.GetRolesAsync(tenantId, ct).ConfigureAwait(false);
        var rolesCsv = string.Join(",", defaultRoles.Select(x => x.Name));
        var roleIdsCsv = string.Join(",", defaultRoles.Select(x => x.RoleId.ToString("D")));

        // 3) Create tenant->user membership (Owner/Administrator)
        await TenantUsersTable().UpsertEntityAsync(new TenantUserEntity
        {
            PartitionKey = _opts.TenantUsersPk(tenantId),   // "TEN#{tenantId}"
            RowKey = _opts.TenantUsersRk(userIdStr),        // "USR#{userId}"
            TenantId = tenantId.ToString("D"),
            UserId = userIdStr,
            Status = "Active",
            RolesCsv = rolesCsv,
            RoleIdsCsv = roleIdsCsv,
            CreatedAt = now
        }, TableUpdateMode.Replace, ct).ConfigureAwait(false);

        // 4) Create user->tenant membership (also default)
        await UserTenantsTable().UpsertEntityAsync(new UserTenantEntity
        {
            PartitionKey = _opts.UserTenantsPk(userIdStr),  // "USR#{userId}"
            RowKey = _opts.UserTenantsRk(tenantId),         // "TEN#{tenantId}"

            UserId = userIdStr,
            TenantId = tenantId.ToString("D"),

            Status = "Active",
            RolesCsv = rolesCsv,
            RoleIdsCsv = roleIdsCsv,

            TenantDisplayName = tenantName,
            IsDefault = true,
            LastSelectedAt = now,
            CreatedAt = now
        }, TableUpdateMode.Replace, ct).ConfigureAwait(false);

        return tenantId;
    }
}
