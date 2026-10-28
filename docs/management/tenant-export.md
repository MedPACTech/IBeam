# App-owned tenant export

## IBM-0130: versioned contracts and optional target boundary

The contract/adaptor card supplies `IBeam.Management.TenantData` contracts, optional `IBeam.Management.TenantData.Target.Services` and `.Api` packages, and compiled signed submit/status routes. IBM-0131 adds the framework's durable journal/service, leased worker and private customer retrieval gate described below. Qurvia's business exporter remains app-owned; the scoped support portal endpoints/client facade are IBM-0132. Synthetic consumer coverage and the full export feature regression are IBM-0133. These are development changes for a future release, not published 2.16.1 functionality.

| Surface | Contract / path |
| --- | --- |
| Native export submission | `POST /api/management-tenant-data/tenants/{tenantId}/exports` |
| Native read-only status | `GET /api/management-tenant-data/tenants/{tenantId}/exports/{jobId}` |
| Planned scoped portal submission | `POST /api/management/applications/{applicationId}/tenants/{tenantId}/exports` |
| Planned scoped portal status | `GET /api/management/applications/{applicationId}/tenants/{tenantId}/exports/{jobId}` |
| Independent authority | `management.tenants.export`; discovery additionally needs `management.operations.info` |

The native routes use the standard persisted machine-key plus short-lived RS256 Management actor proof. The descriptor is bound to the exact compiled export DTOs, method and submit route. The status route is a compiled read-only receipt alias bound to the same export operation, signed tenant and stable job GUID. It cannot become a general native route proxy. Export's selected resource GUID is the tenant GUID. Neither ownership, tenant status, membership nor another app's assignment grants export. The existing `management.tenants.delete` name remains unavailable until its own later module is installed.

The JSON request contains only `policyKey` and `expectedVersion`; If-Match must equal that exact native tenant version, and Idempotency-Key is required. A host-owned policy resolves the recipient and destination. There are no email, filename, download-URL, storage-reference or arbitrary-filter fields. The standard typed client derives a stable scope/person/intent job GUID and a fresh assertion nonce for each explicit attempt. Unknown writes must reconcile by the same job reference without launching another export.

## Host registration and required ports

```csharp
using IBeam.Management.TenantData.Target.Api;
using IBeam.Management.Operations.Target.Api;

// Supply the app's providers and compatible durable job service first.
services.AddIBeamManagementTenantDataTargetApi(configuration, options => {
    options.Enabled = true;
    options.PolicyKeys = ["enterprise"];
});

// Use the common signed dispatcher mapping once per host:
app.MapIBeamManagementRegisteredOperations();
```

Registration is disabled by default. When enabled, startup requires every provider below with protocol 1 and a compatible live Management operation access service. The host also supplies the existing credential, pinned validation-key, shared atomic assertion replay and safe audit providers described in [registered dispatch](registered-operation-dispatch.md). Explicitly enable the export operation in target policy and issue/reconnect an appropriately scoped key; registration never grants it.

- `IManagementTenantExportHandler`: a static versioned inventory and app-owned `ExecuteOrResumeAsync` / `CancelAsync`, bound to a stable organization/app/environment/realm/tenant/person/job context. Native Identity and commercial packages are optional. Inventory must cover the consumer's actual ownership/history requirements, including removed or unseated members when their data belongs to the tenant.
- `IManagementTenantExportPolicy`: current tenant eligibility, exact native version and approved policy/recipient rules at submission. A policy key comes from the host's explicit bounded allowlist. It is not a browser-supplied destination.
- `IManagementTenantExportJobService`: durable same-intent submission and read-only lookup. It must bind every row and lookup to the full original scope, including requesting support person; Find must never enqueue work or infer authority from a guessed job ID. The standard framework implementation is an optional registration; a host can also implement the port independently. Volatile jobs require explicit `AllowVolatileDevelopmentJobs` and Development/Testing; they fail startup in Production.
- `IManagementTenantExportDelivery`: app-owned customer delivery/reconciliation and exact-job artifact revocation/deletion. It resolves approved recipients through target policy and enforces retention. A support principal must never be treated as a customer recipient automatically.
- `IManagementTenantExportCustomerAuthorization`: authorization on the app's separate customer retrieval surface. It must enforce live customer identity, tenant/job ownership, membership/retention policy and expiration/revocation before retrieval. A support export permission alone supplies none of that customer authority. This module deliberately has no support artifact download endpoint.

No default permissive policy, exporter, customer authorization, delivery adapter or fake durable worker is installed. Discovery resolves the usable typed adapter only after required startup validation. A descriptor or declared receipt version does not itself make a native provider durable.

## Safe status and coverage

`ManagementTenantExportResult.Outcome` distinguishes `accepted`, `found` and `unknown`. `accepted` means the submission was accepted; it does not mean that business export or delivery completed. Unknown results contain no job projection. `ManagementTenantExportJobStatus` separately names `requested`, `running`, `partially-completed`, `succeeded`, `failed`, `cancelled` and `expired`.

The version-1 public status contains only tenant/job GUIDs, UTC timestamps, a declared consistency mode (`transactional`, `point-in-time` or `best-effort`), bounded coverage and an optional fixed failure code. Coverage includes every declared inventory key and its `dataset`/`attachment` kind. Dispositions are `pending`, `completed`, `omitted` or `retained`. Keys are static host-declared categories, not customer table values or filenames. All inventory entries must be present exactly once. Succeeded requires every entry completed and no failure code; omitted or retained categories require explicit partial completion. Invalid/foreign/undeclared provider metadata is rejected and reconstructed into safe DTOs.

`ManagementTenantExportArtifact.Reference` and `ManagementTenantExportOutput` are private server-side ports: JSON hides the private reference/output, and ToString is redacted. They never appear in Management status, audit or telemetry. Private references must stay in target-owned artifact storage or authenticated encrypted recovery metadata. Do not log app/provider exceptions; the target adapter translates unexpected provider errors into fixed safe failures. Receipt reads are audited separately from export submissions; an uncertain result is not recorded as successful export completion.

The durable implementation uses a dedicated scoped job table, stable operation/job identity, exact ETags, leases, frozen inventory/consistency and private protected artifact references. Runtime reads do not create tables. The support API reads safe job projections only; it does not fetch tenant records or artifact bytes. No production export, new retention promise or Qurvia business-table implementation is included.

## IBM-0131: durable execution and private delivery

Provision a dedicated target-owned export job table through deployment/operator tooling and register its `TableClient`; table creation is never a runtime read side effect:

```csharp
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.TenantData.Target.Services;

services.AddIBeamManagementAzureTableTenantExportJobs(dedicatedExportJobTable);
services.AddIBeamManagementTenantExportJobs(configuration);
// Register the app's exporter, delivery, customer authorization and tenant policy,
// and enable the target API as shown above.
```

The standard `ManagementTenantExportJobService` rechecks signed live support access, native deployment and the complete original person/job scope. It stores one immutable request fingerprint per stable job identity. Same-intent duplicates return the existing job before applying a new tenant-version precondition; changed intent conflicts. Every first submission requires the current native version and target policy. Status is read-only and rechecks current native status policy. Frozen inventory is retained for truthful historical status. Do not change a handler/inventory while its jobs are active without retaining a compatible adapter; incompatible active work pauses for reconciliation.

The Azure Table repository partitions by application/environment/realm/organization/app and identifies rows by tenant/job. Person binding is checked on every point read. Exact ETags protect all state, lease and cancellation changes. `Metadata` contains scoped operational state; `ProtectedArtifact` is a separate authenticated ciphertext column, omitted from metadata JSON and ToString. Recovery scans project only metadata, select one bounded page and validate deployment/row bindings. No artifact bytes, filenames or recipient data are stored in these rows.

Configure `IBeam:Management:TenantExport:Jobs` from the host's normal configuration and secret providers:

- `ActiveKeyId` and `ArtifactProtectionKeys`: a retained key ring of at most five named 32-byte Base64 secrets. AES-GCM binds recovery to deployment, full original scope, job, policy/inventory, request fingerprint and creation/expiration. Keep old keys until their artifacts are cleaned; deleting a key cannot authorize or silently discard an artifact.
- `RetentionMinutes`: 1–1,440, default 60; this is the maximum job/artifact lifetime. The hook receives `ArtifactExpiresUtc` and must honor it in its own storage. A shorter approved artifact lifetime shortens the job deadline. Protected reference JSON is capped at 12,000 bytes and never contains the exported data itself.
- `LeaseSeconds`: 10–120, default 45. Providers receive a live `LeaseFence` and must check it before publishing output or committing delivery, in addition to honoring cancellation.
- `EnableBackgroundWorker`: default false. Enable the bounded metadata recovery scan, or explicitly schedule the trusted `IManagementTenantExportWorker` from the consumer's durable queue. `ScanIntervalSeconds` is 1–300, default 10. The built-in scan processes one page of up to ten handles per tick with a 55-second budget; a large installation should use its own queue/index for prompt scheduling. The journal is authoritative, so an accepted job survives a process restart even if a scheduling signal is lost.

An accepted job is a bounded durable command. Revoking support access blocks further support requests and polls; cancel accepted work separately when it must stop. Before each execution/delivery phase the worker checks current target deployment/work bindings and native policy. Customer retrieval always uses current customer authorization. Disabling/removing a work binding does not prevent trusted cleanup of historical jobs in that deployment.

`ExecuteOrResumeAsync` and `DeliverOrReconcileAsync` must use the full scoped job identity to reconcile their own durable work before creating effects. The framework journal cannot transact atomically with consumer tables, blob storage or notification providers. Native providers therefore own snapshot consistency, scoped output ownership and idempotent delivery receipts. The lease fences journal updates and stale publication; providers must honor the fence at their own commit boundary. Long operations should use a durable consumer workflow and report `ManagementTenantExportPendingException` only when they positively know the same job/delivery remains running.

Unknown exceptions, timeouts and ambiguous replies pause the journal with `NeedsReconciliation` and a fixed safe failure code. The background scan never automatically repeats such work. A trusted operator/consumer reconciler can explicitly call `RunAsync(execution, reconcileUncertain: true)` after establishing that its providers reconcile by the same job ID. A positive pending signal and a crash with a durable phase can resume through the provider's same-job reconciliation contract. Invalid/incomplete coverage cannot become succeeded; omitted/retained data is explicitly partial. Provider text never enters the public result, service logs or safe audit.

`CancelAsync` marks the selected journal immediately; fences and customer retrieval then deny further publication/access. Cleanup waits for an active lease to end, cancels that consumer job and revokes/deletes only its validated artifact. `Cleaned` is recorded only after the provider confirms those steps. Cancelled/expired status denies access immediately and does not claim that physical cleanup already completed. An uncertain cleanup pauses for explicit reconciliation. Keep the provider's own expiration/retention enforcement as well as the framework cleanup scheduler. Journal metadata/receipt deletion is a separate operator policy; automatic deletion must not erase idempotency history and allow an old intent to execute again.

For the app's separate authenticated customer retrieval route, call the server-only `IManagementTenantExportCustomerArtifacts.RequireAsync` with the trusted stored job scope and authenticated customer. It rejects Management human/credential principals, guessed/foreign jobs, incomplete/cancelled/expired outputs and revoked customer access. It checks customer authorization before private lookup and again before returning the JSON-hidden artifact handle; the delivery adapter verifies artifact ownership. The app then reads/streams its own artifact through its customer channel. No support HTTP download route, bearer URL or customer impersonation is supplied.

Worker audit events are metadata-only system events bound to organization/app/job and deployment. The initiating support person remains in the immutable journal; workers do not impersonate that person. Audit and app/journal commits are separate resources: a late audit outage must be reconciled from the journal without replaying consumer work. The optional scan emits a fixed failure counter, never provider exception text.

## Verification cadence

New focused tests cover state/coverage projection, strict schema limits, private-reference redaction, default-disabled registration, missing/unsupported/durability providers, compiled route restrictions and real signed HTTP submit/status with independent grants, policy denial, request substitution, replay and safe errors. Each card builds/packs changed modules and passes required PR checks; the complete legacy/fresh NuGet-only export matrix runs at IBM-0133 feature completion.
