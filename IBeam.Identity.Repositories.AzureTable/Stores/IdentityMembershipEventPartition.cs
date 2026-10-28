using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

/// <summary>Native persistence schema only. Event intent joins the same membership/guard/purge-fence transaction.</summary>
internal static class IdentityMembershipEventPartition
{
    internal const string Prefix = "!IBEAM|MEMBERSHIP|EVENT|", OperationPrefix = "!IBEAM|MEMBERSHIP|OP|";
    internal const string Protocol = "MembershipEventProtocol", Binding = "MembershipEventBinding", Sequence = "MembershipEventSequence";
    internal const string Revision = "IBeamMembershipLifecycleRevision", MemberSequence = "IBeamMembershipLifecycleSequence";
    internal static string EventKey(long sequence) => Prefix + sequence.ToString("D20", System.Globalization.CultureInfo.InvariantCulture);
    internal static IdentityMembershipEventScope Scope(AzureTableIdentityOptions options, Guid tenant)
    {
        var result = new IdentityMembershipEventScope(options.MembershipEventsApplicationKey, options.MembershipEventsEnvironment, options.MembershipEventsRealmId, tenant);
        if (!options.RequireMembershipLifecycleEvents || !Key(result.ApplicationKey) || !Key(result.Environment) || result.RealmId == Guid.Empty || tenant == Guid.Empty)
            throw new IdentityProviderException("Explicit configured membership event scope is required.");
        return result;
    }
    internal static string ScopeHash(IdentityMembershipEventScope scope) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(scope)));
    internal static string DeploymentHash(AzureTableIdentityOptions options) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
        options.MembershipEventsApplicationKey, options.MembershipEventsEnvironment, options.MembershipEventsRealmId })));
    private static bool Key(string value) => value is { Length: > 0 and <= 100 } && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.');
    internal static async Task AppendAsync(TableClient table, List<TableTransactionAction> actions, TableEntity guard,
        AzureTableIdentityOptions? options, Guid? operation, IReadOnlyList<Guid>? ownerRoles, CancellationToken ct)
    {
        var enrolled = guard.GetInt32(Protocol) ?? 0;
        if (options is not { RequireMembershipLifecycleEvents: true }) {
            if (enrolled != 0) throw new IdentityProviderException("This membership writer must participate in the enrolled event protocol.");
            return;
        }
        if (enrolled is not (0 or 1) || !guard.PartitionKey.StartsWith("TEN|", StringComparison.Ordinal) ||
            !Guid.TryParseExact(guard.PartitionKey[4..], "D", out var tenant)) throw new IdentityProviderException("Membership event enrollment is invalid.");
        var scope = Scope(options, tenant); var hash = ScopeHash(scope);
        if (enrolled == 1 && guard.GetString(Binding) != hash) throw new IdentityProviderException("Membership event deployment binding does not match.");
        var sequence = guard.GetInt64(Sequence) ?? 0;
        if (sequence < 0 || sequence == long.MaxValue) throw new IdentityProviderException("Membership event sequence is unavailable.");
        var changes = new List<IdentityMembershipEventChange>();
        foreach (var action in actions.Where(x => x.Entity.RowKey.StartsWith("USR|", StringComparison.Ordinal))) {
            var row = action.Entity;
            if (row.PartitionKey != guard.PartitionKey || !Guid.TryParseExact(row.RowKey[4..], "D", out var user) || user == Guid.Empty ||
                action.ActionType is not (TableTransactionActionType.Add or TableTransactionActionType.UpdateMerge or TableTransactionActionType.UpdateReplace))
                throw new IdentityProviderException("Membership event mutation is unsupported.");
            var stored = await table.GetEntityIfExistsAsync<TableEntity>(row.PartitionKey, row.RowKey,
                ["Status", "RoleIdsCsv", "RolesCsv", Revision, MemberSequence], ct);
            if (action.ActionType == TableTransactionActionType.Add ? stored.HasValue : !stored.HasValue || row.ETag != stored.Value.ETag)
                throw new IdentityConcurrencyException();
            var before = stored.HasValue ? State(stored.Value, tenant, user) : null;
            var roles = Text(row, "RoleIdsCsv") ?? (stored.HasValue ? stored.Value.GetString("RoleIdsCsv") : "");
            var names = Text(row, "RolesCsv") ?? (stored.HasValue ? stored.Value.GetString("RolesCsv") : "");
            var status = Text(row, "Status") ?? (stored.HasValue ? stored.Value.GetString("Status") : "Active");
            if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) && !string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase)) throw new IdentityProviderException("Membership event state is invalid.");
            var ids = Ids(roles, names);
            var changed = before is null || before.IsEnabled != (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)) || !before.RoleIds.SequenceEqual(ids);
            var revision = changed ? Guid.NewGuid() : before!.Revision;
            Set(row, revision, changed ? sequence + 1 : stored.Value.GetInt64(MemberSequence) ?? 0);
            changes.Add(new(user, before, new(revision, string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase), Array.AsReadOnly(ids))));
            if (operation is null && Guid.TryParse(Text(row, "IBeamLastMembershipOperationId"), out var id) && id != Guid.Empty) operation = id;
        }
        if (changes.Count is < 1 or > 2) throw new IdentityProviderException("Membership event mutation count is invalid.");
        var now = DateTimeOffset.UtcNow; var op = operation ?? Guid.NewGuid();
        // Candidate bytes are private until these rows and the membership commit together. No callback runs here.
        var candidate = IdentityTenantMembershipEventProtocol.ProjectCommitted(new(IdentityMembershipCommitDisposition.Committed, Guid.NewGuid(), scope,
            op, sequence + 1, now, IdentityMembershipEventActorContext.Current, changes, ownerRoles ?? []));
        guard[Protocol] = 1; guard[Binding] = hash; guard[Sequence] = candidate is null ? sequence : sequence + 1;
        if (candidate is null) return;
        var payload = JsonSerializer.Serialize(candidate);
        if (Encoding.UTF8.GetByteCount(payload) > 16384) throw new IdentityProviderException("Membership event exceeds its metadata bound.");
        actions.Add(new(TableTransactionActionType.Add, new TableEntity(guard.PartitionKey, EventKey(candidate.Sequence)) {
            ["TenantId"] = tenant.ToString("D"), ["Binding"] = hash, ["Deployment"] = DeploymentHash(options), ["EventId"] = candidate.EventId, ["OperationId"] = op,
            ["Sequence"] = candidate.Sequence, ["EventJson"] = payload, ["State"] = "pending", ["Attempts"] = 0, ["NextAttemptUtc"] = now }));
        actions.Add(new(TableTransactionActionType.Add, new TableEntity(guard.PartitionKey, OperationPrefix + op.ToString("D")) {
            ["TenantId"] = tenant.ToString("D"), ["Binding"] = hash, ["EventId"] = candidate.EventId, ["Sequence"] = candidate.Sequence }));
    }
    internal static IdentityMembershipEventState State(TableEntity row, Guid tenant, Guid user)
    {
        var revision = row.GetGuid(Revision) ?? new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(tenant.ToString("D") + user.ToString("D") + row.ETag)).AsSpan(0, 16));
        var status = row.GetString("Status");
        if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) && !string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase)) throw new IdentityProviderException("Membership event state is invalid.");
        return new(revision, string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase), Array.AsReadOnly(Ids(row.GetString("RoleIdsCsv"), row.GetString("RolesCsv"))));
    }
    private static Guid[] Ids(string? value, string? names)
    {
        var tokens = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 && !string.IsNullOrWhiteSpace(names) || tokens.Length > 32 || tokens.Any(x => !Guid.TryParseExact(x, "D", out var id) || id == Guid.Empty))
            throw new IdentityProviderException("Canonical native role IDs are required before event enrollment.");
        var ids = tokens.Select(Guid.Parse).ToArray(); if (ids.Distinct().Count() != ids.Length) throw new IdentityProviderException("Native membership roles are invalid.");
        return ids.Order().ToArray();
    }
    private static string? Text(ITableEntity entity, string key) => entity switch {
        TableEntity row => row.GetString(key), TenantUserEntity row => key switch { "Status" => row.Status, "RoleIdsCsv" => row.RoleIdsCsv,
            "RolesCsv" => row.RolesCsv, "IBeamLastMembershipOperationId" => row.IBeamLastMembershipOperationId, _ => null }, _ => null };
    private static void Set(ITableEntity entity, Guid revision, long sequence)
    {
        if (entity is TableEntity row) { row[Revision] = revision; row[MemberSequence] = sequence; }
        else if (entity is TenantUserEntity member) { member.IBeamMembershipLifecycleRevision = revision; member.IBeamMembershipLifecycleSequence = sequence; }
        else throw new IdentityProviderException("Membership event entity is unsupported.");
    }
}
