using Azure.Data.Tables;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;

namespace IBeam.Identity.Repositories.AzureTable.Tenants;

public sealed class AzureTableTenantMembershipStore : ITenantMembershipStore
{
    private readonly TableServiceClient _svc;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableTenantMembershipStore(TableServiceClient svc, AzureTableIdentityOptions opts)
    {
        _svc = svc;
        _opts = opts;
    }

    private TableClient GetUserTenantsTable()
    {
        var name = $"{_opts.TablePrefix}UserTenants";
        var table = _svc.GetTableClient(name);
        table.CreateIfNotExists();
        return table;
    }

    public async Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var table = GetUserTenantsTable();
        var userIdStr = userId.ToString("D");
        var pk = _opts.UserTenantsPk(userIdStr);

        var results = new List<TenantInfo>();

        await foreach (var e in table.QueryAsync<UserTenantEntity>(x => x.PartitionKey == pk, cancellationToken: ct))
        {
            if (!_opts.TryParseTenantIdFromUserTenantsRk(e.RowKey, out var tenantId))
                continue;

            var roles = (e.RolesCsv ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var isActive = string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase);

            results.Add(new TenantInfo(
                tenantId,
                e.TenantDisplayName,
                roles,
                isActive
            ));
        }

        return results;
    }

    public async Task<TenantInfo?> GetTenantForUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var table = GetUserTenantsTable();
        var userIdStr = userId.ToString("D");
        var pk = _opts.UserTenantsPk(userIdStr);
        var rk = _opts.UserTenantsRk(tenantId);

        try
        {
            var resp = await table.GetEntityAsync<UserTenantEntity>(pk, rk, cancellationToken: ct);
            var e = resp.Value;

            var roles = (e.RolesCsv ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var isActive = string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase);

            return new TenantInfo(tenantId, e.TenantDisplayName, roles, isActive);
        }
        catch
        {
            return null;
        }
    }

    public async Task<Guid?> GetDefaultTenantIdAsync(Guid userId, CancellationToken ct = default)
    {
        var table = GetUserTenantsTable();
        var userIdStr = userId.ToString("D");
        var pk = _opts.UserTenantsPk(userIdStr);

        await foreach (var e in table.QueryAsync<UserTenantEntity>(
                           x => x.PartitionKey == pk && x.IsDefault == true,
                           cancellationToken: ct))
        {
            if (_opts.TryParseTenantIdFromUserTenantsRk(e.RowKey, out var tenantId))
                return tenantId;
        }

        return null;
    }

    public async Task SetDefaultTenantAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var table = GetUserTenantsTable();
        var userIdStr = userId.ToString("D");
        var pk = _opts.UserTenantsPk(userIdStr);
        var rk = _opts.UserTenantsRk(tenantId);

        // Verify membership exists
        try
        {
            _ = (await table.GetEntityAsync<UserTenantEntity>(pk, rk, cancellationToken: ct)).Value;
        }
        catch
        {
            throw new InvalidOperationException("User is not a member of the selected tenant.");
        }

        // Unset existing defaults (within this user's partition)
        var batch = new List<TableTransactionAction>();

        await foreach (var e in table.QueryAsync<UserTenantEntity>(x => x.PartitionKey == pk, cancellationToken: ct))
        {
            var shouldBeDefault = string.Equals(e.RowKey, rk, StringComparison.Ordinal);

            if (e.IsDefault != shouldBeDefault)
            {
                e.IsDefault = shouldBeDefault;
                e.LastSelectedAt = shouldBeDefault ? DateTimeOffset.UtcNow : e.LastSelectedAt;

                batch.Add(new TableTransactionAction(TableTransactionActionType.UpdateReplace, e));
            }

            if (batch.Count == 100)
            {
                await table.SubmitTransactionAsync(batch, ct);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            await table.SubmitTransactionAsync(batch, ct);
    }
}
