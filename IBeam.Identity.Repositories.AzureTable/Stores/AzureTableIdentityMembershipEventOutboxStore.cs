using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed partial class AzureTableIdentityMembershipEventOutboxStore(TableServiceClient storage, IOptions<AzureTableIdentityOptions> options)
    : IIdentityMembershipEventOutboxStore
{
    private AzureTableIdentityOptions Options => options.Value;
    private TableClient Table => storage.GetTableClient(Options.FullTableName(Options.TenantUsersTableName));
    public int ProtocolVersion => Options.RequireMembershipLifecycleEvents ? 1 : 0;
    public bool Durable => Options.RequireMembershipLifecycleEvents;
    public async Task<TenantMembershipChangedEvent?> FindByOperationAsync(IdentityMembershipEventScope scope, Guid operationId, CancellationToken ct = default)
    {
        Require(scope); if (operationId == Guid.Empty) throw new IdentityValidationException("An event operation is required.");
        var index = await Table.GetEntityIfExistsAsync<TableEntity>(Partition(scope), IdentityMembershipEventPartition.OperationPrefix + operationId.ToString("D"), cancellationToken: ct);
        if (!index.HasValue) return null;
        if (index.Value.GetString("Binding") != IdentityMembershipEventPartition.ScopeHash(scope) || index.Value.GetInt64("Sequence") is not long sequence || sequence < 1)
            throw Invalid();
        var row = await Table.GetEntityIfExistsAsync<TableEntity>(Partition(scope), IdentityMembershipEventPartition.EventKey(sequence), cancellationToken: ct);
        if (!row.HasValue) throw Invalid(); var entry = Parse(row.Value, scope);
        if (entry.Event.OperationId != operationId || entry.Event.EventId != index.Value.GetGuid("EventId")) throw Invalid(); return entry.Event;
    }
    public async Task<IdentityMembershipEventPage> ReadPageAsync(IdentityMembershipEventScope scope, long afterSequence, int maximumEvents, CancellationToken ct = default)
    {
        Require(scope); if (afterSequence < 0 || maximumEvents is < 1 or > 100) throw new IdentityValidationException("Event page bounds are invalid.");
        var prefix = IdentityMembershipEventPartition.Prefix; var lower = IdentityMembershipEventPartition.EventKey(afterSequence); var upper = prefix + '\uffff';
        var pk = Partition(scope); var binding = IdentityMembershipEventPartition.ScopeHash(scope);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {pk} and RowKey gt {lower} and RowKey lt {upper} and Binding eq {binding}");
        await foreach (var page in Table.QueryAsync<TableEntity>(filter, maximumEvents + 1, cancellationToken: ct).AsPages(pageSizeHint: maximumEvents + 1)) {
            var events = page.Values.Take(maximumEvents).Select(x => Parse(x, scope).Event).ToArray();
            if (events.Any(x => x.Sequence <= afterSequence) || !events.Select(x => x.Sequence).SequenceEqual(events.Select(x => x.Sequence).Order()) || events.Select(x => x.Sequence).Distinct().Count() != events.Length) throw Invalid();
            return new(Array.AsReadOnly(events), events.Length == 0 ? afterSequence : events[^1].Sequence, page.Values.Count > maximumEvents || page.ContinuationToken is not null);
        }
        return new([], afterSequence, false);
    }
    public async Task<IdentityMembershipOutboxEntry?> ReadFirstUndeliveredAsync(IdentityMembershipEventScope scope, CancellationToken ct = default)
    {
        Require(scope); var pk = Partition(scope); var lower = IdentityMembershipEventPartition.Prefix; var upper = lower + '\uffff'; var hash = IdentityMembershipEventPartition.ScopeHash(scope);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {pk} and RowKey ge {lower} and RowKey lt {upper} and Binding eq {hash} and State ne {"acked"}");
        // One server page only; later events are never leased ahead of this earliest unacknowledged row.
        await foreach (var page in Table.QueryAsync<TableEntity>(filter, 1, cancellationToken: ct).AsPages(pageSizeHint: 1)) {
            return page.Values.Count == 0 ? null : Parse(page.Values[0], scope);
        }
        return null;
    }
    public async Task<bool> TryReplaceAsync(IdentityMembershipOutboxEntry entry, string expectedVersion, CancellationToken ct = default)
    {
        var scope = entry.Event.Scope; Require(scope);
        if (string.IsNullOrWhiteSpace(expectedVersion) || expectedVersion == "*" || expectedVersion.Length > 256 || expectedVersion.Any(char.IsControl)) throw Invalid();
        var row = await Table.GetEntityIfExistsAsync<TableEntity>(Partition(scope), IdentityMembershipEventPartition.EventKey(entry.Event.Sequence), cancellationToken: ct);
        if (!row.HasValue || row.Value.ETag.ToString() != expectedVersion) return false;
        var original = Parse(row.Value, scope);
        if (JsonSerializer.Serialize(original.Event) != JsonSerializer.Serialize(entry.Event)) throw new IdentityConcurrencyException();
        ValidateDispatch(entry);
        var next = row.Value; next["State"] = entry.State; next["Attempts"] = entry.Attempts; next["NextAttemptUtc"] = entry.NextAttemptUtc;
        next.Remove("LeaseId"); next.Remove("LeaseExpiresUtc"); next.Remove("FailureCode");
        if (entry.LeaseId.HasValue) next["LeaseId"] = entry.LeaseId.Value;
        if (entry.LeaseExpiresUtc.HasValue) next["LeaseExpiresUtc"] = entry.LeaseExpiresUtc.Value;
        if (entry.FailureCode is not null) next["FailureCode"] = entry.FailureCode;
        try { await IdentityTenantPurgePartition.Update(Table, next, new ETag(expectedVersion), TableUpdateMode.Replace, Options.RequireTenantPurgeFencing, ct); return true; }
        catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412) { return false; }
    }
    public async Task<IdentityMembershipPendingPage> ListPendingScopesAsync(int maximumScopes, string? continuation, CancellationToken ct = default)
    {
        if (maximumScopes is < 1 or > 100 || continuation?.Length > 65536 || continuation?.Any(char.IsControl) == true) throw new IdentityValidationException("Pending event page bounds are invalid.");
        _ = IdentityMembershipEventPartition.Scope(Options, Guid.NewGuid());
        var lower = IdentityMembershipEventPartition.Prefix; var upper = lower + '\uffff'; var deployment = IdentityMembershipEventPartition.DeploymentHash(Options);
        var filter = TableClient.CreateQueryFilter($"RowKey ge {lower} and RowKey lt {upper} and Deployment eq {deployment} and (State eq {"pending"} or State eq {"leased"})");
        await foreach (var page in Table.QueryAsync<TableEntity>(filter, maximumScopes, ["PartitionKey", "RowKey", "TenantId", "Deployment", "Binding"], ct).AsPages(continuation, maximumScopes)) {
            var scopes = page.Values.Select(row => {
                if (!Guid.TryParseExact(row.GetString("TenantId"), "D", out var tenant) || tenant == Guid.Empty) throw Invalid();
                var scope = IdentityMembershipEventPartition.Scope(Options, tenant);
                if (row.PartitionKey != Partition(scope) || row.GetString("Binding") != IdentityMembershipEventPartition.ScopeHash(scope)) throw Invalid(); return scope;
            }).Distinct().ToArray();
            return new(Array.AsReadOnly(scopes), page.ContinuationToken);
        }
        return new([], null);
    }
    public async Task RequireMutableTenantAsync(IdentityMembershipEventScope scope, CancellationToken ct = default)
    {
        Require(scope);
        var metadata = storage.GetTableClient(Options.FullTableName(Options.TenantsTableName));
        await IdentityTenantPurgePartition.RequireMutable(metadata, scope.TenantId, TenantEntity.TenantsPartitionKey, ct);
        await IdentityTenantPurgePartition.RequireMutable(Table, scope.TenantId, Partition(scope), ct);
        var guard = await Table.GetEntityIfExistsAsync<TableEntity>(Partition(scope), TenantMembershipPartition.GuardKey, cancellationToken: ct);
        if (!guard.HasValue || guard.Value.GetInt32(IdentityMembershipEventPartition.Protocol) != 1 ||
            guard.Value.GetString(IdentityMembershipEventPartition.Binding) != IdentityMembershipEventPartition.ScopeHash(scope)) throw Invalid();
    }
    private void Require(IdentityMembershipEventScope scope)
    { if (scope is null || scope != IdentityMembershipEventPartition.Scope(Options, scope.TenantId)) throw Invalid(); }
    private string Partition(IdentityMembershipEventScope scope) => Options.TenantUsersPk(scope.TenantId);
    private IdentityMembershipOutboxEntry Parse(TableEntity row, IdentityMembershipEventScope scope)
    {
        try {
            Require(scope); var payload = row.GetString("EventJson");
            if (payload is null || Encoding.UTF8.GetByteCount(payload) > 16384) throw Invalid();
            var value = JsonSerializer.Deserialize<TenantMembershipChangedEvent>(payload) ?? throw Invalid();
            var canonical = IdentityTenantMembershipEventProtocol.ProjectCommitted(new(IdentityMembershipCommitDisposition.Committed, value.EventId, value.Scope, value.OperationId,
                value.Sequence, value.OccurredUtc, value.Actor, value.Changes, value.ProtectedOwnerRoleIds));
            if (canonical is null || value.ContractVersion != 1 || value.Kind != canonical.Kind || value.Scope != scope || row.PartitionKey != Partition(scope) ||
                row.RowKey != IdentityMembershipEventPartition.EventKey(value.Sequence) || row.GetString("Binding") != IdentityMembershipEventPartition.ScopeHash(scope) ||
                row.GetGuid("EventId") != value.EventId || row.GetGuid("OperationId") != value.OperationId || row.GetInt64("Sequence") != value.Sequence) throw Invalid();
            var entry = new IdentityMembershipOutboxEntry(canonical, row.GetString("State") ?? "", row.GetInt32("Attempts") ?? -1,
                row.GetDateTimeOffset("NextAttemptUtc") ?? DateTimeOffset.MinValue, row.GetGuid("LeaseId"), row.GetDateTimeOffset("LeaseExpiresUtc"), row.GetString("FailureCode")) { Version = row.ETag.ToString() };
            ValidateDispatch(entry); return entry;
        } catch (OperationCanceledException) { throw; }
        catch { throw Invalid(); }
    }
    private static void ValidateDispatch(IdentityMembershipOutboxEntry entry)
    {
        if (entry.State is not ("pending" or "leased" or "acked" or "dead-letter") || entry.Attempts is < 0 or > 1000 || entry.NextAttemptUtc.Offset != TimeSpan.Zero || entry.NextAttemptUtc < DateTimeOffset.UnixEpoch ||
            (entry.State == "leased" ? entry.LeaseId is null || entry.LeaseId == Guid.Empty || entry.LeaseExpiresUtc is null || entry.LeaseExpiresUtc.Value.Offset != TimeSpan.Zero : entry.LeaseId is not null || entry.LeaseExpiresUtc is not null) ||
            entry.FailureCode is not (null or "consumer-unavailable" or "timeout" or "cancelled" or "unknown")) throw Invalid();
    }
    private static IdentityProviderException Invalid() => new("Membership outbox metadata or scope is invalid.");
}
