using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Repositories.AzureTable.Options;
namespace IBeam.Identity.Repositories.AzureTable.Stores;

/// <summary>Persistence fence only: membership + partition revision commit in one Table transaction.
/// Business decisions (including administrator eligibility) belong to services.</summary>
internal static class TenantMembershipPartition
{
    internal const string GuardKey = "!IBEAM|MEMBERSHIP";
    internal static async Task<TableEntity> Read(TableClient table, string partition, CancellationToken ct, bool requirePurge = false)
    {
        var tenant = IdentityTenantPurgePartition.TenantOf(new TableEntity(partition, GuardKey));
        await IdentityTenantPurgePartition.RequireMutable(table, tenant, partition, ct);
        var row = await table.GetEntityIfExistsAsync<TableEntity>(partition, GuardKey, cancellationToken: ct);
        if (row.HasValue) return row.Value;
        try { await IdentityTenantPurgePartition.Add(table, new TableEntity(partition, GuardKey) { ["Revision"] = Guid.NewGuid(), ["AdministratorRoleIds"] = "" }, requirePurge, ct); }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412) { await IdentityTenantPurgePartition.RequireMutable(table, tenant, partition, ct); }
        catch (IdentityConcurrencyException) { await IdentityTenantPurgePartition.RequireMutable(table, tenant, partition, ct); }
        return (await table.GetEntityAsync<TableEntity>(partition, GuardKey, cancellationToken: ct)).Value;
    }
    internal static async Task Write<T>(TableClient table, T row, bool add, CancellationToken ct, bool requireUnprotected = false,
        bool requireOwnershipProtection = false, bool requirePurge = false, AzureTableIdentityOptions? eventOptions = null) where T : class, ITableEntity, new()
    {
        if (row is Entities.TenantUserEntity member) {
            member.IBeamLastMembershipOperationId = "native";
            if (string.IsNullOrWhiteSpace(member.TenantId) && row.PartitionKey.StartsWith("TEN|", StringComparison.Ordinal) && Guid.TryParseExact(row.PartitionKey[4..], "D", out var tenant)) member.TenantId = tenant.ToString("D");
            if (string.IsNullOrWhiteSpace(member.UserId) && row.RowKey.StartsWith("USR|", StringComparison.Ordinal) && Guid.TryParseExact(row.RowKey[4..], "D", out var user)) member.UserId = user.ToString("D");
        }
        var guard = await Read(table, row.PartitionKey, ct, requirePurge);
        var owned = requireOwnershipProtection || !string.IsNullOrEmpty(guard.GetString("OwnerRoleIds"));
        if (requireUnprotected && (owned || !string.IsNullOrEmpty(guard.GetString("AdministratorRoleIds"))))
            throw new IdentityValidationException("Versioned membership administration is required for this tenant.");
        if (owned && !add) {
            var before = await table.GetEntityIfExistsAsync<TableEntity>(row.PartitionKey, row.RowKey,
                ["Status", "RoleIdsCsv", "RolesCsv"], ct);
            if (before.HasValue) {
                if (row is not Entities.TenantUserEntity memberRow ||
                    string.Equals(before.Value.GetString("Status"), "Active", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(memberRow.Status, "Active", StringComparison.OrdinalIgnoreCase))
                    throw new IdentityValidationException("Versioned membership administration is required for this tenant.");
                var ids = Tokens(before.Value.GetString("RoleIdsCsv"));
                if (ids.Except(Tokens(memberRow.RoleIdsCsv), StringComparer.OrdinalIgnoreCase).Any() ||
                    ids.Length == 0 && Tokens(before.Value.GetString("RolesCsv")).Except(Tokens(memberRow.RolesCsv), StringComparer.OrdinalIgnoreCase).Any())
                    throw new IdentityValidationException("Versioned membership administration is required for this tenant.");
            }
        }
        await Commit(table, row, add, guard, guard.ETag, ct, requirePurge: requirePurge, eventOptions: eventOptions);
    }
    internal static async Task Commit<T>(TableClient table, T row, bool add, TableEntity guard, ETag expectedGuard, CancellationToken ct, bool replace = false, bool requirePurge = false, AzureTableIdentityOptions? eventOptions = null) where T : class, ITableEntity, new()
    {
        guard["Revision"] = Guid.NewGuid();
        var actions = new List<TableTransactionAction> {
            new(TableTransactionActionType.UpdateMerge, guard, expectedGuard),
            new(add ? TableTransactionActionType.Add : replace ? TableTransactionActionType.UpdateReplace : TableTransactionActionType.UpdateMerge, row, row.ETag) };
        try {
            await IdentityMembershipEventPartition.AppendAsync(table, actions, guard, eventOptions, null, null, ct);
            await IdentityTenantPurgePartition.Commit(table, actions, requirePurge, ct);
        } catch (RequestFailedException ex) when (eventOptions?.RequireMembershipLifecycleEvents == true && ex.Status is 409 or 412) { throw new IdentityConcurrencyException(); }
        catch (Exception ex) when (eventOptions?.RequireMembershipLifecycleEvents == true && ex is not IBeam.Identity.Exceptions.IdentityException && !(ex is OperationCanceledException && ct.IsCancellationRequested))
            { throw new IdentityProviderException("The atomic membership event commit outcome is unavailable; inspect its original operation."); }
    }
    internal static async Task RequireLegacyDestructiveWrite(TableClient table, string partition, CancellationToken ct, bool requireOwnershipProtection = false)
    {
        await IdentityTenantPurgePartition.RequireMutable(table, IdentityTenantPurgePartition.TenantOf(new TableEntity(partition, GuardKey)), partition, ct);
        var row = await table.GetEntityIfExistsAsync<TableEntity>(partition, GuardKey, cancellationToken: ct);
        if (requireOwnershipProtection || row.HasValue && (!string.IsNullOrEmpty(row.Value.GetString("AdministratorRoleIds")) || !string.IsNullOrEmpty(row.Value.GetString("OwnerRoleIds"))))
            throw new IdentityValidationException("Versioned membership administration is required for this tenant.");
    }
    internal static async Task RequireOwnerAwareWrite(TableClient table, string partition, CancellationToken ct, bool required)
    {
        await IdentityTenantPurgePartition.RequireMutable(table, IdentityTenantPurgePartition.TenantOf(new TableEntity(partition, GuardKey)), partition, ct);
        var row = await table.GetEntityIfExistsAsync<TableEntity>(partition, GuardKey, cancellationToken: ct);
        if (required || row.HasValue && !string.IsNullOrEmpty(row.Value.GetString("OwnerRoleIds")))
            throw new IdentityValidationException("An ownership-aware versioned operation is required for this tenant.");
    }
    private static string[] Tokens(string? csv) => (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
