using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed partial class AzureTableTenantMembershipStore : IIdentityTenantOwnershipTransferStore
{
    private static string TransferKey(Guid operation) => "!IBEAM|OWNERSHIP|" + operation.ToString("D");
    public Task<IdentityTenantMembershipSnapshot> ReadOwnershipSnapshotAsync(Guid tenant, int maximumMembers, CancellationToken ct = default)
        => ReadSnapshotCoreAsync(tenant, maximumMembers, true, ct);
    public async Task<IdentityTenantOwnershipTransferReceipt?> FindOwnershipTransferAsync(Guid tenant, Guid operation, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty || operation == Guid.Empty) throw new IdentityValidationException("Ownership scope is invalid.");
        var row = await GetTenantUsersTable().GetEntityIfExistsAsync<TableEntity>(_opts.TenantUsersPk(tenant), TransferKey(operation), cancellationToken: ct);
        if (!row.HasValue) return null;
        var value = row.Value;
        if (value.GetString("TenantId") != tenant.ToString("D") || value.GetString("OperationId") != operation.ToString("D") ||
            !Guid.TryParseExact(value.GetString("SourceUserId"), "D", out var source) || source == Guid.Empty ||
            !Guid.TryParseExact(value.GetString("DestinationUserId"), "D", out var destination) || destination == Guid.Empty || source == destination ||
            !ValidHash(value.GetString("IntentHash"))) throw new IdentityProviderException("Ownership receipt is invalid.");
        return new(tenant, operation, source, destination, value.GetString("IntentHash")!);
    }
    public async Task<IdentityTenantOwnershipTransferReceipt> CommitOwnershipTransferAsync(IdentityTenantOwnershipChange change, CancellationToken ct = default)
    {
        var source = change.Source; var destination = change.Destination; var tenant = source.TenantId;
        if (!_opts.RequireOwnershipProtection || tenant == Guid.Empty || destination.TenantId != tenant || source.UserId == Guid.Empty ||
            destination.UserId == Guid.Empty || source.UserId == destination.UserId || change.OperationId == Guid.Empty ||
            !Exact(source.Version) || !Exact(destination.Version) || !Exact(change.ExpectedOwnershipVersion) || !ValidHash(change.IntentHash) ||
            source.RoleIds.Count > 32 || destination.RoleIds.Count > 32 || source.RoleNames.Count > 32 || destination.RoleNames.Count > 32)
            throw new IdentityValidationException("Ownership transfer is invalid.");
        var existing = await FindOwnershipTransferAsync(tenant, change.OperationId, ct);
        if (existing is not null) return SameIntent(existing, change);
        var table = GetTenantUsersTable(); var pk = _opts.TenantUsersPk(tenant);
        var guard = await table.GetEntityAsync<TableEntity>(pk, TenantMembershipPartition.GuardKey, cancellationToken: ct);
        if (guard.Value.ETag.ToString() != change.ExpectedOwnershipVersion) throw new IdentityConcurrencyException();
        if (guard.Value.GetInt32("OwnershipProtocolVersion") != 1 || string.IsNullOrEmpty(guard.Value.GetString("OwnerRoleIds")))
            throw new IdentityValidationException("Native ownership protection is required.");
        var receipt = new TableEntity(pk, TransferKey(change.OperationId)) {
            ["TenantId"] = tenant.ToString("D"), ["OperationId"] = change.OperationId.ToString("D"),
            ["SourceUserId"] = source.UserId.ToString("D"), ["DestinationUserId"] = destination.UserId.ToString("D"),
            ["IntentHash"] = change.IntentHash, ["CommittedAt"] = DateTimeOffset.UtcNow };
        TableEntity Membership(IdentityTenantMembershipState state) => new(pk, _opts.TenantUsersRk(state.UserId.ToString("D"))) {
            ["RoleIdsCsv"] = string.Join(',', state.RoleIds.Select(x => x.ToString("D"))), ["RolesCsv"] = string.Join(',', state.RoleNames),
            ["IBeamLastMembershipOperationId"] = change.OperationId.ToString("D"), ["UpdatedAt"] = DateTimeOffset.UtcNow };
        guard.Value["Revision"] = Guid.NewGuid();
        try {
            await table.SubmitTransactionAsync([
                new(TableTransactionActionType.UpdateMerge, guard.Value, new ETag(change.ExpectedOwnershipVersion)),
                new(TableTransactionActionType.UpdateMerge, Membership(source), new ETag(source.Version)),
                new(TableTransactionActionType.UpdateMerge, Membership(destination), new ETag(destination.Version)),
                new(TableTransactionActionType.Add, receipt)], ct);
        } catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412) {
            var committed = await FindOwnershipTransferAsync(tenant, change.OperationId, ct);
            if (committed is not null) return SameIntent(committed, change);
            throw new IdentityConcurrencyException();
        }
        return new(tenant, change.OperationId, source.UserId, destination.UserId, change.IntentHash);
    }
    private static IdentityTenantOwnershipTransferReceipt SameIntent(IdentityTenantOwnershipTransferReceipt receipt, IdentityTenantOwnershipChange change)
        => receipt.IntentHash == change.IntentHash && receipt.SourceUserId == change.Source.UserId && receipt.DestinationUserId == change.Destination.UserId
            ? receipt : throw new IdentityConcurrencyException();
    private static bool Exact(string? version) => !string.IsNullOrWhiteSpace(version) && version != "*" && version.Length <= 256;
    private static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
