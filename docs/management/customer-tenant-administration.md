# Target tenant creation and rename (IBM-0105)

This optional target module creates framework customer tenants and renames existing ones. It does not create a support person's customer membership, assign customer roles, set a default/current tenant, or issue customer tokens. Support staff remain external management actors. Portal forwarding is a separate card (IBM-0106).

## Registration and storage

After the target's normal Identity services/store, named management machine authentication, actor verification and durable management audit registrations:

```csharp
services.AddIBeamManagementAzureTableIdentityOperationStore(operationsTable);
services.AddIBeamManagementAzureTableIdentityOperationAuditStore(auditTable);
services.AddIBeamManagementAzureTableTenantAdministrationStore(tenantMetadataTable);
services.AddIBeamManagementTenantAdministrationApi(configuration);
```

Provision the dedicated tables through trusted setup. This registration installs operation receipt reads and the tenant administration services; it does not create tables, install customer authentication defaults, or provision memberships. Configure the stable operation fingerprint key and bounds described in [durable operations](customer-identity-operations.md). Set `IBeam:Management:CustomerIdentity:EnabledOperations` explicitly and provision matching machine capabilities and independent human app grants for `management.identity.tenants.create` / `management.identity.tenants.update`. Existing diagnostics/read grants confer neither operation.

`IBeam.Management` holds transport-neutral commands/results and tenant administration metadata. `ManagementTenantAdministrationService` orchestrates authorization, operation receipts, metadata and `IIdentityTenantAdministrationService`. The optional Identity service adapter reuses `IIdentityTenantService.CreateAsync`, tenant extension coordinators, lifecycle hooks and event publishing. `IIdentityTenantStore` / `IIdentityTenantService` interfaces and existing constructors remain unchanged. `IIdentityTenantConditionalStore` is additive. The Azure Table tenant store implements it; custom stores must implement real conditional writes to advertise updates. Unsupported stores can still create tenants but expose no `tenants.update` capability and cannot execute rename.

The normal `TEN` / tenant GUID row convention is retained. Conditional rename reads framework fields only and merges `Name`, `NormalizedName`, `UpdatedAt` and an optional `LastOperationId` marker with the **caller-provided ETag**. It preserves status, creation time and consumer columns. Missing legacy creation time projects as null. Reads do not add markers, rewrite tenants or create management metadata.

`ManagementTenantAdministrationEntity` lives in a separate target metadata table: source (`support-provisioned` or `unknown`), optional external reference, creation/last operation IDs and phase (`creating`, `renaming`, `ready`). Its partition binds realm/app/environment. New support-created tenants receive `support-provisioned`; legacy/external tenants remain `unknown`, including after rename. Missing provenance never means self-signup. No customer profile, name, email, membership, role, session, token, password or consumer extension JSON is stored in this table.

## HTTP contract

Every request uses the deployment-scoped management key and a short-lived signed support actor bound to the exact method/path/body/idempotency key. Customer bearer credentials or machine-only authorization cannot perform these actions.

- `POST /api/management-identity/tenants` with `Idempotency-Key` and `{ "name": "Enterprise Customer", "externalReference": "CRM:123" }`.
- `PATCH /api/management-identity/tenants/{tenantId}` with `Idempotency-Key`, the exact returned `If-Match` version, and `{ "name": "New Name" }`.

Name is required, trimmed, at most 512 characters and cannot contain controls. External reference is optional, at most 128 ASCII letters/digits or `- _ . : /`; an empty value means absent. Arbitrary properties (profiles, roles, status, source, owner/user IDs, etc.) are rejected. Request body/authentication bounds from the delegated actor module still apply.

External references are immutable and unique **case-insensitively among management metadata registrations in one target realm/app/environment**, across support organizations managing that target. An atomic metadata/index reservation prevents a second tenant from being created under the same reference. A conflict returns 409 without revealing the other tenant ID. Names need not be globally unique. The module neither discovers nor backfills references from consumer records.

Rename requires a nonempty, exact version; missing/wildcard versions cannot update, and stale versions return 412. The `If-Match` digest is included in the actor's signed request binding; changing or removing it invalidates the proof. Clients must issue a new intent/key after deliberately refreshing a version, rather than changing the payload/version of an existing intent.

Completed creation returns 201 with IBeam `success`/`data` and a `Location` for the operation receipt; completed rename returns 200. Data is `operation` (safe receipt), `tenant` (ID/name/status/times/version), `source` and optional external reference. Pending/running/uncertain receipts return 202 with a null tenant. Responses are no-store. Validation errors are generic 400, denied access 403 (invalid authentication/proofs 401), missing tenants 404, idempotency/reference conflicts 409, stale versions 412, unavailable dependencies/incomplete lifecycle work 503. Provider/consumer exception text, profiles and credentials never appear in responses or management audit.

## Recovery and consumer hooks

The coordinator reserves stable operation/resource IDs and records audit intent before customer writes. It then reserves tenant administration metadata (and any unique reference) before calling Identity. Extension/event failure leaves the existing tenant and a tracked `creating`/`renaming` phase plus an uncertain operation; there is no destructive rollback or second random tenant. Retry the same normalized request/version and idempotency key with a fresh assertion. Lost replies and 503s also use that retry pattern.

Creation recovery verifies the owned creation-operation link and existing tenant before replaying Identity creation hooks/extension ensure. Rename recovery requires the stored operation marker and expected final name, then resumes lifecycle work without another name write. Unknown/unrelated mutations do not get adopted or overwritten. Another management rename cannot take over an incomplete metadata phase. After an uncertain rename, retry the **original** expected version/key so its operation marker can reconcile the already committed change.

Consumer tenant hooks and extension stores must be idempotent. `TenantExtensionContext.AuthUserId` is null; trusted metadata identifies `origin=external-management`, operation ID, support organization/person and management application. Correlation and causation connect recovery attempts to the same intent. Do not treat a support person as a customer account. On recovery, an update hook's previous tenant can be null because no old customer name/profile is persisted in management metadata. Use the operation/tenant ID and current Identity state to reconcile side effects. Lifecycle delivery can be at least once; external conditional/idempotent effects remain the consumer's responsibility. Update event idempotency includes the specific operation ID.

The external adapter enables `TenantExtensionContext.RedactFailureDetails` and `RequireLifecycleDelivery`; framework lifecycle error logging and propagated failures omit raw consumer exceptions, and failed delivery remains recoverable even if ordinary customer sign-up permits non-strict publishing. Ordinary Identity calls keep their existing logging/delivery defaults. Status casing is canonicalized for responses without rewriting legacy stored values. Customer extension services may perform their normal internal work, but their records are never forwarded by this API. Default-tenant, membership, role, sign-in and customer-token services are absent from the administration path.
