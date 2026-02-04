using Azure.Data.Tables;
using IBeam.Identity.Core.Auth.Contracts;
using IBeam.Identity.Core.Tenants;

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

    public async Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(string userId, CancellationToken ct = default)
    {
        var table = GetUserTenantsTable();
        var pk = _opts.UserPk(userId);

        var results = new List<TenantInfo>();

        await foreach (var e in table.QueryAsync<UserTenantEntity>(x => x.PartitionKey == pk, cancellationToken: ct))
        {
            if (!_opts.TryParseTenantIdFromRowKey(e.RowKey, out var tenantId))
                continue;

            var roles = (e.RolesCsv ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            results.Add(new TenantInfo(
                tenantId,
                e.DisplayName,
                roles,
                e.Status ?? "Active"
            ));
        }

        return results;
    }

    public async Task<TenantInfo?> GetTenantForUserAsync(string userId, Guid tenantId, CancellationToken ct = default)
    {
        var table = GetUserTenantsTable();
        var pk = _opts.UserPk(userId);
        var rk = _opts.TenantRk(tenantId);

        try
        {
            var resp = await table.GetEntityAsync<UserTenantEntity>(pk, rk, cancellationToken: ct);
            var e = resp.Value;

            var roles = (e.RolesCsv ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return new TenantInfo(tenantId, e.DisplayName, roles, e.Status ?? "Active");
        }
        catch
        {
            return null;
        }
    }

    public async Task<Guid?> GetDefaultTenantIdAsync(string userId, CancellationToken ct = default)
    {
        var table = GetUserTenantsTable();
        var pk = _opts.UserPk(userId);

        await foreach (var e in table.QueryAsync<UserTenantEntity>(
                           x => x.PartitionKey == pk && x.IsDefault == true,
                           cancellationToken: ct))
        {
            if (_opts.TryParseTenantIdFromRowKey(e.RowKey, out var tenantId))
                return tenantId;
        }

        return null;
    }

    public async Task SetDefaultTenantAsync(string userId, Guid tenantId, CancellationToken ct = default)
    {
        var table = GetUserTenantsTable();
        var pk = _opts.UserPk(userId);
        var rk = _opts.TenantRk(tenantId);

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
