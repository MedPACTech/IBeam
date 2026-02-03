using Azure.Data.Tables;
using IBeam.Identity.Core.Tenants;
using IBeam.Identity.Storage.AzureTable.Tenants.Entities;

namespace IBeam.Identity.Storage.AzureTable.Tenants;

public sealed class AzureTableTenantProvisioningService : ITenantProvisioningService
{
    private readonly TableServiceClient _svc;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableTenantProvisioningService(TableServiceClient svc, AzureTableIdentityOptions opts)
    {
        _svc = svc;
        _opts = opts;
    }

    private TableClient TenantsTable()
    {
        var t = _svc.GetTableClient($"{_opts.TablePrefix}Tenants");
        t.CreateIfNotExists();
        return t;
    }

    private TableClient TenantUsersTable()
    {
        var t = _svc.GetTableClient($"{_opts.TablePrefix}TenantUsers");
        t.CreateIfNotExists();
        return t;
    }

    private TableClient UserTenantsTable()
    {
        var t = _svc.GetTableClient($"{_opts.TablePrefix}UserTenants");
        t.CreateIfNotExists();
        return t;
    }

    private static string PkTenant(Guid tenantId) => $"TEN|{tenantId:D}";
    private static string RkUser(string userId) => $"USR|{userId}";
    private static string PkUser(string userId) => $"USR|{userId}";
    private static string RkTenant(Guid tenantId) => $"TEN|{tenantId:D}";

    public async Task<Guid> CreateTenantForNewUserAsync(string userId, string? email, CancellationToken ct = default)
    {
        var tenantId = Guid.NewGuid();

        var tenantName = !string.IsNullOrWhiteSpace(email)
            ? $"{email.Split('@')[0]}'s Workspace"
            : "Workspace";

        // 1) Create tenant row
        await TenantsTable().AddEntityAsync(new TenantEntity
        {
            RowKey = tenantId.ToString("D"),
            Name = tenantName,
            OwnerUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);

        // 2) Create tenant->user membership (Owner/Admin)
        await TenantUsersTable().UpsertEntityAsync(new TenantUserEntity
        {
            PartitionKey = PkTenant(tenantId),
            RowKey = RkUser(userId),
            Status = "Active",
            RolesCsv = "Owner,Admin",
            CreatedAt = DateTimeOffset.UtcNow
        }, TableUpdateMode.Replace, ct);

        // 3) Create user->tenant membership (also default)
        await UserTenantsTable().UpsertEntityAsync(new UserTenantEntity
        {
            PartitionKey = PkUser(userId),
            RowKey = RkTenant(tenantId),
            Status = "Active",
            RolesCsv = "Owner,Admin",
            DisplayName = tenantName,
            IsDefault = true,
            LastSelectedAt = DateTimeOffset.UtcNow
        }, TableUpdateMode.Replace, ct);

        return tenantId;
    }
}
