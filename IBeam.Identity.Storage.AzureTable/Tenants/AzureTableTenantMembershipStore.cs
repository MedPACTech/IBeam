using Azure.Data.Tables;
using IBeam.Identity.Core.Auth.Contracts;
using IBeam.Identity.Core.Tenants;

namespace IBeam.Identity.Storage.AzureTable.Tenants;

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
        // You can choose naming. Keep it consistent & prefixed.
        var name = $"{_opts.TablePrefix}UserTenants";
        var table = _svc.GetTableClient(name);
        table.CreateIfNotExists();
        return table;
    }

    private static string PkForUser(string userId) => $"USR|{userId}";
    private static string RkForTenant(Guid tenantId) => $"TEN|{tenantId:D}";

    public async Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(string userId, CancellationToken ct = default)
    {
        var table = GetUserTenantsTable();
        var pk = PkForUser(userId);

        var results = new List<TenantInfo>();

        await foreach (var e in table.QueryAsync<UserTenantEntity>(x => x.PartitionKey == pk, cancellationToken: ct))
        {
            // RowKey format: TEN|{guid}
            var tenantIdString = e.RowKey.StartsWith("TEN|") ? e.RowKey[4..] : e.RowKey;
            if (!Guid.TryParse(tenantIdString, out var tenantId))
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
        var pk = PkForUser(userId);
        var rk = RkForTenant(tenantId);

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
    var pk = PkForUser(userId);

    await foreach (var e in table.QueryAsync<UserTenantEntity>(
                       x => x.PartitionKey == pk && x.IsDefault == true,
                       cancellationToken: ct))
    {
        var tenantIdString = e.RowKey.StartsWith("TEN|") ? e.RowKey[4..] : e.RowKey;
        if (Guid.TryParse(tenantIdString, out var tenantId))
            return tenantId;
    }

    return null;
}

public async Task SetDefaultTenantAsync(string userId, Guid tenantId, CancellationToken ct = default)
{
    var table = GetUserTenantsTable();
    var pk = PkForUser(userId);
    var rk = RkForTenant(tenantId);

    // 1) Verify the membership row exists
    UserTenantEntity selected;
    try
    {
        selected = (await table.GetEntityAsync<UserTenantEntity>(pk, rk, cancellationToken: ct)).Value;
    }
    catch
    {
        throw new InvalidOperationException("User is not a member of the selected tenant.");
    }

    // 2) Unset any existing defaults (scan just this user's partition)
    var batch = new List<TableTransactionAction>();

    await foreach (var e in table.QueryAsync<UserTenantEntity>(x => x.PartitionKey == pk, cancellationToken: ct))
    {
        var shouldBeDefault = string.Equals(e.RowKey, rk, StringComparison.Ordinal);

        // Only write if something changes
        if (e.IsDefault != shouldBeDefault)
        {
            e.IsDefault = shouldBeDefault;
            if (shouldBeDefault)
                e.LastSelectedAt = DateTimeOffset.UtcNow;

            // Use Replace to update the whole entity (simple)
            batch.Add(new TableTransactionAction(TableTransactionActionType.UpdateReplace, e));
        }

        // Azure Tables batch limit is 100 ops per transaction per partition
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

