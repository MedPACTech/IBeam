using System.Text.Json.Serialization;
using IBeam.Identity.Events;

namespace IBeam.Identity.Interfaces;

public sealed record IdentityMembershipProjectionMember(Guid UserId, IdentityMembershipEventState? State, long Sequence);
public sealed record IdentityMembershipProjectionSnapshot(IdentityMembershipEventScope Scope, string NativeVersion, long Sequence,
    IReadOnlyList<IdentityMembershipProjectionMember> Members);
public interface IIdentityMembershipProjectionSource
{
    int ProtocolVersion { get; }
    Task RequireScopeAsync(IdentityMembershipEventScope scope, CancellationToken ct = default);
    Task<IdentityMembershipProjectionSnapshot> ReadCurrentAsync(IdentityMembershipEventScope scope, IReadOnlyList<Guid> userIds, CancellationToken ct = default);
    Task RequireCurrentAsync(IdentityMembershipProjectionSnapshot snapshot, CancellationToken ct = default);
}
/// <summary>App-owned access/write fence shared with its purge handler and every projection writer.</summary>
public interface IIdentityMembershipProjectionWriteFence
{
    int ProtocolVersion { get; }
    Task RequireMutableAsync(IdentityMembershipEventScope scope, CancellationToken ct = default);
}
public interface IIdentityMembershipProjectionCommitFence
{ Task RequireAsync(CancellationToken ct = default); }
/// <summary>Server-only intent. Support actor attribution is verified by the caller, never a request body.</summary>
public sealed record IdentityMembershipProjectionIntent(Guid Id, string Kind, IdentityMembershipEventScope Scope, IdentityMembershipEventActor Actor)
{ [JsonIgnore] public string IntentHash { get; init; } = ""; }
public sealed record IdentityMembershipProjectionReceipt(Guid Id, string Kind, IdentityMembershipEventScope Scope, string Version, int Applied, int Skipped);
/// <summary>App-owned entity store. Protocol 1 commits dedup receipt + the entire metadata group atomically, checks
/// expected projection version and per-subject sequence/revision, and joins its own app purge/write guard. Never
/// overwrite newer member state or repeat business side effects. Current native access remains authoritative.</summary>
public interface IIdentityMembershipProjectionStore
{
    int ProtocolVersion { get; }
    bool Durable { get; }
    Task<string> GetVersionAsync(IdentityMembershipEventScope scope, CancellationToken ct = default);
    Task<IdentityMembershipProjectionReceipt?> FindAsync(IdentityMembershipProjectionIntent intent, CancellationToken ct = default);
    Task<IdentityMembershipProjectionReceipt> ApplyAsync(IdentityMembershipProjectionIntent intent, IdentityMembershipProjectionSnapshot snapshot,
        string expectedVersion, IIdentityMembershipProjectionCommitFence fence, CancellationToken ct = default);
}
public interface IIdentityMembershipProjectionService
{
    Task<string> GetVersionAsync(IdentityMembershipEventScope scope, CancellationToken ct = default);
    Task<IdentityMembershipProjectionReceipt> RepairAsync(IdentityMembershipProjectionIntent intent, IReadOnlyList<Guid> userIds, string expectedVersion, CancellationToken ct = default);
    Task<IdentityMembershipProjectionReceipt?> FindRepairAsync(IdentityMembershipProjectionIntent intent, CancellationToken ct = default);
}
