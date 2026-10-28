# Bounded invitation revocation

IBM-0144 adds independent operation **`management.invites.bulk-revoke`**. Neither
`management.identity.invites.manage` nor another tenant/role operation grants it.
Enable it separately in the target operation ceiling, machine credential, live
support app assignment and administrator delegation ceiling. It is absent by default.

## Modules and routes

The existing optional packages provide:

- `IBeam.Management`: `ManagementBulkInvitationRevokeOperation`, typed selection/result,
  private batch entity and service/repository ports in `IBeam.Management.CustomerIdentity`.
- `IBeam.Management.Identity.Target.Services`: `ManagementBulkInvitationRevokeService`
  and typed handler in `.Invitations`, wrapping native conditional invitation stores.
- `IBeam.Management.Repositories.AzureTable`: `AzureTableManagementBulkInvitationBatchRepository`.
- `IBeam.Management.Services`: `ManagementPortalBulkInvitationRevokeService` using
  `IManagementTargetOperationSender` with strict scoped result projection.
- `IBeam.Management.Api`: opt-in `ManagementPortalBulkInvitationRevokeController`.

Target POST:
`/api/management-operations/tenants/{tenantId:guid}/invitation-revocations/{resourceId:guid}`.
The resource GUID is the caller's stable batch ID, not an invite ID. Read-only target
recovery uses the registered receipt protocol at
`/api/management-operations/receipts/{operationId:guid}` through the typed sender.

Portal POST:
`/api/management/applications/{applicationId:guid}/tenants/{tenantId:guid}/invitation-revocations/{batchId:guid}`.
Portal GET:
the same path followed by `/operations/{operationId:guid}`.
The POST returns 202 and the original receipt `Location`; GET never resumes a batch.
Both require the separate Management human authentication scheme and current bulk grant.
There is no support invitation-token, customer-login or arbitrary target URL endpoint.

```json
{
  "invitations": [
    { "inviteId": "00000000-0000-0000-0000-000000000001", "expectedVersion": "exact-native-invite-version" }
  ]
}
```

Send a stable `Idempotency-Key` and `If-Match` containing the **native tenant version**
from the tenant directory. Each selected item also carries its own exact invitation
version. The example GUID is a placeholder, not seeded data. Maximum request/result
bytes are 32,768. Select 1–50 distinct nonempty invite IDs within the one fixed tenant;
host policy may lower this bound. Wildcards, duplicate IDs and empty selections fail.

## Explicit target setup

Provision a dedicated private journal table with trusted deployment tooling. Runtime
registration never creates tables. In the consumer target, install its existing native
`IIdentityTenantStore` (conditional version support required), `ITenantInviteStore` and
conditional `IManagedTenantInviteStore`. The opt-in helper can bind the latter from an
existing invite provider implementing that interface; it never falls back to legacy
unconditional updates. Configure `IBeam:Management:CustomerIdentity:IdentityRealmId`
and `DedicatedApplicationRealm=true`, matching the registered target realm exactly.
This attests the physical native stores belong to that app/environment; shared app
realms require a trusted scope adapter and must not be falsely marked dedicated.

```csharp
services.AddIBeamManagementAzureTableBulkInvitationBatchStore(preprovisionedJournal);
services.AddIBeamManagementBulkInvitationRevokeTarget(configuration, o => {
    o.Enabled = true;
    o.MaximumBatchSize = 50;
    o.MaximumItemsPerAttempt = 10;
    o.AttemptSeconds = 5;
    o.RetentionDays = 7;
});
services.AddIBeamManagementRegisteredOperationsTargetApi(configuration);
// Existing app pipeline: authentication, authorization, then MapIBeamManagementRegisteredOperations().
```

On the portal server, explicitly install
`AddIBeamManagementPortalBulkInvitationRevokeApi(configuration)` and the registered
typed sender/signing configuration. No native customer provider is installed in the
portal. See [target sender](target-operation-sender.md) for connection/key references
and fixed signed authority. Registration/discovery does not grant access.

## Progress and recovery

One durable row owns the original org/app/realm/tenant/person/batch/operation scope,
sorted immutable selection, exact versions and per-item state. SDK ETags compare-and-swap
every checkpoint. Two bounded binary properties retain the private metadata without
exceeding a Table property limit, including maximum opaque versions. No email, token,
password, OTP, role list, body snapshot or raw provider text is stored by this journal.
Versions remain private and are omitted from public progress and audit.

Default attempts process at most ten items under a five-second work deadline. The
framework persists `started` before each native call. The native operation ID derives
from the original verified batch operation and invite GUID, and is reused forever
within that batch. Native acceptance claims and exact-version CAS arbitrate acceptance
versus revoke: a redeemed or claimed invite cannot be revoked. Native revoke replay
may repair its token index, without another primary mutation. No mail is sent and no
membership, role, seat, global user, password or customer business table is changed.

Per-item states are `pending`, `revoked`, `already-revoked`, `not-eligible`,
`changed-version`, `failed` or `unknown`, with fixed safe reason codes. Acceptance,
expiry and missing invitations are explicit `not-eligible` reasons. Stale versions are
terminal for that immutable selection; do not replace them during resume. `failed`
rate-limit entries and uncertain entries remain resumable. Results contain the complete
bounded selection, original IDs, total and `complete`, not just the last attempted item.
`state=succeeded` means all selected items are confirmed revoked/already revoked;
terminal skips yield `partial` even when `complete=true`. Any uncertain item yields
`unknown`; processing acceptance is not proof of revocation.

When pending/failed work remains, an authorized operator can continue with the same
POST path, original body, idempotency key and original tenant `If-Match`. Reordered
identical selections are equivalent; changing IDs, versions or the original person
conflicts. This is a bounded server batch, not an unbounded browser revoke loop. A
later unrelated tenant rename does not rewrite the accepted selection. Current tenant
existence, native purge fences and current target/portal authority still gate new work.

After timeout, cancellation or an ambiguous reply, poll the original operation first.
Unknown/missing metadata is not proof that no work occurred. Authorized resume
reconciles the same native intent; never invent a new key or automatically retry a
destructive request with a fresh batch. Caller/host cancellation checkpoints only
metadata under a separate bounded cleanup budget; native late completion still uses
the original exact CAS. Revoked grants stop the next item and deny further polls;
restoring authority can resume the original owner-bound batch. Polls never call the
native transition or fix token indexes.

The default recovery window is seven days (host policy 1–30). Expired rows fail rather
than create another batch. No automatic physical TTL cleanup is installed. Trusted
retention tooling must preserve unfinished recovery evidence and minimal intent
tombstones; do not delete journals to retry. Framework operation journals are retained
control metadata, separate from consumer business-data deletion coverage.

## Safe observability

The shared sender validates tenant/batch/original operation, canonical child intent IDs,
selection and fixed state/reason combinations before returning metadata. Contradictory
write replies preserve the original uncertain operation ID. Common telemetry marks
unknown progress explicitly; registered audit uses verified scoped IDs only. Neither
audit nor telemetry includes selection versions, request/response bodies, keys or
provider text. Customer diagnostic sanitization settings do not weaken these rules.
See [safe target telemetry](target-operation-telemetry.md).

The focused tests use owned synthetic Table data and real signed HTTP proofs for
acceptance races, independent grants, all binding/replay rejections, partial attempts,
lost native/checkpoint replies, cancellation/deadlines, revoked grants, rate limits,
scope/expiry/purge fences, portal opt-in/authentication and contradictory replies.
No production invitations or consumer repositories are changed.
