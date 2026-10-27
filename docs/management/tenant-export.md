# App-owned tenant export

## IBM-0130: versioned contracts and optional target boundary

The contract/adaptor card supplies `IBeam.Management.TenantData` contracts, optional `IBeam.Management.TenantData.Target.Services` and `.Api` packages, and compiled signed submit/status routes. It does not implement Qurvia's business exporter, the framework's durable job worker/store (IBM-0131), or the scoped support portal endpoints/client facade (IBM-0132). Synthetic consumer coverage and the full export feature regression are IBM-0133. These are development changes for a future release, not published 2.16.1 functionality.

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
- `IManagementTenantExportJobService`: durable same-intent submission and read-only lookup. It must bind every row and lookup to the full original scope, including requesting support person; Find must never enqueue work or infer authority from a guessed job ID. The framework implementation arrives in IBM-0131. A host can implement the port independently. Volatile jobs require explicit `AllowVolatileDevelopmentJobs` and Development/Testing; they fail startup in Production.
- `IManagementTenantExportDelivery`: app-owned customer delivery/reconciliation and exact-job artifact revocation/deletion. It resolves approved recipients through target policy and enforces retention. A support principal must never be treated as a customer recipient automatically.
- `IManagementTenantExportCustomerAuthorization`: authorization on the app's separate customer retrieval surface. It must enforce live customer identity, tenant/job ownership, membership/retention policy and expiration/revocation before retrieval. A support export permission alone supplies none of that customer authority. This module deliberately has no support artifact download endpoint.

No default permissive policy, exporter, customer authorization, delivery adapter or fake durable worker is installed. Discovery resolves the usable typed adapter only after required startup validation. A descriptor or declared receipt version does not itself make a native provider durable.

## Safe status and coverage

`ManagementTenantExportResult.Outcome` distinguishes `accepted`, `found` and `unknown`. `accepted` means the submission was accepted; it does not mean that business export or delivery completed. Unknown results contain no job projection. `ManagementTenantExportJobStatus` separately names `requested`, `running`, `partially-completed`, `succeeded`, `failed`, `cancelled` and `expired`.

The version-1 public status contains only tenant/job GUIDs, UTC timestamps, a declared consistency mode (`transactional`, `point-in-time` or `best-effort`), bounded coverage and an optional fixed failure code. Coverage includes every declared inventory key and its `dataset`/`attachment` kind. Dispositions are `pending`, `completed`, `omitted` or `retained`. Keys are static host-declared categories, not customer table values or filenames. All inventory entries must be present exactly once. Succeeded requires every entry completed and no failure code; omitted or retained categories require explicit partial completion. Invalid/foreign/undeclared provider metadata is rejected and reconstructed into safe DTOs.

`ManagementTenantExportArtifact.Reference` and `ManagementTenantExportOutput` are private server-side ports: JSON hides the private reference/output, and ToString is redacted. They never appear in Management status, audit or telemetry. Private references must stay in target-owned artifact storage or authenticated encrypted recovery metadata. Do not log app/provider exceptions; the target adapter translates unexpected provider errors into fixed safe failures. Receipt reads are audited separately from export submissions; an uncertain result is not recorded as successful export completion.

The durable implementation will use a dedicated scoped job table, stable operation/job identity, exact ETags, leases, frozen inventory/consistency and private protected artifact references. Runtime reads must not create tables. The support API will read safe job projections only; it will not fetch tenant records or artifact bytes. No production export, new retention promise or Qurvia business-table implementation is included in this contract card.

## Verification cadence

New focused tests cover state/coverage projection, strict schema limits, private-reference redaction, default-disabled registration, missing/unsupported/durability providers, compiled route restrictions and real signed HTTP submit/status with independent grants, policy denial, request substitution, replay and safe errors. Each card builds/packs changed modules and passes required PR checks; the complete legacy/fresh NuGet-only export matrix runs at IBM-0133 feature completion.
