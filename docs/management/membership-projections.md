# Optional application membership projections

IBM-0141 supplies a target-local consumer for the committed membership events in
[the lifecycle protocol](membership-lifecycle-events.md). It reads current native
membership metadata and updates an application's declared member projection. It
does not authorize customer access or change native users, roles, invitations or
ownership. Native access enforcement remains authoritative even if this consumer
is delayed, unavailable or dead-lettered.

## Composition

Install compatible native lifecycle storage first, including its existing scope,
purge fences and all-writer enrollment. `AddIBeamIdentityAzureTableMembershipEvents`
also registers `IIdentityMembershipProjectionSource`. The source performs bounded
point reads of selected member IDs, enabled status, canonical role IDs, logical
revision and sequence; no customer profiles or business records are returned.

The consumer application implements `IIdentityMembershipProjectionStore` protocol 1
with `Durable = true`, and `IIdentityMembershipProjectionWriteFence` protocol 1.
Both must use the app's real durable purge/write guard. Explicitly call
`AddIBeamIdentityMembershipProjection(enabled: true)` to install the projection
service as `IIdentityMembershipEventDelivery`. The default installs nothing.
Startup rejects missing or incompatible providers. Existing receivers must be
composed explicitly; registration will not replace a bus or another receiver.
Dispatcher installation and background delivery remain separate opt-ins.

`SyntheticMembershipProjectionStore.cs` in the bootstrap `docs/examples` folder is
a compiled, tested example using one preprovisioned private emulator table. It
accepts only explicitly declared synthetic tenants, application `fixture`, and
Development/Testing. It does not run in the demo host, create schemas at runtime,
or implement Qurvia business behavior. Its seed preserves existing purge blocks.

## Atomic persistence and recovery

The store must atomically commit the entire affected member group, a durable
original-intent receipt, and its own app write-guard/version CAS. Atomic owner
transfer has two subjects; do not apply half of the group. Preserve unrelated
application columns. Persist each subject's native sequence and logical revision:
an older sequence cannot replace a newer one; equal sequences with conflicting
revisions must fail. Do not advance a global processed-event cursor to the current
snapshot sequence and thereby skip another subject's pending event.

Delivery deduplicates the original event ID and original actor/payload binding
across process restarts. It reads current native state rather than blindly applying
an old event payload, so a late role event cannot restore an obsolete role. Missing
native membership produces a disabled metadata tombstone with no roles. The native
snapshot version and purge fence are rechecked before commit. Native and app stores
are separate transactions: a later native change still requires its event or repair;
the projection is never an authorization cache. Its app guard must join the local
transaction so a late event cannot recreate purged application rows.

After an ambiguous reply retry the same original intent. A durable committed
receipt ends the retry without repeating the effect; a failed transaction has no
receipt and can be retried. Do not invent a new ID after an unknown outcome.
Fixed provider errors omit private storage details. Calls use the service operation
pipeline without entity snapshots. Original actor metadata is attribution only,
never permission to impersonate a customer.

## Bounded trusted reconciliation

`IIdentityMembershipProjectionService.GetVersionAsync(scope)` reads the app's
opaque projection version. Trusted code can call `RepairAsync(intent, userIds,
expectedVersion)` with a stable nonempty intent ID, kind `repair`, and **1–96 distinct
nonempty native user IDs**. The exact version is mandatory; wildcard versions and
unbounded tenant scans are rejected. The receipt binds scope, original actor,
selection and version. `FindRepairAsync` recovers the original receipt for that same
scope/actor. Reusing an intent with a different selection or version conflicts.
Repair writes only declared projection metadata and never native membership.

**There is no support HTTP repair endpoint or repair permission installed by this
package.** The service port is for trusted target-local workers/operators, not
public request binding. Do not expose it behind Qurvia's `roles.assign` workaround.
If an app exposes support repair, register an independent fixed typed operation
(for example `app.memberships.reconcile`) through the Management operation registry.
Require an explicit grant, live support app assignment, target credential scope,
installed capability and host policy. Derive full scope and actor from verified
server context, require an exact projection version, and bind the stable operation
ID to selected IDs; provide a read-only original-receipt lookup. Existing
`roles.assign` grants must confer no repair authority. Use metadata-only audit and
the bounded signed target pipeline; never accept arbitrary table names, URLs,
roles, actor GUIDs or tenant lists from that route.

## Qurvia migration boundary

Qurvia owns its member rows, business columns, purge guard and adapter transaction.
Replace its `members/sync` role-saving side effect with an explicit durable receiver
following this protocol. Keep native authorization on every customer request.
Inventory all application writers and join their common purge fence before
enabling delivery. Adapt only native user IDs, enabled state, role IDs and logical
version metadata; do not copy clinical data into IBeam or the support portal.
Deploy a consumer with deduplication before enabling the emitter/dispatcher, and
use bounded trusted reconciliation to populate selected existing projections.
No Qurvia repository or production data is modified by this framework example.
