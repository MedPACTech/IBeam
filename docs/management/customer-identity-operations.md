# Durable customer Identity operations (IBM-0104)

This is the common receipt/retry infrastructure for subsequent tenant, membership and invitation write modules. It introduces no general-purpose mutation HTTP endpoint and grants no write capability by itself. Business services validate their own typed requests and use `IManagementIdentityOperationService.ExecuteAsync` with a server-owned `IManagementIdentityMutationHandler`.

## Projects and registration

- `IBeam.Management`: `ManagementIdentityOperationEntity`, safe receipt DTOs, repository/audit ports and mutation handler/service contracts.
- `IBeam.Management.Identity.Target.Services/Operations`: scoped reservation, canonical fingerprinting, conditional claims, reconciliation, receipts and durable audit delivery through `IServiceOperationExecutor`.
- `IBeam.Management.Repositories.AzureTable`: operation metadata adapter and an idempotent append-only audit destination.
- `IBeam.Management.Identity.Target.Api`: explicitly installed receipt GET, using the named machine scheme plus a verified support actor.
- `IBeam.Management.Services` and `.Api`: optional selected-app receipt forwarding with live support grants and the same pinned HTTPS/signed-actor transport as directory reads. No customer database/Identity registration in the portal.

After normal target authentication/delegation setup, register:

```csharp
services.AddIBeamManagementAzureTableIdentityOperationStore(dedicatedOperationsTable);
services.AddIBeamManagementAzureTableIdentityOperationAuditStore(dedicatedAuditTable);
services.AddIBeamManagementIdentityOperationsApi(configuration);
```

Provision both tables in trusted deployment/setup code. Runtime adapters never create schema or delete rows. Operations need point read/add/conditional update rights; audit needs add and point read for duplicate confirmation. The existing `IManagementAuditRepository` and audit sink still record normal metadata-only service activity; the separate operation audit port provides repeatable delivery of durable mutation events.

`IBeam:Management:CustomerIdentity:Operations` requires `FingerprintKey`: base64 of 32 random secret bytes. **Keep this key stable across all target instances and restarts throughout the retention window.** Changing it without migrating reservation identities can defeat idempotency. Store it in the deployment's secret store, separately from token/signing keys. Options: `RetentionDays=90` (1–3650), `LeaseSeconds=60` (30–300), `TimeoutSeconds=20` (1–30, strictly below lease), `MaximumAttempts=10` (1–100). Hosts/adapters must honor cancellation. Resolving the operation service validates all dependencies at startup.

The portal calls `AddIBeamManagementPortalIdentityOperationsApi(configuration)` after its normal authentication, repositories, scoped secret store and actor-signing registrations. It reuses the bounded `PortalCustomerIdentity` transport settings described in [portal reads](customer-identity-api.md).

## Mutation/retry contract

Every non-GET target customer-management request requires exactly one `Idempotency-Key`: 8–128 ASCII letters, digits, `-`, `_` or `.`. Generate a fresh random key for each new user intent. Retry the same intent with the same key and validated payload but a **fresh actor assertion**. The signature binds method, exact encoded path/query, body digest and idempotency-key digest. Missing, duplicate, changed or invalid keys fail authentication before a mutation/replay claim. The business service must pass that same key and its validated payload into `ManagementIdentityMutationRequest`; direct service calls also require it.

The coordinator canonicalizes bounded JSON objects (32 KiB, depth 8, at most 100 object fields/array entries, duplicate properties rejected), sorts object fields and preserves array order/scalar representation. Business services should serialize normalized typed requests so irrelevant whitespace/object ordering does not change the fingerprint. It stores only a keyed HMAC, never the body or raw idempotency key. Scope includes support organization, target realm, portal application, app/environment, operation and resource. `ResourceScope` identifies a parent tenant or logical resource; set `ExistingResourceId` only for a change to an existing entity. Creates reserve a new ID even when they belong to an existing tenant. The keyed reservation ID makes concurrent requests contend on one atomic insert. A changed payload with the same scoped key returns 409; the original resource ID remains stable. Other operations/resources have separate scopes.

Azure Table partitions are `realmN|organizationN|applicationN`, with the operation GUID as row key. The typed metadata row contains operation/resource IDs, safe status/step, original and attempt actor/credential/correlation IDs, claim/expiry/attempt metadata and audit delivery state. Internal HMAC/lease fields are JSON-hidden in public serialization; no name, email, password, profile, invitation secret/link or arbitrary error/result JSON is stored. Receipt step is 0 before completion and 1 after completion; later workflow modules can coordinate multiple such receipts.

The reservation stores audit intent before any customer mutation. The coordinator delivers that intent before applying work. Provider failure at reservation or intent delivery prevents a new customer write. New resource IDs are derived with a separate HMAC purpose from the scoped intent, so concurrent reservations and recovery retain the same ID. ETags claim one lease; concurrent callers get the existing pending/running/completed receipt. After crashes or lost replies, a new service instance reconciles the **same reserved resource**, rather than generating a second one. Unknown reconciliation remains `uncertain` and performs no write. Attempts and operation time are bounded.

**A lease cannot fence a different database.** Every trusted business handler must reconcile by stable resource ID and a verified postcondition, and apply using conditional create or an expected version/ETag. This remains required when a stalled old worker resumes after lease expiry. Do not wrap unguarded legacy create/update calls and assume the receipt makes them exactly once. The tests cover lost replies, failed receipt persistence and a resumed stale owner; later business cards supply the actual Identity adapters. Permission revocation is checked before applying and around completion; an already committed customer write cannot be rolled back by a later access check.

Cancellation or an ambiguous downstream exception leaves a durable `uncertain` result, or a running lease if metadata storage also failed. Retry after the lease expires and reconcile. A succeeded receipt is preserved if subsequent audit delivery fails. Durable intent/result events have stable audit IDs; `AppendOnceAsync` accepts an identical retry and rejects different metadata under the same ID. `DeliverAuditAsync` can acknowledge/redeliver pending events without invoking the customer handler. Delivery after a lost acknowledgement does not duplicate audit events. Events contain only trusted scope/actor IDs, action/phase, time and result. No automatic background delivery or retention job is installed: the host can retry receipt execution/delivery with current authority, or arrange trusted operator maintenance.

## Receipt reads

- Target: `GET /api/management-identity/operations/{operationId}`.
- Portal: `GET /api/management/applications/{applicationId}/operations/{operationId}?authority=management.identity.tenants.create` (use the operation that created the receipt).

Receipt GETs are explicitly marked read endpoints. The target verifies the signature before accepting an original write operation as read authority; this exception applies only to the fixed receipt path, with no target query/body. It still checks current machine capability, installed business adapter and target policy. Ordinary GET endpoints continue to reject assertions with write authority. The service checks stored operation, realm, organization, application and app/environment before returning anything. `onboarding.read` can poll onboarding create/retry receipts only. That independent capability requires explicit host/machine/human grants; it does not expose unrelated mutation receipts. Registering receipt infrastructure never advertises tenant/user/invitation write adapters.

The portal checks the requested authority against live support assignments before resolving secrets, verifies the target's live module binding and rechecks the connection/access before returning its allowlisted receipt. GET responses use IBeam `success`/`data` envelopes and `Cache-Control: no-store`. Public data is operation ID/name, `pending`/`running`/`succeeded`/`uncertain`, step, safe resource ID and timestamps. There is no receipt enumeration, arbitrary URL or raw proxy endpoint. Target/portal transport bodies and deadlines remain bounded; a poll is one scoped point lookup plus bounded security checks. Clients should wait at least two seconds between polls and back off while status is unchanged; operators should apply their host's request-rate limits.

Expired receipt reads return 404. An expired reservation still returns 409 on replay, rather than silently reusing its key. Azure Table expiry is metadata, not automatic TTL. Trusted retention maintenance may purge completed/expired rows only after the chosen idempotency/recovery/audit window and all lease safety margins; never delete running, uncertain or pending-audit rows as routine cleanup. After physical removal the server cannot recognize an old key, so clients must never recycle keys. Keep keys/backups, metadata storage and retention policy consistent across deployments. This is deliberately bounded idempotency, with conditional downstream writes as the final duplicate guard.
