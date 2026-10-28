using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

/// <summary>Persistence-only CAS protocol. Register a partition before writing it; a frozen root inventories every delayed writer.
/// Local tenant-specific guards also protect shared user/token partitions without blocking another tenant.</summary>
internal static class IdentityTenantPurgePartition
{
    internal const string Prefix = "!IBEAM|PURGE|";
    internal const string RootKey = Prefix + "ROOT";
    internal static string RootPartition(Guid tenant) => "PURGE|" + tenant.ToString("D");
    internal static string LocalKey(Guid tenant) => Prefix + tenant.ToString("D");
    internal static string RegistryKey(string partition) => Prefix + "PART|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(partition)));
    internal static bool Fenced(TableEntity row) => !string.IsNullOrEmpty(row.GetString("PurgeScope"));
    internal static async Task<bool> HasTenantFence(TableServiceClient service, Options.AzureTableIdentityOptions options, Guid tenant, CancellationToken ct)
    {
        try {
            var row = await service.GetTableClient(options.FullTableName(options.TenantsTableName)).GetEntityIfExistsAsync<TableEntity>("TEN", LocalKey(tenant), cancellationToken: ct);
            return row.HasValue && Fenced(row.Value);
        } catch (RequestFailedException ex) when (ex.Status == 404 && !options.RequireTenantPurgeFencing) {
            // Standalone legacy credentials need not provision a tenants table; active purge deployments do.
            return false;
        }
    }
    internal static TableEntity Control(string partition, string key) => new(partition, key) { ["Status"] = "PurgeControl", ["IsDeleted"] = true, ["Revision"] = Guid.NewGuid() };
    internal static async Task<TableEntity> Ensure(TableClient table, string partition, string key, CancellationToken ct)
    {
        var current = await table.GetEntityIfExistsAsync<TableEntity>(partition, key, cancellationToken: ct);
        if (current.HasValue) return current.Value;
        try { await table.AddEntityAsync(Control(partition, key), ct); }
        catch (RequestFailedException ex) when (ex.Status == 409) { }
        return (await table.GetEntityAsync<TableEntity>(partition, key, cancellationToken: ct)).Value;
    }
    internal static async Task RequireMutable(TableClient table, Guid tenant, string partition, CancellationToken ct)
    { if (await IsFenced(table, tenant, partition, ct)) throw new IdentityConcurrencyException(); }
    internal static async Task<bool> IsFenced(TableClient table, Guid tenant, string partition, CancellationToken ct)
    {
        var root = await table.GetEntityIfExistsAsync<TableEntity>(RootPartition(tenant), RootKey, cancellationToken: ct);
        var local = await table.GetEntityIfExistsAsync<TableEntity>(partition, LocalKey(tenant), cancellationToken: ct);
        return root.HasValue && Fenced(root.Value) || local.HasValue && Fenced(local.Value);
    }
    private static async Task Register(TableClient table, Guid tenant, string partition, CancellationToken ct)
    {
        var rootPartition = RootPartition(tenant); var key = RegistryKey(partition);
        for (var attempt = 0; attempt < 8; attempt++) {
            var root = await Ensure(table, rootPartition, RootKey, ct);
            if (Fenced(root)) throw new IdentityConcurrencyException();
            var existing = await table.GetEntityIfExistsAsync<TableEntity>(rootPartition, key, cancellationToken: ct);
            if (existing.HasValue) {
                if (existing.Value.GetString("TargetPartition") != partition) throw new IdentityProviderException("Tenant writer registry is invalid.");
                return;
            }
            root["Revision"] = Guid.NewGuid();
            try {
                await table.SubmitTransactionAsync(new[] {
                    new TableTransactionAction(TableTransactionActionType.UpdateReplace, root, root.ETag),
                    new TableTransactionAction(TableTransactionActionType.Add, new TableEntity(rootPartition, key) { ["TargetPartition"] = partition, ["Status"] = "PurgeControl" }) }, ct);
                return;
            } catch (RequestFailedException ex) when (ex.Status is 409 or 412) { }
        }
        throw new IdentityConcurrencyException();
    }
    internal static async Task<TableEntity?> Prepare(TableClient table, Guid tenant, string partition, bool enabled, CancellationToken ct)
    {
        // Unscoped account records are outside tenant deletion authority and retain legacy behavior.
        if (tenant == Guid.Empty) return null;
        var old = await table.GetEntityIfExistsAsync<TableEntity>(partition, LocalKey(tenant), cancellationToken: ct);
        if (old.HasValue && Fenced(old.Value)) throw new IdentityConcurrencyException();
        if (!enabled && !old.HasValue) {
            var root = await table.GetEntityIfExistsAsync<TableEntity>(RootPartition(tenant), RootKey, cancellationToken: ct);
            if (!root.HasValue) return null;
        }
        await Register(table, tenant, partition, ct);
        var local = old.HasValue ? old.Value : await Ensure(table, partition, LocalKey(tenant), ct);
        if (Fenced(local)) throw new IdentityConcurrencyException();
        return local;
    }
    internal static async Task<Response> Commit(TableClient table, IReadOnlyList<TableTransactionAction> actions, IReadOnlyList<Guid> tenants, bool enabled, CancellationToken ct)
    {
        if (actions.Count == 0) throw new IdentityValidationException("A native write is required.");
        var partition = actions[0].Entity.PartitionKey;
        if (actions.Any(x => x.Entity.PartitionKey != partition)) throw new IdentityValidationException("A native write must have one partition.");
        var all = new List<TableTransactionAction>();
        foreach (var tenant in tenants.Distinct()) {
            var guard = await Prepare(table, tenant, partition, enabled, ct);
            if (guard is not null) { guard["Revision"] = Guid.NewGuid(); all.Add(new(TableTransactionActionType.UpdateReplace, guard, guard.ETag)); }
        }
        if (all.Count + actions.Count > 100) throw new IdentityValidationException("Native write exceeds its bounded transaction.");
        all.AddRange(actions);
        var result = await table.SubmitTransactionAsync(all, ct); return result.Value[^1];
    }
    internal static Task<Response> Add<T>(TableClient table, T row, Guid tenant, bool enabled, CancellationToken ct) where T : class, ITableEntity, new()
        => Commit(table, [new(TableTransactionActionType.Add, row)], [tenant], enabled, ct);
    internal static Task<Response> Update<T>(TableClient table, T row, ETag etag, TableUpdateMode mode, Guid tenant, bool enabled, CancellationToken ct) where T : class, ITableEntity, new()
        => Commit(table, [new(mode == TableUpdateMode.Replace ? TableTransactionActionType.UpdateReplace : TableTransactionActionType.UpdateMerge, row, etag)], [tenant], enabled, ct);
    internal static Task<Response> Upsert<T>(TableClient table, T row, TableUpdateMode mode, Guid tenant, bool enabled, CancellationToken ct) where T : class, ITableEntity, new()
        => Commit(table, [new(mode == TableUpdateMode.Replace ? TableTransactionActionType.UpsertReplace : TableTransactionActionType.UpsertMerge, row)], [tenant], enabled, ct);
    internal static Task<Response> Add<T>(TableClient table, T row, bool enabled, CancellationToken ct) where T : class, ITableEntity, new() => Add(table, row, TenantOf(row), enabled, ct);
    internal static Task<Response> Update<T>(TableClient table, T row, ETag etag, TableUpdateMode mode, bool enabled, CancellationToken ct) where T : class, ITableEntity, new() => Update(table, row, etag, mode, TenantOf(row), enabled, ct);
    internal static Task<Response> Upsert<T>(TableClient table, T row, TableUpdateMode mode, bool enabled, CancellationToken ct) where T : class, ITableEntity, new() => Upsert(table, row, mode, TenantOf(row), enabled, ct);
    internal static Task<Response> Commit(TableClient table, IReadOnlyList<TableTransactionAction> actions, bool enabled, CancellationToken ct) => Commit(table, actions, actions.Select(x => TenantOf(x.Entity)).ToArray(), enabled, ct);
    internal static Task<Response> Delete(TableClient table, string partition, string row, ETag etag, bool enabled, CancellationToken ct) => Commit(table,
        [new(TableTransactionActionType.Delete, new TableEntity(partition, row), etag)], [TenantOf(new TableEntity(partition, row))], enabled, ct);
    internal static Guid TenantOf(ITableEntity row)
    {
        Guid tenant;
        if (row.PartitionKey == "TEN" && Guid.TryParseExact(row.RowKey, "D", out tenant)) return tenant;
        if (row.PartitionKey.StartsWith("TEN|", StringComparison.Ordinal) && row.PartitionKey.Length >= 40 && Guid.TryParseExact(row.PartitionKey.Substring(4, 36), "D", out tenant)) return tenant;
        if (row.PartitionKey.StartsWith("USR|", StringComparison.Ordinal) && row.RowKey.Length >= 40 && row.RowKey.StartsWith("TEN", StringComparison.Ordinal) &&
            Guid.TryParseExact(row.RowKey[4..], "D", out tenant)) return tenant;
        var tagged = row is TableEntity dictionary ? dictionary.GetString("TenantId") : row.GetType().GetProperty("TenantId")?.GetValue(row)?.ToString();
        if (Guid.TryParse(tagged, out tenant) && tenant != Guid.Empty) return tenant;
        throw new IdentityValidationException("The native row has no supported tenant scope.");
    }
}
