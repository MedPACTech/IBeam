# Management API: Ownership, Tenant Lifecycle and App Operations

Created 2026-10-27. Baseline: IBeam 2.16.0. The preceding Management API epics remain shipped.

Hubbsly epic: `6b9422fc-0205-4c3b-88cb-63ff7f114cd1`. Stage: `in_flight`. Six features, 25 cards, **152 points**.

Completed: **1/25 cards, 5/152 points**.

Feature points are sums of their cards. Implement in the order below; each card depends on its stated predecessors and must merge into development before the next starts. Future scope stays in triage until selected.

| Order | Feature | Points | Dependencies |
| --- | --- | ---: | --- |
| 1 | MLO-F01: Ownership and role safety | 34 | None |
| 2 | MLO-F02: Explicit operations and supported app extensions | 23 | MLO-F01 |
| 3 | MLO-F03: App-owned tenant export | 23 | MLO-F02 |
| 4 | MLO-F04: Recoverable tenant purge | 31 | MLO-F03 |
| 5 | MLO-F05: Committed membership events and consumer synchronization | 18 | MLO-F04 |
| 6 | MLO-F06: Shared target transport, telemetry and bulk invitation revocation | 23 | MLO-F05 |

## Shared requirements

Portal API -> Management services -> typed signed target client -> target API -> native/application services -> entity-focused stores. Custom operations use IServiceOperationExecutor. Business data handlers run only in the consumer target; commercial and Identity bridges remain optional.

Effective authority is the intersection of current support assignment, explicit operation grant, target credential, installed capability and host-owned policy. No support impersonation, arbitrary target proxy, customer business-data response in the support portal, global account deletion under tenant purge, or automatic privilege migration. Keep exact versions, replay guards, durable same-intent recovery and metadata-only audit.

Each card must build/pack its changed production packages, pass focused behavior and meaningful negative/provider tests, update its public contracts/docs, merge into development and record commit/test evidence before Done. Later integration cards validate fresh NuGet-only consumers. Never mark unimplemented criteria complete.

The live Hubbsly MCP exposes no dependency-edge fields or tools. Record ordered predecessor references in card descriptions and feature outcomes, and retain an explicit acyclic dependency graph in this JSON. Resolve references to real IBM source keys after publication.

The data-export/purge operation family is separate from Identity (`management.tenants.export`, `management.tenants.delete`). Ownership transfer belongs to Identity (`management.identity.owners.transfer`). These are proposed future capabilities until their implementation cards complete; no new stable package publication or production data operation is part of this backlog.

## MLO-F01: Ownership and role safety (34 points)

Role editing cannot silently strip authority outside its policy. Native and Management writers enforce an independent owner invariant, and explicit ownership transfer is atomic, versioned and app-scoped.

Feature dependencies: None.

Feature acceptance criteria:

- Ordinary role replacement cannot remove roles outside the selected target policy, including an Owner role excluded from provisioning.
- An eligible administrator does not substitute for an eligible owner. Concurrent native and support writes cannot remove the final owner.
- Ownership comes from configured immutable role membership, with a dedicated atomic transfer and no legacy OwnerUserId authority.
- Independent transfer grants, signed requests, exact versions, durable recovery and package-only tests cover the full support-to-target flow.

### IBM-0121 / MLO-001: Reject policy-bound role replacement that would strip an unassignable role

**5 points.** Lane: `done`. Order: 10. Dependencies: None.

Harden ManagementCustomerMembershipService.SetRolesAsync. Validate the roles being removed against the same target-owned policy and roles.assign authority before any mutation. Reject an out-of-policy removal instead of silently dropping Owner or another privileged role. Preserve existing replacement semantics for editable roles; no hardcoded Owner name, new privilege or implicit roles.read grant.

Paths/surfaces: `IBeam.Management.Identity.Target.Services/Memberships/ManagementCustomerMembershipService.cs`; `IBeam.Tests.Management.CustomerIdentity/CustomerMembershipHttpTests.cs`; `docs/management/customer-membership-lifecycle.md`.

Acceptance criteria:

- [x] A signed role update using a Member-only policy cannot remove an existing out-of-policy Owner or other privileged role, even if another administrator remains.
- [x] Rejected requests leave primary membership, secondary projection, native access version and mutation receipts unchanged; errors do not expose role/profile details.
- [x] An approved replacement and clearing editable roles still work; roles.assign alone is sufficient without roles.read or memberships.manage.
- [x] Exact retries and lost-projection recovery remain idempotent; stale member versions fail and concurrent native changes are never overwritten.
- [x] Real provider plus signed HTTP regressions cover mixed-role memberships, cross-tenant scope, empty/legacy role state and existing administrator protection.
- [x] Changed packages build/pack, public behavior is documented, and the card is merged to development before it is marked done.

Merged implementation: `60dd46d30c287fd10497d478c233d347ca08c4ef`.

24 focused provider/signed-HTTP/customer-access checks and 1,084 fresh NuGet-only regressions passed, zero failures/skips. Built/packed 31 production dependencies at private validation version 2.17.0-dev.mlo.121.1; inspected 16 management package roots; package-only portal/two-target smoke passed. Tests prove out-of-policy Owner/custom-role removal is rejected before receipts or primary/projection/access-version changes, approved replacement does not require read/membership grants, stale/concurrent writes do not overwrite newer authority, and role-removal projection recovery remains idempotent. The two-target enterprise test now proves narrow-policy denial and uses an explicit Member+Owner policy for an authorized atomic replacement. No stable package release or running app upgrade was performed.

Dependency keys: None.

### IBM-0122 / MLO-002: Enforce an independent last-owner invariant across native membership writers

**8 points.** Lane: `ready`. Order: 20. Dependencies: IBM-0121.

Add explicit target/native ownership policy and optional conditional ownership store contracts. Extend the tenant-partition guard to track owner protection separately from administrator protection. All supported role, membership disable/revoke and destructive native writers participate; do not hardcode Owner names or use the legacy tenant owner column. Define global account-disable and unsupported-writer behavior explicitly and fail closed when owner safety cannot be established.

Paths/surfaces: `IBeam.Identity/Interfaces/`; `IBeam.Identity.Services/Tenants/`; `IBeam.Identity.Repositories.AzureTable/Stores/`; `IBeam.Management.Identity.Target.Services/Memberships/`; `IBeam.Tests.Management.CustomerIdentity/`.

Acceptance criteria:

- [ ] Host-owned policy resolves active immutable owner role IDs within the tenant; invalid, ambiguous or missing policy cannot advertise owner-safe capabilities.
- [ ] A remaining Admin without an Owner does not satisfy owner protection; role removal, membership disable and revocation cannot remove the last eligible owner.
- [ ] Two same-snapshot writes against different owners have at most one winner using a shared persistence revision; no process-local lock is treated as the authority.
- [ ] Native role services, invitations/bootstrap and supported membership writers respect the guard; legacy direct destructive paths and global-disable paths have a documented fail-closed boundary.
- [ ] Providers lacking the new conditional protocol retain existing unrelated behavior and do not advertise ownership transfer; default configurations are not silently granted ownership authority.
- [ ] Race, retry, unavailable-provider and legacy-role tests preserve other tenants, global identity records and existing last-administrator protection.

Dependency keys: MLO-001.

### IBM-0123 / MLO-003: Implement authoritative owner lookup and atomic native ownership transfer

**8 points.** Lane: `triage`. Order: 30. Dependencies: IBM-0122.

Add an optional native ownership service and provider operation that changes the source and destination memberships plus guard in one tenant-partition transaction. Expose current owner IDs and versions from the same authoritative membership model; do not create another unsynchronized OwnerUserId field.

Paths/surfaces: `IBeam.Identity/Interfaces/`; `IBeam.Identity.Services/Tenants/`; `IBeam.Identity.Repositories.AzureTable/Stores/`; `IBeam.Tests.Management.CustomerIdentity/`.

Acceptance criteria:

- [ ] Transfer requires an active eligible destination membership in the same tenant and exact source, destination and ownership snapshot versions.
- [ ] A stable operation ID makes retries and lost replies reconcile to one transfer; source removal and destination grant never expose an ownerless intermediate state.
- [ ] Unrelated roles, other memberships, global user status, default tenant and commercial entitlements remain unchanged; destination is never implicitly reactivated.
- [ ] Single-owner versus multiple-owner semantics are explicit host policy; transfer cannot become an unrestricted role grant.
- [ ] Lookup is bounded and read-only, resolves current role membership and cannot report a stale legacy tenant-column owner.
- [ ] Provider races, rollback of unapplied transactions, projection repair and unsupported-adapter behavior are tested.

Dependency keys: MLO-002.

### IBM-0124 / MLO-004: Expose scoped owner transfer through target and portal Management APIs

**8 points.** Lane: `triage`. Order: 40. Dependencies: IBM-0123.

Add management.identity.owners.transfer and its fixed typed target/portal routes and clients, reusing the existing delegation, exact-version, idempotency and safe receipt mechanisms. Proposed portal path: POST /api/management/applications/{applicationId}/tenants/{tenantId}/ownership/transfers; target equivalent under /api/management-identity.

Paths/surfaces: `IBeam.Management/CustomerIdentity/`; `IBeam.Management.Identity.Target.Services/`; `IBeam.Management.Identity.Target.Api/`; `IBeam.Management.Services/CustomerIdentity/`; `IBeam.Management.Api/`; `IBeam.Management.Identity.Services/`.

Acceptance criteria:

- [ ] Transfer is independently authorized by live human assignment, machine capability, target policy and app/environment/realm binding; roles.assign does not imply transfer.
- [ ] Discovery advertises transfer only with a working atomic ownership adapter and enforcement protocol; older targets remain usable for their installed operations.
- [ ] Actor proofs bind operation, method, route, payload, idempotency and all required versions; reused or cross-app proofs fail.
- [ ] Portal polls are read-only and expose safe operation metadata; unknown outcomes do not trigger a second transfer or claim success.
- [ ] Metadata-only audit records source/destination IDs and operation outcome without customer profiles or credentials.
- [ ] Target/portal integration tests cover revoked assignments, stale versions, unsupported targets, independent permission ceilings and lost replies.

Dependency keys: MLO-003.

### IBM-0125 / MLO-005: Validate ownership compatibility and publish consumer migration guidance

**5 points.** Lane: `triage`. Order: 50. Dependencies: IBM-0124.

Complete package-only native and portal ownership coverage, update bootstrap policy/source and handoff docs, and document the upgrade from UI-only Owner guards and stale owner badges. Existing 2.16 contracts retain their defined editable-role replacement behavior, with unsafe removals rejected.

Paths/surfaces: `scripts/Test-ManagementCustomerIdentityPackages.ps1`; `samples/Management.Bootstrap/`; `docs/management/`; `IBeam.Tests.Management.CustomerIdentity/`.

Acceptance criteria:

- [ ] A fresh NuGet-only consumer proves ordinary role editing cannot strip ownership, explicit transfer succeeds, and last-owner and last-admin invariants both hold.
- [ ] Already-issued customer JWT/refresh behavior follows committed membership versions after transfer or disable; unrelated tenants remain usable.
- [ ] The synthetic demo uses real support/customer MFA, preserves existing seed access changes and contains no private credentials in source or package artifacts.
- [ ] Migration guidance identifies full replacement semantics, protected policy boundaries, authoritative owner discovery and required provider/configuration changes.
- [ ] No frontend UI, Qurvia implementation, stable publication or production deployment is claimed by this card.

Dependency keys: MLO-004.
## MLO-F02: Explicit operations and supported app extensions (23 points)

Typed operation descriptors replace borrowed permissions. Optional licence/seat adapters and registered app operations use the same capability, authorization and delegation rules as built-in Management operations.

Feature dependencies: MLO-F01.

Feature acceptance criteria:

- Unknown operations and arbitrary URL forwarding remain denied; app extensions are explicitly registered and typed.
- Licensing stays optional and reuses existing Licensing services; tenant access, licence status, seats and ownership remain separate authorities.
- Discovery, human/machine grants, signing, receipts and auditing agree on every registered operation.

### IBM-0126 / MLO-006: Introduce a bounded registered operation catalog and extension contracts

**8 points.** Lane: `triage`. Order: 60. Dependencies: IBM-0125.

Generalize fixed operation validation through an immutable host-owned descriptor registry while retaining all built-in 2.16 operation names. Descriptors bind namespace, method, route template, request/result schemas, installed handler, required grants, receipt protocol and audit action. Include explicit data-operation families management.tenants.export and management.tenants.delete without presenting business data as Identity DTOs.

Paths/surfaces: `IBeam.Management/`; `IBeam.Management.Services/`; `IBeam.Management.Identity.Target.Services/`; `IBeam.Management.Identity.Services/`; `IBeam.Tests.Management.CustomerIdentity/`.

Acceptance criteria:

- [ ] Names alone cannot grant or advertise authority; only installed registered handlers with valid nonoverlapping descriptors become capabilities.
- [ ] Wildcard namespaces, caller-defined routes/URLs, duplicate descriptors, unsafe schemas and incompatible versions fail closed at registration or discovery.
- [ ] Credential capabilities, human assignments, delegation, payload limits and audit allowlists use the same registry and preserve independent live access checks.
- [ ] Existing diagnostics-only, Identity-only and combined 2.16 targets remain compatible and acquire no new grants automatically.
- [ ] Typed extension contracts remain provider-neutral and do not force Identity, Licensing, Billing or consumer assemblies into core Management.

Dependency keys: MLO-005.

### IBM-0127 / MLO-007: Add optional licence suspend and restore Management adapters

**5 points.** Lane: `triage`. Order: 70. Dependencies: IBM-0126.

Provide independent management.licenses.suspend and management.licenses.restore descriptors and typed target/portal operations that delegate to ITenantLicenseService in an optional integration module. Do not reuse tenants.status or turn Billing state into runtime authorization.

Paths/surfaces: `IBeam.Management/`; `IBeam.Management.Licensing/ (new optional module)`; `IBeam.Management.Services/`; `IBeam.Management.Api/`; `IBeam.Tests.Licensing/`.

Acceptance criteria:

- [ ] Suspension and restoration require explicit independent grants and only affect the selected app/tenant/license.
- [ ] Restoration respects current licence validity, revocation, expiry and host policy; it cannot resurrect an invalid entitlement or reactivate memberships.
- [ ] Exact versions, idempotency, unknown-outcome reconciliation and safe audit cover both transitions.
- [ ] Missing Licensing adapters advertise no licence capability and do not affect Identity/diagnostic startup.
- [ ] Adapter tests reuse native Licensing rules and prove tenants.status alone cannot suspend or restore a licence.

Dependency keys: MLO-006.

### IBM-0128 / MLO-008: Add independent seat assignment and revocation Management adapters

**5 points.** Lane: `triage`. Order: 80. Dependencies: IBM-0127.

Expose management.seats.assign and management.seats.unassign through the optional Licensing module, delegating to ILicenseSeatAssignmentService. Use explicit license/assignment/member scope and existing capacity/eligibility policies.

Paths/surfaces: `IBeam.Management.Licensing/`; `IBeam.Management.Services/`; `IBeam.Management.Api/`; `IBeam.Tests.Licensing/`.

Acceptance criteria:

- [ ] Seat assignment and revocation have separate grants and do not inherit memberships.manage authority.
- [ ] Capacity, active licence, subject eligibility and tenant ownership are checked by existing Licensing services before writes.
- [ ] Retries cannot consume a second seat or revoke another assignment; concurrent requests have bounded deterministic outcomes.
- [ ] Seat operations do not create memberships, grant roles or change tenant/user status.
- [ ] Discovery and error handling cover absent adapters, exhausted seats, revoked access and uncertain provider outcomes.

Dependency keys: MLO-007.

### IBM-0129 / MLO-009: Wire extension discovery, grants and consumer permission migration

**5 points.** Lane: `triage`. Order: 90. Dependencies: IBM-0128.

Complete target/portal typed dispatch and safe operation listing for registered extensions; provide migration guidance for Qurvia licence, seat and owner routes currently borrowing other permissions. Default grants remain unchanged.

Paths/surfaces: `IBeam.Management.Services/`; `IBeam.Management.Api/`; `docs/management/`; `samples/Management.Bootstrap/`.

Acceptance criteria:

- [ ] A support user can invoke only advertised operations permitted by both current app assignment and machine credential.
- [ ] Borrowed permissions are not automatically upgraded to commercial, transfer, export or deletion authority.
- [ ] Client dispatch binds a registered descriptor and scoped resource IDs; there is no free-form target URL/method/body proxy.
- [ ] Mixed-version target tests cover unavailable operations and stable diagnostics/customer-management behavior.
- [ ] Migration examples explain consumer-owned handler registration and required re-grant/reconnect steps without embedding credentials.

Dependency keys: MLO-008.
## MLO-F03: App-owned tenant export (23 points)

An explicitly authorized durable export job invokes a target-owned exporter, records coverage and delivers data through a customer-authorized target channel. The support portal receives safe job status rather than business records.

Feature dependencies: MLO-F02.

Feature acceptance criteria:

- Export adapters define coverage and exclusions; unsupported targets do not claim complete export.
- Business payloads, credentials and download capabilities stay out of support responses, receipts, logs and audit.
- Leased work, cancellation, replay and artifact expiry have explicit durable behavior.

### IBM-0130 / MLO-010: Define export job, coverage manifest and target handler contracts

**5 points.** Lane: `triage`. Order: 100. Dependencies: IBM-0129.

Add optional IBeam.Management.TenantData.Target.Services and .Api modules with IManagementTenantExportHandler, versioned job DTOs and a coverage manifest. Define submission/status routes under /api/management-tenant-data and matching scoped portal routes. Separate private artifact references from safe public status.

Paths/surfaces: `IBeam.Management/ (tenant-data contracts)`; `IBeam.Management.TenantData.Target.Services/ (new)`; `IBeam.Management.TenantData.Target.Api/ (new)`; `IBeam.Management.Repositories.AzureTable/`.

Acceptance criteria:

- [ ] Contracts distinguish requested, running, partially completed, succeeded, failed, cancelled and expired states without ambiguous success.
- [ ] Coverage names expected tenant datasets, attachments and omitted/retained categories without placing their contents in Management metadata.
- [ ] Export is opt-in with independent capability, app/environment/tenant scope and a registered usable handler.
- [ ] Customer delivery authorization is a target-owned port; support permission alone does not authorize reading exported business data.
- [ ] Core contracts remain usable without native Identity or commercial packages.

Dependency keys: MLO-009.

### IBM-0131 / MLO-011: Implement durable export execution and private artifact delivery

**8 points.** Lane: `triage`. Order: 110. Dependencies: IBM-0130.

Implement leased export jobs, idempotent submission, resumable app handler work and target-owned artifact retention. App exporters enumerate tenant data, including historical memberships where their coverage contract requires it, without confusing current seat eligibility with data ownership.

Paths/surfaces: `IBeam.Management.TenantData.Target.Services/`; `IBeam.Management.Repositories.AzureTable/`; `IBeam.Tests.Management.TenantData/ (new)`.

Acceptance criteria:

- [ ] Duplicate submissions and worker crashes reconcile one stable job; leases prevent concurrent destructive/redundant phases.
- [ ] The handler defines snapshot/consistency boundaries and returns a complete or explicitly partial manifest; missing datasets cannot be reported as complete.
- [ ] Private artifacts use bounded lifetime and a customer-authorized retrieval path; guessed, expired, cross-tenant or revoked access is denied.
- [ ] Support DTOs, logs, audit and exceptions contain no payloads, filenames with PII, bearer download URLs, keys or storage credentials.
- [ ] Cancellation and cleanup are bounded and do not delete unrelated artifacts or another job output.

Dependency keys: MLO-010.

### IBM-0132 / MLO-012: Expose scoped portal export requests and safe progress polling

**5 points.** Lane: `triage`. Order: 120. Dependencies: IBM-0131.

Add typed export submit/status clients and Management API routes, reusing registered operation descriptors and signed delegation. Deliver artifacts through the app-owned recipient channel rather than proxying tenant content through the support API.

Paths/surfaces: `IBeam.Management.Services/ (tenant-data clients)`; `IBeam.Management.Api/ (tenant-data controllers)`; `IBeam.Management.TenantData.Target.Api/`.

Acceptance criteria:

- [ ] Every submit/poll checks current support app assignment, operation grant, target credential, realm and host policy.
- [ ] Unknown HTTP outcomes preserve one job reference and do not blindly launch another export.
- [ ] Recipient and destination are validated by trusted target policy; callers cannot supply arbitrary export delivery URLs or use support identity as customer identity.
- [ ] Status reports safe coverage/progress only; a separately authorized operator-download feature is not implied.
- [ ] Cross-app IDs, replayed actor assertions, unavailable targets and revoked assignments fail safely.

Dependency keys: MLO-011.

### IBM-0133 / MLO-013: Validate complete export coverage with a synthetic consumer hook

**5 points.** Lane: `triage`. Order: 130. Dependencies: IBM-0132.

Supply a synthetic app exporter and package-only tests covering data categories reported missing by consumers. Document the Qurvia implementation contract and manifest expectations; do not claim to implement Qurvia business-table exports in this repository.

Paths/surfaces: `samples/Management.Bootstrap/`; `IBeam.Tests.Management.TenantData/`; `docs/management/tenant-export.md (new)`; `scripts/`.

Acceptance criteria:

- [ ] Synthetic coverage includes facilities/settings/commercial metadata/history/attachments and data linked to removed or unseated members where explicitly declared.
- [ ] Tests prove business data reaches only the app-authorized recipient and never support metadata or telemetry.
- [ ] Expired artifacts, partial coverage, failed handlers, replay/restart and unavailable storage have explicit tested outcomes.
- [ ] Fresh NuGet-only installation advertises export only when the exporter and delivery adapters are installed.
- [ ] Consumer guidance defines required inventory and consistency decisions; no production export or new legal retention promise is made.

Dependency keys: MLO-012.
## MLO-F04: Recoverable tenant purge (31 points)

A distinct destructive workflow previews scope, confirms exact intent, blocks access and coordinates app cleanup with tenant-scoped Identity cleanup. It reports incomplete or retained data explicitly and preserves shared identities and other tenants.

Feature dependencies: MLO-F03.

Feature acceptance criteria:

- A status/deactivation grant never permits deletion; fresh authorization and an expiring version-bound confirmation are required.
- Application hooks own business tables/files and retention declarations; portal code never directly queries customer storage.
- Stable phases, write fencing and reconciliation prevent duplicate purge, cross-tenant deletion and false complete outcomes.

### IBM-0134 / MLO-014: Add purge planning, scope inventory and confirmation contracts

**5 points.** Lane: `triage`. Order: 140. Dependencies: IBM-0133.

Define IManagementTenantPurgeHandler and versioned preview/confirm/status DTOs. Proposed portal routes are POST .../tenants/{tenantId}/purge-plans and POST .../tenants/{tenantId}/purges. Bind confirmation to target app/environment/tenant, inventory/version, actor, expiration and the independent management.tenants.delete grant.

Paths/surfaces: `IBeam.Management/ (tenant-data contracts)`; `IBeam.Management.TenantData.Target.Services/`; `IBeam.Management.TenantData.Target.Api/`.

Acceptance criteria:

- [ ] Preflight is read-only and reports affected categories and retained/excluded scope without returning business data.
- [ ] Confirmation requires fresh server-validated support authentication, exact intent/version and an unexpired one-use plan reference.
- [ ] Tenant status, roles.assign, licence or export grants alone cannot plan/confirm purge.
- [ ] Stale inventory, changed scope, disabled credentials or missing required handlers prevent destructive execution.
- [ ] Global account deletion is explicitly excluded from tenant purge authority.

Dependency keys: MLO-013.

### IBM-0135 / MLO-015: Implement durable purge coordination and app-owned cleanup phases

**8 points.** Lane: `triage`. Order: 150. Dependencies: IBM-0134.

Coordinate write/access fencing, app-owned database/blob cleanup, resumable checkpoints and final verification under a stable purge job. Reconcile uncertain app work before retrying; no destructive rollback or resetting disabled access.

Paths/surfaces: `IBeam.Management.TenantData.Target.Services/`; `IBeam.Management.Repositories.AzureTable/`; `IBeam.Tests.Management.TenantData/`.

Acceptance criteria:

- [ ] A confirmed purge blocks new tenant access and app writes before deletion through an explicit target integration contract.
- [ ] All destructive phases are idempotent or support authoritative reconciliation with operation IDs; unknown outcomes never cause blind duplicate work.
- [ ] Worker concurrency, lease loss, partial failure, cancellation boundaries and restart have deterministic durable states.
- [ ] No final success is emitted until required app phases and coverage verification complete; retained categories remain visible as metadata.
- [ ] Hooks cannot receive arbitrary cross-tenant storage selectors from the Portal and do not expose payloads or secrets in audit.

Dependency keys: MLO-014.

### IBM-0136 / MLO-016: Implement optional tenant-scoped Identity cleanup without deleting shared accounts

**8 points.** Lane: `triage`. Order: 160. Dependencies: IBM-0135.

Add an optional IBeam.Management.TenantData.Identity bridge and provider cleanup ports for tenant memberships, roles, invitations, indexes and tenant metadata. Deliberate whole-tenant deletion has its own verified context rather than disabling last-owner checks for ordinary writers.

Paths/surfaces: `IBeam.Management.TenantData.Identity/ (new optional module)`; `IBeam.Identity/Interfaces/`; `IBeam.Identity.Services/`; `IBeam.Identity.Repositories.AzureTable/`.

Acceptance criteria:

- [ ] Only a validated confirmed purge context can run cleanup; no general-purpose skip-owner/skip-authorization flag is exposed.
- [ ] All tenant-scoped primary and secondary records are inventoried and reconciled, including pending invitations and stale indexes.
- [ ] Global user credentials, OTPs, sessions/preferences used by other tenants and unrelated tenant data are preserved; orphan account deletion requires separate explicit account-level authority.
- [ ] Concurrent invitation acceptance, role writes, membership creation and refresh cannot recreate access during or after purge fencing.
- [ ] Unsupported providers fail closed; partial cleanup reports progress and never claims atomic multi-table deletion.
- [ ] Real provider tests cover overlapping users/IDs across two tenants, interruptions and exact-scope deletion.

Dependency keys: MLO-015.

### IBM-0137 / MLO-017: Expose purge preflight, confirmation and progress through the Portal API

**5 points.** Lane: `triage`. Order: 170. Dependencies: IBM-0136.

Connect typed portal/target purge routes, current grants, signed confirmation, stable job receipts and safe polling. The Portal manages intent and progress only; the consumer target owns deletion.

Paths/surfaces: `IBeam.Management.Services/ (tenant-data clients)`; `IBeam.Management.Api/ (tenant-data controllers)`; `IBeam.Management.TenantData.Target.Api/`.

Acceptance criteria:

- [ ] Preflight/confirm/poll bind the same app, tenant, actor and plan; replay, resource substitution and stale confirmation fail.
- [ ] Revoked support assignment or machine capability is enforced live, including on progress polling and any attempted resume.
- [ ] Lost target replies preserve an authoritative status reference and do not create another destructive intent.
- [ ] Responses distinguish completed, partially completed, retained, failed and unknown work without leaking customer data.
- [ ] Audit records attributable IDs, confirmation/result metadata and phase outcome without secrets or raw exception details.

Dependency keys: MLO-016.

### IBM-0138 / MLO-018: Verify purge completeness, shared-user isolation and consumer recovery guidance

**5 points.** Lane: `triage`. Order: 180. Dependencies: IBM-0137.

Add a synthetic app purge hook and fault-injected package-only end-to-end tests. Define consumer inventory and retention coverage, backup/external-system exclusions and operator recovery procedures.

Paths/surfaces: `samples/Management.Bootstrap/`; `IBeam.Tests.Management.TenantData/`; `docs/management/tenant-purge.md (new)`; `scripts/`.

Acceptance criteria:

- [ ] A synthetic complete purge removes declared app data/files and tenant Identity records while an overlapping user remains usable in another tenant.
- [ ] Every deletion phase is interrupted and resumed without double execution, revived access or false completion.
- [ ] Retention, backups and external systems have explicit coverage/exclusion metadata; the API does not promise deletion outside the handler contract.
- [ ] Package-only tests reject missing write fences, unsupported cleanup adapters, cross-app proofs and expired confirmations.
- [ ] The demo never purges non-synthetic tenants and the implementation card runs no production purge.

Dependency keys: MLO-017.
## MLO-F05: Committed membership events and consumer synchronization (18 points)

Consumers can maintain their own member/owner projections from committed versioned Identity changes, with durable delivery, idempotent replay and bounded reconciliation.

Feature dependencies: MLO-F04.

Feature acceptance criteria:

- Events describe committed state and stable operation/version identities across native and management writes.
- Delivery failure cannot lose a committed change or cause a business mutation to replay.
- Consumer synchronization does not borrow roles.assign or expand customer data access.

### IBM-0139 / MLO-019: Define versioned role, membership and ownership lifecycle events

**5 points.** Lane: `triage`. Order: 190. Dependencies: IBM-0138.

Add provider-neutral event contracts for role replacement/grant/revoke, membership enable/disable and owner transfer, linked to the same native mutations used by ordinary customer and Management flows. Keep event contents scoped IDs/role IDs/versions and trusted actor metadata.

Paths/surfaces: `IBeam.Identity/Events/`; `IBeam.Identity/Interfaces/`; `IBeam.Identity.Services/Tenants/`; `IBeam.Management.Identity.Target.Services/`.

Acceptance criteria:

- [ ] Native and support-origin changes produce the same versioned event shapes with tenant, subject, operation and before/after version identities.
- [ ] Events represent committed changes only; rejected/no-op/replayed operations do not invent another logical state change.
- [ ] Ownership transfer links both membership changes under one transfer identity without an ownerless intermediate event.
- [ ] No credentials, tokens, profiles or consumer business data enter the event contracts.
- [ ] Compatibility and event ordering/deduplication rules are documented for existing invite-event consumers.

Dependency keys: MLO-018.

### IBM-0140 / MLO-020: Persist and dispatch membership events through a durable outbox

**8 points.** Lane: `triage`. Order: 200. Dependencies: IBM-0139.

Persist committed mutation and event intent under the same provider transaction/fence where supported, with explicit repair/reconciliation for projections. Add leased dispatch, retries and idempotent consumer acknowledgement; do not claim exactly-once external delivery.

Paths/surfaces: `IBeam.Identity.Services/`; `IBeam.Identity.Repositories.AzureTable/`; `IBeam.Identity/Interfaces/`; `IBeam.Tests.Management.CustomerIdentity/`.

Acceptance criteria:

- [ ] A crash after membership commit cannot lose the event; a crash after dispatch may redeliver the same event ID without repeating the mutation.
- [ ] Provider contracts bind outbox writes to the same membership revision; unsupported adapters do not claim durable delivery.
- [ ] Per-tenant/version ordering and bounded retry/dead-letter behavior support reconciliation after failures.
- [ ] Leases and acknowledgements resist duplicate workers; replay preserves original actor/operation attribution.
- [ ] Tests cover every commit/dispatch boundary and ensure event failures do not leak private provider messages.

Dependency keys: MLO-019.

### IBM-0141 / MLO-021: Add consumer projection hooks and bounded reconciliation

**5 points.** Lane: `triage`. Order: 210. Dependencies: IBM-0140.

Provide an optional target-local consumer hook and reconciliation port for application member rows. Document replacing Qurvia members/sync routes that currently borrow roles.assign with event-driven synchronization and an independently authorized repair operation when exposed.

Paths/surfaces: `IBeam.Identity/Interfaces/`; `IBeam.Management/ (extension descriptors)`; `samples/Management.Bootstrap/`; `docs/management/`.

Acceptance criteria:

- [ ] The synthetic consumer deduplicates event IDs and refuses to apply an older membership version over newer state.
- [ ] Reconciliation reads bounded authoritative tenant membership state and repairs only the declared consumer projection.
- [ ] Any support-triggered repair has its own registered grant and safe metadata results; roles.assign is insufficient.
- [ ] Consumer failure/restart can be retried without changing native membership, roles or ownership.
- [ ] Guidance describes the Qurvia adapter boundary without shipping Qurvia business code in IBeam.

Dependency keys: MLO-020.
## MLO-F06: Shared target transport, telemetry and bulk invitation revocation (23 points)

Registered built-in and app operations share signed transport and safe telemetry. Bulk invitation revocation is bounded, version-aware and resumable, and the complete module set has a tested consumer handoff.

Feature dependencies: MLO-F05.

Feature acceptance criteria:

- Every app operation has correlation, timing, outcome and attributable safe audit without payload or credential logging.
- Shared transport retains exact target/route/authority binding and production HTTPS/DNS protections.
- Bulk revocation reports per-item results and never revokes accepted invitations or creates duplicate destructive work.

### IBM-0142 / MLO-022: Provide one typed signed target sender for registered app operations

**8 points.** Lane: `triage`. Order: 220. Dependencies: IBM-0141.

Centralize the existing signed HTTP primitives behind a supported typed IManagementTargetOperationSender. Migrate built-in and synthetic extension clients; provide a migration path for the Portal ManagementTargetSender workaround without accepting arbitrary routes or target origins.

Paths/surfaces: `IBeam.Management/ (transport ports)`; `IBeam.Management.Services/`; `samples/Management.Bootstrap/`; `docs/management/`.

Acceptance criteria:

- [ ] A sender resolves only registered operation descriptors and the selected live connection, method/template and scoped resource IDs.
- [ ] Production HTTPS, DNS/socket pinning, TLS, deadlines, cancellation, response limits and secret reference handling remain intact.
- [ ] Delegated actor, request bytes, resource scope, idempotency and versions are signed once and validated at the target.
- [ ] Redirects, arbitrary URLs/methods, undeclared request shapes and response payload leakage are rejected.
- [ ] Compatibility tests show equivalent built-in authorization and a working typed synthetic custom route.

Dependency keys: MLO-021.

### IBM-0143 / MLO-023: Instrument built-in and app-specific target calls with safe telemetry

**5 points.** Lane: `triage`. Order: 230. Dependencies: IBM-0142.

Add common correlation and duration/outcome telemetry around the shared target sender and metadata-only audit on registered operations. Include app-specific failures currently invisible to Portal telemetry.

Paths/surfaces: `IBeam.Management.Services/`; `IBeam.Management.Services/Audit/`; `IBeam.Tests.Management.Diagnostics/`; `docs/management/`.

Acceptance criteria:

- [ ] Success, rejection, timeout, network failure, cancellation and unknown mutation outcome produce correlated bounded operation telemetry.
- [ ] Telemetry records app/tenant/operation identifiers and safe error classes, never request/response bodies, Authorization headers, keys, OTPs or raw provider errors.
- [ ] Existing diagnostics sanitization settings are respected without weakening transport/audit redaction.
- [ ] Tracing failures cannot grant authority, retry a destructive request or change the operation result.
- [ ] Tests cover the synthetic custom route and prove secret/profile canaries are absent from every emitted record.

Dependency keys: MLO-022.

### IBM-0144 / MLO-024: Add bounded version-aware bulk invitation revocation

**5 points.** Lane: `triage`. Order: 240. Dependencies: IBM-0143.

Expose a registered independently discoverable bulk-revoke operation with bounded explicit invite IDs or a protected snapshot selection. Reuse native invite transitions, stable per-item intents and safe job/status results rather than an unbounded browser loop.

Paths/surfaces: `IBeam.Management/CustomerIdentity/`; `IBeam.Management.Identity.Target.Services/Invitations/`; `IBeam.Management.Identity.Target.Api/`; `IBeam.Management.Services/`; `IBeam.Management.Api/`.

Acceptance criteria:

- [ ] Batch limits, actor/tenant/app scope, selection versions and independent bulk authority are enforced before work starts.
- [ ] Each item reports revoked, already revoked, accepted/not eligible, changed version, failed or unknown; partial completion is explicit.
- [ ] Retries/resume reuse per-item operation identities and do not revoke accepted invitations, resend mail or create another batch.
- [ ] Concurrent acceptance and revoke retain the existing native exact-version guarantees.
- [ ] Cancellation, revoked grants, rate limits and provider failures leave safe resumable progress and correlated telemetry.

Dependency keys: MLO-023.

### IBM-0145 / MLO-025: Complete package-only regression, demo and frontend migration handoff

**5 points.** Lane: `triage`. Order: 250. Dependencies: IBM-0144.

Validate the six features as an optional package graph, update synthetic hooks/demo Swagger/OpenAPI and the baseline application prompt, and document migration from borrowed permissions and custom signing. Prepare release guidance without publishing a stable version or performing production lifecycle work.

Paths/surfaces: `scripts/`; `samples/Management.Bootstrap/`; `docs/management/`; `docs/planning/`; `IBeam.Tests.Management.TenantData/`.

Acceptance criteria:

- [ ] A fresh package-only consumer runs ownership, explicit commercial operations, export/purge hooks, event delivery, telemetry and bulk revoke with real scoped authentication.
- [ ] Negative tests cover cross-app/realm, shared users, stale versions, missing adapters, assignment revocation and private-data canaries.
- [ ] Old diagnostics-only and 2.16 Identity-only consumers remain usable and gain no new destructive/download/commercial rights.
- [ ] Bootstrap setup preserves passwords, keys and changed access, uses synthetic data only and exposes an accurate Development API document.
- [ ] Frontend handoff lists real operation names/routes, required grants, status/unknown semantics, confirmation flow and data-delivery boundaries.
- [ ] Build/pack, meaningful tests and package content inspection pass; release publication remains a separate explicit request.

Dependency keys: MLO-024.
