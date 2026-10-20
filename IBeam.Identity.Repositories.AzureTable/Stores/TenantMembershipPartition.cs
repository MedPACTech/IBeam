using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
namespace IBeam.Identity.Repositories.AzureTable.Stores;

/// <summary>Persistence fence only: membership + partition revision commit in one Table transaction.
/// Business decisions (including administrator eligibility) belong to services.</summary>
internal static class TenantMembershipPartition
{
    internal const string GuardKey = "!IBEAM|MEMBERSHIP";
    internal static async Task<TableEntity> Read(TableClient table, string partition, CancellationToken ct)
    {
        var row = await table.GetEntityIfExistsAsync<TableEntity>(partition, GuardKey, cancellationToken: ct);
        if (row.HasValue) return row.Value;
        try { await table.AddEntityAsync(new TableEntity(partition, GuardKey) { ["Revision"] = Guid.NewGuid(), ["AdministratorRoleIds"] = "" }, ct); }
        catch (RequestFailedException ex) when (ex.Status == 409) { }
        return (await table.GetEntityAsync<TableEntity>(partition, GuardKey, cancellationToken: ct)).Value;
    }
    internal static async Task Write<T>(TableClient table, T row, bool add, CancellationToken ct, bool requireUnprotected = false) where T : class, ITableEntity, new()
    {
        if (row is Entities.TenantUserEntity member) {
            member.IBeamLastMembershipOperationId = "native";
            if (string.IsNullOrWhiteSpace(member.TenantId) && row.PartitionKey.StartsWith("TEN|", StringComparison.Ordinal) && Guid.TryParseExact(row.PartitionKey[4..], "D", out var tenant)) member.TenantId = tenant.ToString("D");
            if (string.IsNullOrWhiteSpace(member.UserId) && row.RowKey.StartsWith("USR|", StringComparison.Ordinal) && Guid.TryParseExact(row.RowKey[4..], "D", out var user)) member.UserId = user.ToString("D");
        }
        var guard = await Read(table, row.PartitionKey, ct);
        if (requireUnprotected && !string.IsNullOrEmpty(guard.GetString("AdministratorRoleIds")))
            throw new IdentityValidationException("Versioned membership administration is required for this tenant.");
        await Commit(table, row, add, guard, guard.ETag, ct);
    }
    internal static async Task Commit<T>(TableClient table, T row, bool add, TableEntity guard, ETag expectedGuard, CancellationToken ct, bool replace = false) where T : class, ITableEntity, new()
    {
        guard["Revision"] = Guid.NewGuid();
        await table.SubmitTransactionAsync([
            new(TableTransactionActionType.UpdateMerge, guard, expectedGuard),
            new(add ? TableTransactionActionType.Add : replace ? TableTransactionActionType.UpdateReplace : TableTransactionActionType.UpdateMerge, row, row.ETag)], ct);
    }
    internal static async Task RequireLegacyDestructiveWrite(TableClient table, string partition, CancellationToken ct)
    {
        var row = await table.GetEntityIfExistsAsync<TableEntity>(partition, GuardKey, cancellationToken: ct);
        if (row.HasValue && !string.IsNullOrEmpty(row.Value.GetString("AdministratorRoleIds")))
            throw new IdentityValidationException("Versioned membership administration is required for this tenant.");
    }
}
