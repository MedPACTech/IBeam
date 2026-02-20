using Azure.Data.Tables;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Tenants;

internal sealed class AzureTableTenantProvisioningService : ITenantProvisioningService
{
    private readonly TableServiceClient _svc;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableTenantProvisioningService(
        TableServiceClient svc,
        IOptions<AzureTableIdentityOptions> opts)
    {
        _svc = svc;
        _opts = opts.Value;
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

        // 2) Create tenant->user membership (Owner/Admin)
        await TenantUsersTable().UpsertEntityAsync(new TenantUserEntity
        {
            PartitionKey = _opts.TenantUsersPk(tenantId),   // "TEN#{tenantId}"
            RowKey = _opts.TenantUsersRk(userIdStr),        // "USR#{userId}"
            Status = "Active",
            RolesCsv = "Owner,Admin",
            CreatedAt = now
        }, TableUpdateMode.Replace, ct).ConfigureAwait(false);

        // 3) Create user->tenant membership (also default)
        await UserTenantsTable().UpsertEntityAsync(new UserTenantEntity
        {
            PartitionKey = _opts.UserTenantsPk(userIdStr),  // "USR#{userId}"
            RowKey = _opts.UserTenantsRk(tenantId),         // "TEN#{tenantId}"

            UserId = userIdStr,
            TenantId = tenantId.ToString("D"),

            Status = "Active",
            RolesCsv = "Owner,Admin",

            TenantDisplayName = tenantName,
            IsDefault = true,
            LastSelectedAt = now,
            CreatedAt = now
        }, TableUpdateMode.Replace, ct).ConfigureAwait(false);

        return tenantId;
    }
}
