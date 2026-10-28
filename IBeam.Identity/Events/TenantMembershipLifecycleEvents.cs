using System.Text.Json.Serialization;

namespace IBeam.Identity.Events;

public enum IdentityMembershipCommitDisposition { Committed = 0, Replayed = 1, NoChange = 2, Rejected = 3 }
[JsonConverter(typeof(JsonStringEnumConverter<IdentityMembershipActorKind>))]
public enum IdentityMembershipActorKind { Unknown = 0, Customer = 1, Support = 2, System = 3 }

/// <summary>Trusted deployment identity, never taken from a role-change HTTP body.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentityMembershipEventScope(string ApplicationKey, string Environment, Guid RealmId, Guid TenantId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentityMembershipEventActor(IdentityMembershipActorKind Kind, Guid? UserId, Guid? ManagementOrganizationId,
    Guid? ManagementApplicationId, Guid? CorrelationId);
/// <summary>Logical membership revision, not an HTTP ETag, password/session version or lexically sortable value.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentityMembershipEventState(Guid Revision, bool IsEnabled, IReadOnlyList<Guid> RoleIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentityMembershipEventChange(Guid UserId, IdentityMembershipEventState? Before, IdentityMembershipEventState? After);

/// <summary>Metadata only. This deliberately does not inherit unrestricted invite-event Metadata or destination fields.
/// A transfer contains both committed memberships in one event. Knowing an event grants no mutation authority.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantMembershipChangedEvent(int ContractVersion, Guid EventId, IdentityMembershipEventScope Scope,
    Guid OperationId, long Sequence, DateTimeOffset OccurredUtc, string Kind, IdentityMembershipEventActor Actor,
    IReadOnlyList<IdentityMembershipEventChange> Changes, IReadOnlyList<Guid> ProtectedOwnerRoleIds)
{
    public const string TypeName = "identity.tenant.membership.changed.v1";
    public string EventType => TypeName;
    public override string ToString() => TypeName;
}

/// <summary>Trusted native provider output. Only a committed transaction may supply Committed and its original
/// event/operation IDs, tenant sequence, revisions, timestamp and actor. Replays return no new logical event.
/// This is not an authorization proof, public request DTO or a substitute for atomic provider persistence.</summary>
public sealed record IdentityMembershipCommittedChange(IdentityMembershipCommitDisposition Disposition, Guid EventId,
    IdentityMembershipEventScope Scope, Guid OperationId, long Sequence, DateTimeOffset OccurredUtc,
    IdentityMembershipEventActor Actor, IReadOnlyList<IdentityMembershipEventChange> Changes, IReadOnlyList<Guid> ProtectedOwnerRoleIds);
public sealed record IdentityMembershipEventPage(IReadOnlyList<TenantMembershipChangedEvent> Events, long NextSequence, bool HasMore);
