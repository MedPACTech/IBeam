using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;

namespace IBeam.Management.SyntheticConsumer;

/// <summary>Disabled example: only explicitly seeded synthetic tenants in an existing private emulator table.</summary>
public sealed class SyntheticMembershipProjectionStore : IIdentityMembershipProjectionStore, IIdentityMembershipProjectionWriteFence
{
    private readonly TableClient table; private readonly string environment; private readonly Guid realm; private readonly HashSet<Guid> tenants;
    public int ProtocolVersion => 1; public bool Durable => true;
    public Action? AfterCommitForTesting { get; set; }
    public SyntheticMembershipProjectionStore(TableClient privateTable, string environment, Guid realm, IReadOnlyCollection<Guid> syntheticTenants)
    {
        if (environment is not ("Development" or "Testing") || !privateTable.Uri.IsLoopback || !privateTable.Uri.AbsolutePath.StartsWith("/devstoreaccount1/", StringComparison.Ordinal) ||
            realm == Guid.Empty || syntheticTenants is not { Count: > 0 and <= 32 } || syntheticTenants.Any(x => x == Guid.Empty))
            throw new InvalidOperationException("The synthetic projection requires a private Development/Testing fixture and explicit synthetic tenants.");
        table = privateTable; this.environment = environment; this.realm = realm; tenants = syntheticTenants.ToHashSet();
    }
    public async Task SeedAsync(IdentityMembershipEventScope scope, CancellationToken ct = default)
    {
        Require(scope); var row = new TableEntity(Partition(scope), "META") { ["Synthetic"] = true, ["Scope"] = Hash(scope), ["Blocked"] = false, ["Revision"] = Guid.NewGuid().ToString("D") };
        try { await table.AddEntityAsync(row, ct); } catch (RequestFailedException ex) when (ex.Status == 409) { _ = await Control(scope, allowBlocked: true, ct); }
    }
    public async Task<string> GetVersionAsync(IdentityMembershipEventScope scope, CancellationToken ct = default) => (await Control(scope, false, ct)).GetString("Revision")!;
    public async Task RequireMutableAsync(IdentityMembershipEventScope scope, CancellationToken ct = default) => _ = await Control(scope, false, ct);
    public async Task<IdentityMembershipProjectionReceipt?> FindAsync(IdentityMembershipProjectionIntent intent, CancellationToken ct = default)
    {
        _ = await Control(intent.Scope, false, ct);
        var row = await table.GetEntityIfExistsAsync<TableEntity>(Partition(intent.Scope), ReceiptKey(intent), cancellationToken: ct);
        if (!row.HasValue) return null;
        if (row.Value.GetString("Actor") != JsonSerializer.Serialize(intent.Actor) || intent.IntentHash.Length != 0 && row.Value.GetString("IntentHash") != intent.IntentHash)
            throw new IdentityConcurrencyException();
        var receipt = JsonSerializer.Deserialize<IdentityMembershipProjectionReceipt>(row.Value.GetString("Receipt") ?? "") ?? throw new IdentityProviderException("Synthetic projection receipt is invalid.");
        if (receipt.Scope != intent.Scope || receipt.Id != intent.Id || receipt.Kind != intent.Kind) throw new IdentityProviderException("Synthetic projection receipt scope is invalid.");
        return receipt;
    }
    public async Task<IdentityMembershipProjectionReceipt> ApplyAsync(IdentityMembershipProjectionIntent intent, IdentityMembershipProjectionSnapshot snapshot,
        string expectedVersion, IIdentityMembershipProjectionCommitFence fence, CancellationToken ct = default)
    {
        Require(intent.Scope);
        if (snapshot.Scope != intent.Scope || snapshot.Members.Count is < 1 or > 96 || intent.IntentHash is not { Length: 64 }) throw new IdentityValidationException("Synthetic projection intent is invalid.");
        var prior = await FindAsync(intent, ct); if (prior is not null) return prior;
        var control = await Control(intent.Scope, false, ct);
        if (control.GetString("Revision") != expectedVersion) throw new IdentityConcurrencyException();
        var changes = new List<TableTransactionAction>(); var applied = 0; var skipped = 0; var pk = Partition(intent.Scope);
        foreach (var member in snapshot.Members) {
            var key = "M|" + member.UserId.ToString("D"); var old = await table.GetEntityIfExistsAsync<TableEntity>(pk, key, cancellationToken: ct);
            var revision = member.State?.Revision ?? Guid.Empty; var enabled = member.State?.IsEnabled ?? false;
            var roles = string.Join(',', member.State?.RoleIds.Order().Select(x => x.ToString("D")) ?? []);
            var oldSequence = old.HasValue ? old.Value.GetInt64("NativeSequence") ?? -1 : -1;
            if (oldSequence > member.Sequence) { skipped++; continue; }
            if (oldSequence == member.Sequence) {
                if (old.Value.GetGuid("NativeRevision") != revision) throw new IdentityConcurrencyException();
                if (old.Value.GetBoolean("NativeEnabled") == enabled && old.Value.GetString("NativeRoleIds") == roles) { skipped++; continue; }
            }
            var row = new TableEntity(pk, key) { ["NativeUserId"] = member.UserId, ["NativeSequence"] = member.Sequence, ["NativeRevision"] = revision,
                ["NativeEnabled"] = enabled, ["NativeRoleIds"] = roles, ["NativeMissing"] = member.State is null };
            if (!old.HasValue) row["BusinessCanary"] = "PRIVATE-SYNTHETIC-APP-DATA";
            changes.Add(new(old.HasValue ? TableTransactionActionType.UpdateMerge : TableTransactionActionType.Add, row, old.HasValue ? old.Value.ETag : default)); applied++;
        }
        var version = Guid.NewGuid().ToString("D"); control["Revision"] = version;
        var receipt = new IdentityMembershipProjectionReceipt(intent.Id, intent.Kind, intent.Scope with { }, version, applied, skipped);
        changes.Add(new(TableTransactionActionType.UpdateReplace, control, control.ETag));
        changes.Add(new(TableTransactionActionType.Add, new TableEntity(pk, ReceiptKey(intent)) { ["Actor"] = JsonSerializer.Serialize(intent.Actor), ["IntentHash"] = intent.IntentHash, ["Receipt"] = JsonSerializer.Serialize(receipt) }));
        await fence.RequireAsync(ct); // The META ETag in this same transaction is also this app's durable purge/write guard.
        try { await table.SubmitTransactionAsync(changes, ct); }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412) { return await FindAsync(intent, ct) ?? throw new IdentityConcurrencyException(); }
        AfterCommitForTesting?.Invoke(); return receipt;
    }
    public async Task BlockForPurgeAsync(IdentityMembershipEventScope scope, CancellationToken ct = default)
    {
        var row = await Control(scope, true, ct); row["Blocked"] = true;
        await table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, ct);
    }
    public async Task DeleteSyntheticProjectionRowsAsync(IdentityMembershipEventScope scope, CancellationToken ct = default)
    {
        var pk = Partition(scope);
        await foreach (var row in table.QueryAsync<TableEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {pk} and RowKey ge {"M|"} and RowKey lt {"M|\uffff"}"), cancellationToken: ct))
        {
            var control = await Control(scope, true, ct); if (control.GetBoolean("Blocked") != true) throw new IdentityConcurrencyException();
            await table.SubmitTransactionAsync([new(TableTransactionActionType.UpdateReplace, control, control.ETag), new(TableTransactionActionType.Delete, row, row.ETag)], ct);
        }
    }
    private async Task<TableEntity> Control(IdentityMembershipEventScope scope, bool allowBlocked, CancellationToken ct)
    {
        Require(scope); var row = (await table.GetEntityAsync<TableEntity>(Partition(scope), "META", cancellationToken: ct)).Value;
        if (row.GetBoolean("Synthetic") != true || row.GetString("Scope") != Hash(scope) || !allowBlocked && row.GetBoolean("Blocked") == true ||
            !Guid.TryParseExact(row.GetString("Revision"), "D", out _)) throw new IdentityConcurrencyException(); return row;
    }
    private void Require(IdentityMembershipEventScope scope)
    { if (scope.ApplicationKey != "fixture" || scope.Environment != environment || scope.RealmId != realm || !tenants.Contains(scope.TenantId)) throw new IdentityValidationException("Synthetic projection scope is invalid."); }
    private static string ReceiptKey(IdentityMembershipProjectionIntent intent) => intent.Id != Guid.Empty && intent.Kind is "event" or "repair" ? "R|" + intent.Kind + "|" + intent.Id.ToString("D") : throw new IdentityValidationException("Projection intent is invalid.");
    private static string Hash(IdentityMembershipEventScope scope) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(scope)));
    public static string Partition(IdentityMembershipEventScope scope) => "P|" + Hash(scope);
}
