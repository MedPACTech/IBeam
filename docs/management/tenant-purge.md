# Tenant purge

Tenant purge has its own `management.tenants.delete` grant. Tenant status, roles, licences, seats and export do not grant it. Existing assignments and machine keys must be explicitly updated and connections verified before enabling this capability. Support never obtains a customer principal or direct database access.

## Contracts and preflight (IBM-0134)

Core contracts live under `IBeam.Management.TenantData`. `ManagementTenantPurgeOperation.Descriptor` binds only `ManagementTenantPurgeRequest` / `ManagementTenantPurgeResult` to POST `/api/management-operations/tenants/{tenantId}/purges`. The fixed typed command has exactly two stages, `plan` and `confirm`; the stage cannot select another operation, route or handler. The existing signed dispatcher supplies live human/machine grants, short-lived actor proof, exact tenant/resource scope, idempotency key and If-Match. This capability has no automatic grant upgrade.

`AddIBeamManagementTenantPurgeTargetApi(configuration, configure)` is optional and disabled by default. When enabled, it composes `AddIBeamManagementTenantPurgeTarget` and the standard signed dispatcher; call `MapIBeamManagementRegisteredOperations()` once. All handler/policy/job/access providers must use protocol 1. Missing providers fail startup. Production requires durable atomic plans/jobs; volatile jobs are allowed only with the explicit development flag in Development/Testing. Policy keys are target-owned and confirmation lifetime is 30–300 seconds (default 180).

The first command is `ManagementTenantPurgeRequest("plan", policyKey, exactTenantVersion, null, null)`. `IManagementTenantPurgeHandler.Describe()` returns a static versioned inventory with bounded category keys, kinds and `delete` / `retain` / `exclude` dispositions. It must explicitly exclude `global-accounts` as `identity-metadata`. Categories describe scope, not customer names, file paths or individual records. `PreviewAsync` is read-only: it must not fence access, change tenant status or delete data. The façade calls no destructive handler during preflight or confirmation.

`IManagementTenantPurgePolicy.RequireAsync` checks the native exact tenant revision and current app-owned eligibility before and after preview. The service rechecks live support/machine authority. The plan records only safe metadata: tenant/plan IDs, approved policy key, exact native version, static inventory fingerprint, UTC lifetime and declared categories. Its confirmation binding hashes the target application key/environment, organization/application/realm/tenant/person, plan ID, policy, native version, inventory and lifetime. The binding is public metadata, **not** a credential: knowing a plan or its binding grants no authority.

Confirmation uses a new stable job intent: `ManagementTenantPurgeRequest("confirm", policyKey, exactTenantVersion, planId, confirmationBinding)`. The original support user, target scope, current inventory, exact native revision and unexpired plan must still match. Fresh access/policy checks precede the provider's atomic confirmation. A changed inventory, stale tenant version, revoked key/assignment or missing handler prevents new destructive work. Plan and confirmation idempotency keys must differ. The returned `accepted` / `requested` state means a durable job was reserved, not that business data was deleted.

`IManagementTenantPurgeJobService` is the durable provider boundary. It must bind every read/write to the full app/environment/org/realm/tenant/person scope. Creating a plan preserves its immutable prior intent. Confirming must atomically consume the exact unexpired plan and reserve exactly one job, checking the current native revision/write fence at its commit boundary. Same-job replay returns its receipt; using a consumed plan for another job conflicts. An ambiguous write must be reconciled by its original job reference rather than creating a replacement destructive intent. Read-only receipt polling uses the existing fixed registered-operation receipt route with the original delete authority; it must never confirm, resume or delete anything.

Job status contains bounded declared coverage and fixed state/failure codes. Required categories cannot remain pending or retained under `succeeded`; retained or excluded scope stays visible. Global accounts cannot be reported deleted. Provider text, business records, storage selectors, credentials, paths and private references never enter these DTOs, audit or diagnostic responses. Planning and status use the read-only lookup audit action. Confirmation audit indicates command handling/acceptance; completion requires authoritative worker verification.

## Ordered implementation

IBM-0134 supplies these contracts, validation and fail-closed optional target composition. A custom provider is responsible for the documented atomic contract; a compatible protocol number alone is not evidence that its implementation is correct.

IBM-0135 implements durable coordination, leases, native write/access fencing, uncertainty and app-owned cleanup. IBM-0136 adds the optional tenant-scoped Identity bridge while preserving shared global users, OTPs, sessions and unrelated tenants. Ordinary last-owner/administrator protections remain enabled; no general skip flag is added. IBM-0137 supplies the typed portal `/purge-plans`, `/purges` and progress routes. IBM-0138 verifies synthetic full coverage, interruption recovery, exclusions and NuGet consumers.

## Standard durable coordinator (IBM-0135)

Register `AddIBeamManagementAzureTableTenantPurgeJournal(preProvisionedDedicatedTableClient)` in the target, then `AddIBeamManagementTenantPurgeJobs(configuration)` and the explicit purge target API. Supply the app-owned handler, policy and **all** `IManagementTenantPurgeAccessFence` implementations, plus the existing audit/access providers. The framework creates no runtime tables. The journal is separate from business storage and contains operational metadata only. Core and the Azure Table provider depend on no native Identity or commercial module.

The standard coordinator requires 1–8 uniquely named protocol-1 native fences. Each fence must durably reserve the exact tenant for the original fully scoped purge job, conditional on the planned native revision; it must deny customer requests, refreshes and every app/native write before cleanup. Deactivation alone is insufficient. Ordinary activation or membership writes must not undo it. `RequireFencedAsync` verifies current ownership by that same job, and each destructive hook must check `execution.LeaseFence.RequireAsync()` at its own commit boundary and reconcile durable effects by the full scoped job. The consumer is responsible for its actual request/write integration and native atomic/idempotent commits. The optional IBeam Identity implementation arrives in IBM-0136; declaring a fence port does not implement that integration automatically.

Plan and job rows share the exact deployment/org/app/realm/tenant partition. Confirmation consumes the plan by exact ETag and creates the job in a single Table transaction; a concurrent second intent cannot consume it. The stored plan keeps its original expiry on replay. The journal freezes inventory, exact native version, policy and required fence keys. Missing/changed fences or an incompatible handler inventory stop execution. The native fence checks the exact revision again before any cleanup, so a tenant changed after confirmation is not deleted under the stale plan. No cross-database transaction is claimed between journal acceptance and the native fence.

`IBeam:Management:TenantPurge:Jobs` accepts `LeaseSeconds` (10–120, default 45), `EnableBackgroundWorker` (default false) and `ScanIntervalSeconds` (1–300, default 10). A queue or trusted target scheduler may call `IManagementTenantPurgeWorker.RunAsync`. The optional scanner rotates current trusted app bindings, reads one bounded metadata page and dispatches at most ten handles per tick under a time budget. It skips completed and uncertain jobs. Large installations should use a native durable queue/index rather than rely on scanning historical journal pages.

The worker advances `fencing -> purging -> verifying -> done` with exact journal ETags and a bounded lease. It durably marks uncertainty **before** each external phase, so a crash or lost lease cannot cause an unattended destructive replay. Only `ManagementTenantPurgePendingException`, representing positive knowledge of the same running native job, permits automatic resumption. Other uncertain outcomes pause and project `unknown`, including after a process dies and its lease expires. A trusted native operator must inspect authoritative consumer receipts and call `RunAsync(originalExecution, reconcileUncertain: true)` after repair. This port is never mapped to support HTTP. Verification failures may reconcile the same purge hook again explicitly; native receipts and fences remain intact. Never clear a journal row, invent another operation ID, reset a fence or reactivate the tenant to retry.

`CancelBeforeFenceAsync` is a trusted server port and succeeds only before any fence attempt. Once work starts, cancellation cannot roll back deletion or restore access; failures keep any established fences. Final success requires verified complete coverage for every required category. Retained/excluded scope remains visible. An explicitly partial final result stays partial; later retention handling belongs to the consumer's native process and must not be presented as a completed full purge.

Confirmation is an accepted durable command. Revoking a support assignment denies new submissions and polling; it does not retract already accepted native work. Workers use current native policy, the frozen intent, current dedicated realm/app binding and current host enablement, not an impersonated human session. Removing host authority pauses work. Native policy must recognize job-owned fenced/tombstoned tenants so legitimate status and recovery do not require reactivating them. Known same-intent confirmation receipts remain readable under current access/status policy after the original native version, plan lifetime or inventory changes; this is read-only and cannot create another job. New intents still require a fresh valid plan.

Worker audit is explicit metadata-only system attribution by app/org/job; `Succeeded=true` is emitted only after verified complete cleanup. Partial, pending and uncertain phases do not become successful business audit. Audit and native deletion are separate durable systems: audit failure must be repaired/backfilled from journal/consumer evidence, without repeating business work merely to recreate an audit entry. Journal/native tombstones preserve idempotency; no automatic journal deletion or access reset is installed.

No production purge is performed by these implementation cards. Physical backup and external-system coverage must be declared by each consumer; the framework does not promise deletion outside that inventory.

## Optional IBeam Identity bridge (IBM-0136)

`IBeam.Management.TenantData.Identity` composes the consumer's app handler with verified native Identity cleanup. It requires the standard Management journal/worker, a separate app access/write fence, and native providers that protect every supported tenant-owned writer. Default registration is disabled. It adds no HTTP route and does not depend on the Identity Target API or commercial modules.

For the Azure Table provider, deploy the protected native writers to **all** instances, pre-provision the ten distinct entity-family tables below, and set `IBeam:Identity:AzureTable:RequireTenantPurgeFencing` to true before enabling purge. Runtime purge paths never create tables. Existing ownership and administrator protection remains enabled for ordinary operations. Keep standard live tenant/membership request and refresh enforcement (or equivalent app enforcement); the app fence must also protect custom auth, anonymous invite paths and business writes. Native invitation, API-key, agent and consent lookup denies a tenant as soon as its metadata fence is established.

Register the consumer handler by concrete type, not as a second public `IManagementTenantPurgeHandler`:

```csharp
services.AddScoped<MyAppPurgeHandler>();
services.AddScoped<IManagementTenantPurgeAccessFence, MyAppPurgeFence>();
services.AddIBeamIdentityAzureTableTenantPurge();
services.AddIBeamManagementTenantPurgeIdentity<MyAppPurgeHandler>(o => o.Enabled = true);
services.AddIBeamManagementAzureTableTenantPurgeJournal(dedicatedPurgeTable);
services.AddIBeamManagementTenantPurgeJobs(configuration);
services.AddIBeamManagementTenantPurgeTargetApi(configuration, o => {
    o.Enabled = true;
    o.PolicyKeys = ["enterprise"];
});
```

Supply the existing app-owned policy/audit/access registrations and signed target trust/realm settings. The native provider uses that target's configured Identity `TableServiceClient` and table prefix; portal Identity storage is unrelated. Duplicate handlers/authorities, unprotected providers, aliased native table families or a missing app fence fail startup. Do not enable this bridge over an unsupported shared realm or a partially upgraded fleet of writers.

| Native inventory key | Default table | Scope removed |
| --- | --- | --- |
| `identity-tenant-metadata` | Tenants | Exact tenant row |
| `identity-tenant-memberships` | TenantUsers | Members, roles on members, owner/admin snapshots and native transfer receipts |
| `identity-user-membership-indexes` | UserTenants | Only that tenant's reverse index/default marker per user |
| `identity-tenant-roles` | Roles | Tenant-local role definitions |
| `identity-tenant-invitations` | TenantInvites | Pending/completed primary invitations and attributable current/stale token indexes |
| `identity-tenant-api-credentials` | ApiCredentials | Tenant-owned API credentials |
| `identity-tenant-agent-users` | AgentUsers | Tenant-owned agents |
| `identity-tenant-agent-credentials` | AgentUserCredentials | Scoped bindings and credential indexes |
| `identity-tenant-oauth-consents` | OAuthConsents | Tenant-specific consent grants |
| `identity-tenant-access-catalog-overrides` | AccessCatalogOverrides | Tenant-specific access catalog overrides |

Global users/passwords, OTP challenges, shared sessions, unscoped account authorization artifacts and preferences remain outside tenant purge authority. The same user can retain its roles, default tenant and live session in another tenant. Purging a tenant does not grant account-level deletion authority.

The bridge creates opaque native authorization only after verifying the consumed plan, confirmed job, exact original app/environment/org/realm/tenant/person/version and active worker lease. Native stores cannot accept a public DTO or general skip-owner flag instead. Journal-only lease proof is used when a fence validates itself, avoiding recursive rechecks of that same fence. `IIdentityTenantPurgeService.HasPurgeMarkerAsync` is a read-only operational binding check for app status policy after tenant metadata is removed; it grants no cleanup authority. New submissions still require an existing tenant and exact native revision.

Each native writer registers its physical partition before committing, then joins its row and tenant-specific local guard by exact ETag. A frozen root inventories delayed writers, including token partitions and shared user partitions. Fencing registers legacy attributable partitions, denies new registration and marks every known local partition. Cleanup removes one bounded page at a time with conditional deletes, retains checkpoints and verifies emptiness. A late prepared write loses its guard version and cannot recreate an index after cleanup. Native pending signals resume the same job; an uncertain delete reply pauses for explicit original-job reconciliation. Source selectors are compiled provider code, never portal inputs. Canonical key/tag attribution is required; ambiguous ownership fails closed rather than deleting another tenant's data.

Minimal permanent controls use the reserved `!IBEAM|PURGE|` row prefix and `PURGE|<tenant>` registry partitions. Their declared `identity-purge-tombstones` coverage is **retained**, including job-bound scope hashes, necessary tenant/user partition references and hashed-token partition references. They contain no invitation recipient/profile, raw token or credential secret. They are the persistence fence against late writers and tenant-ID recreation: do not drop/reset them or treat physical zero rows as the completion criterion. Raw table integrations must skip reserved controls. Ordinary owner/admin checks remain intact; deliberate whole-tenant deletion runs through the separately verified native path.


## Portal preflight, confirmation and status (IBM-0137)

Opt in on the portal with `services.AddIBeamManagementPortalTenantPurgeApi(configuration)`.
This installs the typed portal service and compiled contract only. The portal requires no native
Identity provider, purge journal, worker or application cleanup handler. A target must separately
install the signed dispatcher, durable purge coordinator, native policy and all access/write fences.
Existing app assignments, target credentials and connections are not upgraded automatically;
explicit `management.tenants.delete` authority is required at every boundary.

| Method and portal path (under `/api/management/applications/{applicationId}/tenants/{tenantId}`) | Input | Result |
| --- | --- | --- |
| POST `/purge-plans` | `{ "policyKey": "enterprise" }`, exact `If-Match`, `Idempotency-Key` | 200 planned inventory, plan ID, short expiry and confirmation binding; 202 unknown if the reply is ambiguous |
| POST `/purges` | `{ "policyKey": "enterprise", "planId": "...", "confirmationBinding": "..." }`, original exact `If-Match`, a **different** stable `Idempotency-Key` | 202 acceptance or unknown, original operation ID and `Location` for status |
| GET `/purges/{jobId}` | Original selected app/tenant and authenticated support person | 200 authoritative metadata or 202 unknown; never resumes or retries deletion |

Show the full plan inventory, retained/excluded categories and expiry before the user explicitly
confirms. Echo the returned policy, plan ID, binding and original version exactly. The binding is
public confirmation metadata, not a credential: the target still checks the original person,
app/environment/org/realm/tenant, inventory, native revision, expiry and one-use consumption.
Reusing a consumed plan for another job or substituting its scope fails. Same confirmed intent
may recover its receipt without repeating destruction. A preflight is read-only: if its reply is
unknown, do not enable confirmation until a successful explicit repeat using the same preflight
key recovers the plan (or the user requests a fresh plan). There is no automatic retry or job
status URL for an unconfirmed preflight.

Preserve the original confirmation key and returned operation ID/Location when a reply is lost.
Poll that Location; do not make a new confirmation with a new key. Schema errors, malformed
metadata and substituted job/plan IDs after sending are treated as unknown while the signed client
still has the authoritative original operation ID. Current support-user/app/assignment state,
connection and target machine grants/capability are checked again on every call. Revocation denies
new requests and polling; it does not retract already accepted native work.

Acceptance is not completed deletion. Render `requested`, `fencing`, `purging` and `verifying` as
in progress; preserve `partially-completed`, `failed`, `cancelled` and `unknown` as distinct outcomes.
A succeeded job can still declare policy-retained backups/audit metadata and excluded global
accounts: display its category coverage rather than claiming all customer data everywhere vanished.
No support resume, cancellation, artifact download, storage selector or arbitrary target proxy is
installed. Unknown native work can only be reconciled by the trusted consumer worker/operator.

Audit uses the existing metadata-only pipeline and correlation ID. The base event identifies the
app and tenant. Fixed `management.tenants.delete.portal.*` events use `SubjectId` for the plan or
job reference: `planned`, `confirmation`, `accepted`, and the fixed progress state names. Separate
confirmation and acceptance events link the original plan and job without copying the request.
Unknown, failed, cancelled and partial phase events have `Succeeded=false`; an accepted event
means durable command acceptance, not completed deletion. No confirmation binding, inventory hash,
policy/version body, provider exception, storage identifier, customer content or key is persisted.
The target's existing job journal and worker audit remain authoritative for execution phases.
