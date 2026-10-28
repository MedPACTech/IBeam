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
authentication endpoint, grant or publication callback. The contract registration alone makes no live emission or durable-delivery claim. IBM-0140 supplies the optional native atomic persistence/dispatcher described below; consumer projection/reconciliation is IBM-0141. Management stays outside the native Core dependency graph.

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


## Native atomic outbox (IBM-0140)

This is a separate, explicit opt-in on the **consumer target**, not on the support identity host.
Upgrade every native membership/role writer before enrolling any tenant. Configure the native
Azure Table provider with `CreateTablesIfNotExists=false`, `RequireMembershipLifecycleEvents=true`,
`MembershipEventsApplicationKey`, `MembershipEventsEnvironment`, and `MembershipEventsRealmId`.
The configured realm/application/environment must describe the same target represented by its
Management delegation configuration. Use canonical native TEN|/USR| keys and distinct pre-provisioned
Tenants, TenantUsers, UserTenants and role tables. The framework creates no new table at runtime.
Normalize legacy role-name-only memberships to authoritative role IDs before opting in; unsupported
metadata fails closed. The default remains disabled and old native/diagnostics consumers are unchanged.

Register `services.AddIBeamIdentityAzureTableMembershipEvents()` with the existing TableServiceClient
and native options. Provide an explicit `IIdentityMembershipEventDelivery` implementation (protocol 1),
then call `services.AddIBeamIdentityMembershipEventDispatch(o => { o.Enabled = true; ... });` with normal
Identity services/IServiceOperationExecutor. Missing/unsupported storage or receiver fails startup.
A default NoOpAuthEventPublisher is deliberately insufficient. A bus adapter must await durable
acceptance; a projection adapter must deduplicate and commit its projection/checkpoint together.
The target-local consumer/reconciliation helper follows in IBM-0141.

The native provider appends reserved `!IBEAM|MEMBERSHIP|EVENT|{sequence}` and operation-index rows in
the existing TenantUsers tenant partition. The membership(s), shared revision/sequence guard,
immutable event and operation index commit in the **same** native Table transaction, also joining
existing purge write fences. An ownership transfer includes both membership rows and its existing
transfer receipt in that transaction. A crash after commit cannot lose event intent. Replays reuse
the original event; unchanged status/role sets and role-name-only display changes create no new
logical event. Other projections such as UserTenants remain separately repairable and cannot turn
a failed/unknown request into an invented event.

The guard stores a sticky protocol and full deployment/tenant binding. Current writers configured
without event participation cannot modify an enrolled tenant, and another realm/app cannot take
it over. Ordinary owner/administrator protections stay active. Native legacy/bootstrap/additive,
versioned status/role and atomic ownership paths share the same persistence helper; this never
replaces their caller authorization or eligibility checks. Event preparation is private transaction
metadata, not publication: the service projector remains a committed-output formatter.

A logical revision GUID and event sequence are stored on each changed authoritative membership.
Existing rows get a stable identifier derived from their opaque native version on enrollment;
version-only writes preserve that logical revision. Existing HTTP ETags and customer access-token
fencing remain unchanged. Transfer events carry the protected owner-role definitions, but transfer
only roles actually held by the source. The original actor/time/event/operation/sequence and event
payload stay immutable during lease/acknowledgement changes.

Native API actor capture reads only the already-authenticated principal, never target user IDs or
actor fields in a request body. Customer actors carry the unique native user GUID; API credentials,
agents and service subjects are System, and unavailable/ambiguous identities are Unknown. Custom
server flows may use `IdentityMembershipEventActorContext.Push` around an already-authorized native
call; it is a trusted metadata scope and grants no authority. Management membership/ownership services
capture the verified delegated support actor after current access checks. Dispatch and retries retain
that original attribution; they do not impersonate it or require it to remain logged in.

## Dispatch, ordering and recovery

`IIdentityMembershipEventDispatcher.RunAsync(scope, maximumEvents)` is a trusted target-local port,
not an HTTP support endpoint. Bounds are 1-100 events per call. Each tenant's earliest unacknowledged
event blocks later events while leased, delayed or dead-lettered. Leases use exact ETags; duplicate
workers cannot acquire the same active lease, expired workers cannot acknowledge after takeover,
and only dispatch metadata changes. Receiver acknowledgement never repeats a membership mutation.
A crash after receiver acceptance may redeliver the **same event ID**. This is at-least-once delivery;
receivers must deduplicate and refuse older tenant sequences before applying effects.

Options: LeaseSeconds 10-120 (default 45), DeliveryTimeoutSeconds 1-30 (default 20; lease must exceed
it by at least 5 seconds), MaximumAttempts 1-20 (default 5), RetryDelaySeconds 1-60 (default 5), and
ScanIntervalSeconds 1-300 (default 10). Timeouts bound even an uncooperative receiver; that receiver
may still finish its original attempt and therefore must check its own durable event ID/sequence
at commit. Failures/cancellation leave the original event pending with bounded backoff or dead-letter
it at the attempt limit. Failure classes are fixed metadata, never raw provider messages.

An operator inspects the original earliest dead-letter event, repairs its native receiver/provider,
and calls `RetryDeadLetterAsync` with that same scoped event ID and exact dispatch version. It resets
only dispatch attempts; it cannot replace the event, actor, sequence or membership. Do not delete
outbox rows, generate another mutation, or restore tenant access to retry delivery. Successful delivery
is an acknowledged event, not a new business authorization. Event/index history is retained; there is
no automatic pruning that could silently erase replay/deduplication evidence.

The optional background worker (`EnableBackgroundWorker=true`) scans one bounded metadata page of
at most ten pending scopes per tick under a 55-second budget. Cursors remain target-local; empty pages
may still have continuation. It rotates through configured deployment rows and never scans profile
columns. Larger hosts may use their own durable queue/recovery scheduler against the same dispatcher.
Host disablement or scope/provider loss pauses work. Support assignment revocation does not undo an
already committed native change: its event must still propagate using the original metadata.

Purge is a separate accepted lifecycle workflow: its permanent native fences prevent new event writes
or delivery/acknowledgement updates, and tenant-owned outbox rows are included in declared Identity
cleanup. No ordinary role event is emitted for purge's internal access fences. A projection receiver
must participate in its application's purge/write fence and current native access policy; delayed or
replayed events cannot recreate deleted app data or grant access by themselves. Global accounts and
other tenants remain unaffected.
