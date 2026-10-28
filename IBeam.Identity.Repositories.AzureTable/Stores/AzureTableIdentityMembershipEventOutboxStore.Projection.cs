using Azure.Data.Tables;
using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed partial class AzureTableIdentityMembershipEventOutboxStore : IIdentityMembershipProjectionSource
{
    public Task RequireScopeAsync(IdentityMembershipEventScope scope, CancellationToken ct = default) => RequireMutableTenantAsync(scope, ct);
    public async Task<IdentityMembershipProjectionSnapshot> ReadCurrentAsync(IdentityMembershipEventScope scope, IReadOnlyList<Guid> userIds, CancellationToken ct = default)
    {
        Require(scope);
        if (userIds is not { Count: > 0 and <= 96 } || userIds.Any(x => x == Guid.Empty) || userIds.Distinct().Count() != userIds.Count)
            throw new IdentityValidationException("A bounded distinct native member selection is required.");
        await RequireMutableTenantAsync(scope, ct);
        var root = (await Table.GetEntityAsync<TableEntity>(Partition(scope), TenantMembershipPartition.GuardKey, cancellationToken: ct)).Value;
        var sequence = root.GetInt64(IdentityMembershipEventPartition.Sequence) ?? throw Invalid();
        using var parallel = new SemaphoreSlim(8);
        var members = await Task.WhenAll(userIds.Select(async user => {
            await parallel.WaitAsync(ct);
            try {
                var row = await Table.GetEntityIfExistsAsync<TableEntity>(Partition(scope), Options.TenantUsersRk(user.ToString("D")),
                    ["Status", "RoleIdsCsv", "RolesCsv", IdentityMembershipEventPartition.Revision, IdentityMembershipEventPartition.MemberSequence], ct);
                var memberSequence = row.HasValue ? row.Value.GetInt64(IdentityMembershipEventPartition.MemberSequence) ?? 0 : sequence;
                if (memberSequence < 0 || memberSequence > sequence) throw Invalid();
                return new IdentityMembershipProjectionMember(user, row.HasValue ? IdentityMembershipEventPartition.State(row.Value, scope.TenantId, user) : null, memberSequence);
            } finally { parallel.Release(); }
        }));
        var snapshot = new IdentityMembershipProjectionSnapshot(scope with { }, root.ETag.ToString(), sequence, Array.AsReadOnly(members.OrderBy(x => x.UserId).ToArray()));
        await RequireCurrentAsync(snapshot, ct); return snapshot;
    }
    public async Task RequireCurrentAsync(IdentityMembershipProjectionSnapshot snapshot, CancellationToken ct = default)
    {
        Require(snapshot.Scope); await RequireMutableTenantAsync(snapshot.Scope, ct);
        var root = await Table.GetEntityAsync<TableEntity>(Partition(snapshot.Scope), TenantMembershipPartition.GuardKey, cancellationToken: ct);
        if (root.Value.ETag.ToString() != snapshot.NativeVersion || root.Value.GetInt64(IdentityMembershipEventPartition.Sequence) != snapshot.Sequence)
            throw new IdentityConcurrencyException();
    }
}
