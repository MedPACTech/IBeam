# Membership lifecycle events

## Contract milestone (IBM-0139)

`IBeam.Identity.Events.TenantMembershipChangedEvent` is a separate version-1 metadata contract for
native and support-origin membership/role changes. It does not inherit `AuthLifecycleEventBase`:
there is no arbitrary Metadata bag, destination, email, role name, profile, credential, OTP,
provider error, HTTP header or customer business payload. `ToString()` returns only the fixed
event type `identity.tenant.membership.changed.v1`. Unknown JSON fields are rejected.

`IdentityTenantMembershipEventFactory` in Identity.Services supplies the shared strict projector
through `IIdentityTenantMembershipEventFactory.ProjectAsync`. It uses IServiceOperationExecutor
with payload audit disabled. `AddIBeamIdentityMembershipEventContracts()` registers only this
projector with normal Identity services; it installs no emitter, persistence provider, dispatcher,
authentication endpoint, grant or publication callback. Native atomic persistence and dispatch are
IBM-0140, and consumer projection/reconciliation is IBM-0141. This milestone alone makes no live
emission or durable-delivery claim. Management stays outside the native Core dependency graph.

## Native provider boundary

`IdentityMembershipCommittedChange` is trusted server output from a native provider, never a
public command or authorization proof. The provider supplies the original immutable event ID,
operation ID, configured application/environment/realm/tenant, committed per-tenant sequence,
UTC commit timestamp, trusted actor and before/after metadata. Project only after authoritative
commit. `Rejected`, `NoChange` and `Replayed` dispositions produce no new logical event; role
ordering or a version-only timestamp/projection write is not a logical change either. A failed
or uncertain native write cannot be labeled Committed from an exception or attempted request.

`IIdentityTenantMembershipCommittedEventReader` protocol 1 requires durable membership/fence/event
intent under the same native transaction, immutable original event metadata and bounded read-only
pages. Unsupported adapters must advertise protocol 0 / Durable=false and cannot claim delivery.
Find-by-operation returns the original committed event, not a new event attributed to the current
caller. Read-page input must bind the configured deployment and tenant, afterSequence >= 0 and
maximumEvents 1-100; output must be ordered, bounded and scoped. Reads cannot initialize metadata,
repair projections, dispatch callbacks or mutate memberships. Provider enrollment must cover every
native writer; a post-commit service callback alone cannot satisfy the atomic promise.

## Shape and versions

One event contains one logical commit. Single-membership kinds are `membership-linked`,
`membership-unlinked`, `membership-enabled`, `membership-disabled`, `roles-granted`, `roles-revoked`,
`roles-replaced` or combined `membership-changed`. Each change has a native user ID and nullable
before/after snapshots containing enabled state, bounded distinct role IDs and a logical revision
GUID. At least one snapshot exists. Different logical states cannot share a revision. Role IDs
are canonicalized and buffers defensively frozen before handing the event to another component.

Logical revision GUIDs identify committed membership states; they are not HTTP If-Match ETags,
password/session versions or sortable counters. Existing HTTP concurrency tokens remain opaque
and unchanged. A provider must persist the new logical revision with the membership commit and
provide stable legacy-state identifiers when enrolling old rows. Never compare GUIDs or ETags
lexically to decide which event is newer.

Sequence is monotonic per configured application/environment/realm/tenant, assigned atomically
with the native membership revision. It is not a global ordering guarantee or permission grant.
The same event ID/operation/sequence belongs to the same immutable committed payload forever;
retries preserve the original actor and timestamp. A consumer deduplicates by full deployment,
tenant and event ID, checkpoints sequence durably and refuses older changes. Dispatch may deliver
the same event again after a lost acknowledgement: at-least-once is not exactly-once external work.
Missing history, unsupported versions or gaps require bounded native reconciliation, not guessed
role updates or an ownerless intermediate projection.

## Ownership

`ownership-transferred` is one envelope with exactly two enabled before/after memberships and
bounded protected owner-role IDs. The source loses only those owner roles; the destination gains
them while preserving its other roles. Both changes share one native transfer operation/event/
sequence. Ownerless partial snapshots, two sources, disabled destinations and unrelated role
changes are invalid. Do not split a transfer into two independently published owner events.
Native last-owner and last-administrator protections remain authoritative and unchanged.

## Actor and compatibility

Actor kinds are Customer, Support, System or Unknown. Customer requires the trusted native user
ID and no Management org/app IDs. Support requires the verified support person and originating
Management org/application IDs. System/Unknown contain no fabricated human ID. Correlation is an
optional GUID. Capture this metadata at the native commit from trusted current context; never
accept actor metadata from a role/membership request body, infer a support identity from a customer
role, or replace the stored original actor during retries/dispatch. Events grant no authorization.

Existing invitation events and IAuthEventPublisher remain unchanged, including their historical
destination/Metadata fields. Subscribe explicitly to the new fixed type and version; do not
reinterpret TenantInviteRolesAssignedEvent as a complete authoritative membership/ownership feed.
Old consumers continue receiving their existing invite events and do not gain any permission or
new destructive capability. New consumers must check version/bounds and use the native durable
source plus the optional target-local projection hooks delivered in the following cards.
