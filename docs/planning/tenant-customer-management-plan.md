# Tenant and Customer User Management

Date: 2026-10-19. Status: implementation in progress; complete and merge cards in order.

Hubbsly: iBeam project / Tenant and Customer User Management / 42983501-610a-498d-a61e-b89469759f09. Epic stage: in_flight. IBM-0098 through IBM-0108 are complete (58 points); 9 cards remain in Triage. The epic is visible in active Work Epics.

Estimate: **104 points across 8 features and 20 cards**. Card estimates use Fibonacci points; feature estimates are their sums and are included in Hubbsly feature outcomes.

IBeam Management 2.15.0; original Management API epic is shipped. This is a new follow-on epic, not a reopening of completed work.

## Outcome

A developer can select an application in one support portal, browse its customer tenants and users, create an enterprise customer tenant, invite its first administrator, invite additional customers, and manage tenant-specific membership/roles and tenant status. Customer invitation acceptance and sign-in remain in the customer application. Self-service signup continues alongside operator-assisted onboarding.

An application registration represents one app/environment target. A support organization contains portal staff. A customer tenant represents an enterprise/customer organization inside that target application. These are distinct scopes.

The planned grants authorize customer operations across the explicitly selected app/environment, subject to target host policy; they do not create automatic authority across other apps. A target must explicitly opt into these capabilities. Per-customer-tenant support assignments are not a new portal feature in this epic; narrower scopes can be enforced only by a trusted target scope/policy adapter, not inferred from diagnostic records.

## Architecture and projects

Portal API -> Management services -> scoped HTTPS client -> target Identity Management API -> Identity management services -> existing Identity services/stores or entity-focused repositories. Custom service operations execute through IServiceOperationExecutor. Customer Identity is registered only in the target app; the support host retains its isolated identity realm.

- Existing IBeam.Management: provider-neutral customer-management DTOs, ports, operation names and delegated actor contracts.
- Existing IBeam.Management.Services: portal orchestration, live assignments, scoped HTTP client, safe forwarding, receipt reconciliation and signed actor assertions.
- Existing IBeam.Management.Api: thin portal controllers under /api/management/applications/{applicationId}.
- New IBeam.Management.Identity.Target.Services: optional target orchestration over existing customer Identity services. This is separate from IBeam.Management.Identity.Services, which handles support identity.
- New IBeam.Management.Identity.Target.Api: optional target controllers and dedicated named authentication under /api/management-identity.
- Existing IBeam.Management.Repositories.AzureTable and IBeam.Identity.Repositories.AzureTable: entity-focused metadata adapters and additive bounded Identity query/concurrency ports.
- New IBeam.Tests.Management.CustomerIdentity and samples/Management.CustomerIdentity.PackageSmoke: isolated verification and package consumption.
- Existing portable Management API host can consume these packages later. IBeam.ADMIN.API remains a temporary physical host repository; no new repository or rename is required.

Target customer Identity and support Identity stay in different service containers/realms. The portal never registers a target customer database connection or reads customer tables directly. A dedicated app Identity realm is the default; shared multi-app realms require a trustworthy app-scope adapter and otherwise fail closed.

## Storage

- Reuse each target app's existing Identity tenant/user/membership/role/invitation stores. No new customer SystemUsers/SystemOTP realm and no copy of customer directories in the portal.
- Extend existing portal ManagementApplicationAccess and target ManagementApiCredentials with explicit additional operations; existing rows gain no authority.
- New target metadata: ManagementIdentityOperations (idempotency, steps, safe results, audit intent); ManagementTenantAdministration (provenance, optional external-reference uniqueness and operation linkage); bounded ManagementDelegationReplays (single-use mutation assertions). Prefix all target metadata separately from support Identity tables.
- Optional directory indexes are derived target-local metadata only, added only if provider query behavior requires them, with trusted idempotent backfill and documented consistency.
- Initial concrete provider is Azure Table. Provider-neutral ports and fail-closed capability discovery allow optional future adapters without claiming unimplemented EF support.

New target tables and optional indexes are provisioned by trusted setup, not runtime readers. Logical prefixes alone are not a database permission boundary. Support Identity may retain its existing separate-store or shared-store/prefix configuration; customer Identity remains owned/configured by each target.

## Authorization, operations and audit

Every operation requires an enabled app connection, live support assignment, independent human operation permission, target machine capability and target host policy. No support administrator bypass, customer impersonation, raw credential/token output, customer business records, global customer-account disable, or direct portal access to customer storage.

The management.identity namespace has independently assigned tenants.read/create/update/status, users.read/provision, invites.read/manage, memberships.manage, roles.assign and onboarding.create/read/retry operations. Composite actions require the underlying grants too. Operation names and accepted capability versions are finalized in TCM-001/002 before implementation of dependent cards.

A dedicated target machine credential and a short-lived signed support actor assertion travel server-to-server. The target verifies configured issuer/audience/keys, operation, app/resource and request digest. Mutation assertions have atomic replay protection. A support actor is never converted into a fake customer principal. Human grant changes stop new portal requests; existing assertions expire within at most 60 seconds and in-flight request limits are documented.

All mutating requests require Idempotency-Key and durable target operation receipts. Resource updates also require a version/If-Match. Audit intent is durable before a new mutation; later delivery retries do not repeat the mutation. Audit carries actor/resource IDs, safe outcomes and correlation only, with no raw request/profile/credential/invitation payloads.

## Proposed endpoint inventory

Portal prefix P = /api/management/applications/{applicationId}. Target prefix T = /api/management-identity. The same customer resource paths exist beneath P and T unless noted. These are proposed contracts, not available in 2.15.0.

| Method | Path beneath P / T | Purpose |
| --- | --- | --- |
| GET | P/customer-management; T/info | Effective customer-management availability and supported operations |
| GET | /tenants; /tenants/{tenantId} | Paged customer tenant directory and detail |
| POST / PATCH | /tenants; /tenants/{tenantId} | Create / rename tenant |
| POST | /tenants/{tenantId}/deactivate; /tenants/{tenantId}/activate | Disable / reactivate tenant access |
| GET | /tenants/{tenantId}/users; /tenants/{tenantId}/users/{userId} | Tenant-scoped customer directory and detail |
| GET | /tenants/{tenantId}/assignable-roles | Target-approved tenant role catalog |
| POST | /tenants/{tenantId}/users/provision | Invite/provision one customer into the tenant |
| PATCH | /tenants/{tenantId}/users/{userId}/membership | Enable / disable this membership |
| PUT | /tenants/{tenantId}/users/{userId}/roles | Replace approved tenant roles with concurrency checks |
| GET | /tenants/{tenantId}/invites; /tenants/{tenantId}/invites/{inviteId} | Paged invitation list and detail |
| POST | /tenants/{tenantId}/invites/{inviteId}/resend; /tenants/{tenantId}/invites/{inviteId}/revoke | Manage existing invitation |
| POST | /enterprise-onboardings | Create enterprise tenant plus initial administrator invitation |
| GET | /operations/{operationId} | Poll durable operation and safe progress/result |
| POST | /enterprise-onboardings/{operationId}/retry | Resume authorized unfinished onboarding steps |

Directory pages default to 50 and cap at 100, with provider scan/request/deadline budgets and authenticated context-bound continuations. Initial filters are deliberately bounded; no global customer email lookup or arbitrary query interface is exposed. Error/log sanitization options do not control Identity fields.

## Enterprise onboarding flow

1. Support selects an authorized application/environment and an enabled target-owned onboarding policy.
2. Submit tenant name, initial administrator email, optional external reference and an idempotency key.
3. Target reserves stable resource IDs, creates the tenant through Identity and runs required idempotent extension initialization.
4. Target creates/delivers one recoverable administrator invitation using host-approved roles and redirect destinations.
5. Portal shows created, delivery failed/unknown, awaiting acceptance or completed states and permitted recovery actions.
6. The customer accepts through the application Identity flow, proves recipient ownership and completes required password/auth setup. Completion is reconciled from actual invitation/membership state.

Support staff never receive customer passwords or redemption tokens and never acquire customer membership as a side effect. Existing customers can accept an enterprise invitation while retaining their self-service tenants. Email delivery is at-least-once where the provider cannot prove delivery; uncertain delivery is visible and does not create another tenant or invitation.

## Feature estimates and implementation order

| Order | Feature | Cards | Points |
| --- | --- | --- | ---: |
| TCM-F01 | Opt-in customer Identity management and scoped authorization | IBM-0098, IBM-0099, IBM-0100 | 15 |
| TCM-F02 | Tenant and customer user directories | IBM-0101, IBM-0102, IBM-0103 | 15 |
| TCM-F03 | Reliable tenant creation and administration | IBM-0104, IBM-0105, IBM-0106 | 15 |
| TCM-F04 | Enterprise onboarding and first administrator invitation | IBM-0107, IBM-0108 | 13 |
| TCM-F05 | Customer provisioning and invitation management | IBM-0109, IBM-0110, IBM-0111 | 15 |
| TCM-F06 | Tenant and membership access lifecycle | IBM-0112, IBM-0113 | 10 |
| TCM-F07 | Consumer policies and self-service signup compatibility | IBM-0114, IBM-0115 | 8 |
| TCM-F08 | End-to-end verification and baseline application handoff | IBM-0116, IBM-0117 | 13 |

Cards below are ordered for one branch/build/merge cycle at a time. Start from current development, implement only the selected card, run its focused checks, merge according to repository rules and update Hubbsly with evidence before taking the next card. Dependencies are explicit; a later feature may add optional policy composition after the foundational policy contracts exist.

## TCM-F01: Opt-in customer Identity management and scoped authorization

Estimate: **15 points**. Hubbsly feature: 47ffdc73-dc0d-4d9d-9364-23764784273e.

Add optional target Identity management packages and explicit customer-management capabilities without mixing customer and support identities. Done means a verified named support actor can perform only an explicitly granted operation against one registered app/environment.

Feature dependencies: Management API 2.15.0.

Feature acceptance:

- [x] Targets advertise only installed and enabled customer-management operations; existing diagnostics-only deployments remain compatible.
- [x] Effective access is the intersection of live human assignment, target credential capability and host-owned policy; no implicit support-admin bypass.
- [x] Support actor attribution is verifiable at the target and cannot become a customer identity or membership.

### IBM-0098 / TCM-001: Define optional target packages, DTOs and versioned capability discovery

Estimate: **5 points**. Lane: Done. Delivery order: 10. Hubbsly ID: e2c80d58-a8e9-458c-ac78-75f45682ed58.

Dependencies: Management API 2.15.0.

Create IBeam.Management.Identity.Target.Services and IBeam.Management.Identity.Target.Api as optional NuGet modules. Put provider-neutral DTOs/query ports in IBeam.Management; portal integration remains in IBeam.Management.Services and IBeam.Management.Api. Define GET /api/management-identity/info and GET /api/management/applications/{applicationId}/customer-management. Extend the typed connection/capability model to support diagnostics-only, Identity-only and combined targets, with explicit module/version negotiation.

Proposed paths: IBeam.Management/CustomerIdentity/, IBeam.Management.Identity.Target.Services/, IBeam.Management.Identity.Target.Api/, IBeam.Management.Services/ManagementTargetConnectionProbe.cs, IBeam.Management.Services/ManagementApplicationConnectionStateProvider.cs, IBeam.Management.Api/, IBeam.sln, IBeam.Tests.Management.CustomerIdentity/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] Both new packages build/pack independently; core and portal packages do not acquire a dependency on customer IBeam.Identity or on the support Identity adapter.
- [x] No target customer routes or customer capabilities are registered without explicit opt-in; authentication defaults and existing customer routes remain unchanged.
- [x] Discovery validates application key, environment, Identity realm scope, protocol version and recognized installed capabilities; missing adapters never advertise a working operation.
- [x] An Identity-only target can be connected without installing diagnostic readers; a diagnostics-only target still works with existing 2.15.0 contracts.
- [x] Unknown or incompatible Identity protocol versions fail only customer-management availability with a safe explanation; diagnostics capability/sanitization validation remains intact.
- [x] DTOs allow only framework identity metadata; passwords, hashes, security stamps, OTPs, tokens, secrets and application profile/business payloads are excluded.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 388 tests passed with zero failures/skips; both optional target packages built/packed and inspected. See Hubbsly IBM-0098 for full delivery evidence.

### IBM-0099 / TCM-002: Extend app assignments and machine credentials with independent Identity grants

Estimate: **5 points**. Lane: Done. Delivery order: 20. Hubbsly ID: b6be9778-a582-4143-a491-d74a70f00f79.

Dependencies: IBM-0098.

Add independent management.identity operations: tenants.read/create/update/status, users.read/provision, invites.read/manage, memberships.manage, roles.assign, onboarding.create/read/retry. Extend credential issuance validation, connection state, assignment validation, delegation rules and selector capability projection. An operation matrix documents required read/write grants; composite onboarding requires the underlying tenant-create, invite-management and approved-role grants as well as onboarding permission.

Proposed paths: IBeam.Management/ManagementClaims.cs, IBeam.Management/ManagementApplicationAccessContracts.cs, IBeam.Management.Services/Credentials/, IBeam.Management.Services/ManagementApplicationAccessService.cs, IBeam.Management.Services/ManagementApplicationConnectionStateProvider.cs, IBeam.Tests.Management.CustomerIdentity/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] Every new operation is independently recognized on human assignments and target credentials; errors/logs permissions grant no Identity access.
- [x] Effective permissions are intersected with target-advertised capabilities and host policy; disabled/revoked users, apps, connections and assignments fail on the next authorization check.
- [x] Delegators must hold explicit per-operation delegation authority and cannot grant themselves or another support user broader access; management administrator alone grants no customer access.
- [x] Existing stored diagnostics credentials, assignments and secret references continue to work and receive no new Identity permissions during upgrade.
- [x] Direct service calls enforce the same operation matrix through IServiceOperationExecutor; request-supplied role names, organization IDs or actor headers cannot authorize operations.
- [x] Selector/customer-management responses report effective supported operations for a user assigned only customer capabilities; no app enumeration across support organizations occurs.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 491 unique tests passed, including both Identity storage modes; no failed or outstanding skipped cases. See Hubbsly IBM-0099 for delivery evidence.

### IBM-0100 / TCM-003: Verify delegated support actors at target and preserve separate audit identities

Estimate: **5 points**. Lane: Done. Delivery order: 30. Hubbsly ID: 9f53c03f-96b0-4164-96b1-ec4762bcc435.

Dependencies: IBM-0098, IBM-0099.

Introduce IManagementDelegatedActorProvider and a target validator using short-lived asymmetrically signed actor assertions in a dedicated header, alongside the deployment-scoped machine key. Trust issuer/audience/signing keys through operator configuration; bind organization, support actor, app/environment, operation, route/resource, canonical request digest, expiry and assertion ID. Keep verified external actor context separate from target customer principal/tenant membership. Reuse machine validation services with a dedicated /api/management-identity named authentication scheme rather than broadening ordinary customer routes.

Proposed paths: IBeam.Management/CustomerIdentity/, IBeam.Management.Services/CustomerIdentity/Delegation/, IBeam.Management.Identity.Target.Api/Authentication/, IBeam.Management.Identity.Target.Services/Authorization/, IBeam.Management.Services/ManagementAuditRecorder.cs, IBeam.Tests.Management.CustomerIdentity/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] Missing, forged, expired, wrong audience/issuer/key, wrong app/operation/resource or body-mismatched assertions fail before customer store access; arbitrary actor headers are ignored.
- [x] Mutation assertions are single-use with an atomic bounded replay store; legitimate retries use a fresh assertion and the original idempotency key.
- [x] Target host policy and live credential status/capabilities still authorize the operation; a signed actor assertion never adds machine authority.
- [x] Only configured algorithms and trusted rotated signing keys are accepted; remote key discovery is never driven by request URLs, and signing secrets/keys remain outside source and card payloads.
- [x] Audit records separately identify support organization/user, target credential, app/environment, operation and correlation; no synthetic customer user, customer token or tenant membership is created for the operator.
- [x] Removing an app grant blocks new portal requests; a previously issued assertion has at most a documented 60-second lifetime and in-flight behavior is documented and tested.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 521 tests passed, zero failures/skips; five affected packages packed and inspected. See Hubbsly IBM-0100 for delivery evidence.

## TCM-F02: Tenant and customer user directories

Estimate: **15 points**. Hubbsly feature: e4267e25-17c7-4d49-8366-49c133aadce2.

Let authorized support staff browse customer tenants and tenant-scoped users, including pending invitations, in the selected app. Reads are bounded and expose framework identity metadata only.

Feature dependencies: TCM-F01.

Feature acceptance:

- [x] Tenant list/detail and tenant-user list/detail work against actual target Identity storage with bounded paging.
- [x] Pending/disabled statuses are explicit and customer membership in another tenant or app is never revealed.
- [x] Portal reads enforce live grants and do not copy customer directories into portal storage.

### IBM-0101 / TCM-004: Implement bounded target tenant directory and detail reads

Estimate: **5 points**. Lane: Done. Delivery order: 40. Hubbsly ID: a5580dbc-b6d4-455a-9748-288b9b2d9c49.

Dependencies: IBM-0100.

Add IManagementTenantDirectoryService and an optional paged query port rather than GetAllAsync/full-realm materialization. Implement Azure Table reads of existing Tenants metadata; GET /api/management-identity/tenants and /tenants/{tenantId}. Support pageSize (default 50, max 100), continuation, status and bounded normalized-name prefix search. Default target binding requires a dedicated customer Identity realm; multi-app shared realms require an explicit trusted app-scope adapter or registration fails.

Proposed paths: IBeam.Management/CustomerIdentity/, IBeam.Management.Identity.Target.Services/Tenants/, IBeam.Identity.Repositories.AzureTable/Stores/, IBeam.Management.Identity.Target.Api/Controllers/ManagementTenantsController.cs, IBeam.Tests.Management.CustomerIdentity/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] Existing and newly created tenants return ID, name, status, created/updated timestamps and safe version metadata only; no support-organization tenants or consumer extension records appear.
- [x] Provider queries use bounded pages, scan/time budgets and cancellation; no complete directory is loaded to simulate paging/search.
- [x] Continuation tokens are authenticated and bound to target realm/app/environment, operation, filters and expiry; cross-app/filter replay and tampering fail.
- [x] Name-prefix and status behavior, sort order, empty pages with continuation and any optional index/backfill requirements are documented and tested with real isolated Table fixtures.
- [x] Dedicated-realm configuration is explicit; ambiguous multi-app shared Identity storage fails closed unless the host supplies a trusted app-scope filter.
- [x] Read credentials require no schema-creation or write rights; not-found/denial responses do not reveal tenants outside the authorized target scope.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 462 tests passed, zero failures/skips, including signed HTTP over read-only real storage. See Hubbsly IBM-0101 for delivery evidence.

### IBM-0102 / TCM-005: Implement tenant-scoped customer user and invitation directory reads

Estimate: **5 points**. Lane: Done. Delivery order: 50. Hubbsly ID: 27348e0a-7107-41b9-9fb7-1cd4adbee681.

Dependencies: IBM-0101.

Add IManagementCustomerUserDirectoryService with paged Identity membership/invitation query ports. GET /api/management-identity/tenants/{tenantId}/users and /users/{userId}; use separate invite routes for invitation metadata. Reuse existing Identity membership, directory and invite models without returning IdentityUser directly. Avoid the existing unbounded ListAsync/GetUsersForTenantAsync APIs for large directories.

Proposed paths: IBeam.Management/CustomerIdentity/, IBeam.Management.Identity.Target.Services/Users/, IBeam.Identity.Repositories.AzureTable/Stores/AzureTableTenantMembershipStore.cs, IBeam.Identity.Repositories.AzureTable/Stores/AzureTableTenantInviteStore.cs, IBeam.Management.Identity.Target.Api/Controllers/ManagementCustomerUsersController.cs, IBeam.Tests.Management.CustomerIdentity/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] User list/detail returns user ID, email, available first/last name, tenant membership status, permitted role IDs/names and timestamps; optional absent fields remain absent.
- [x] Pending invitations are distinguishable from active/disabled memberships and require invites.read; users.read alone does not disclose invitation recipients.
- [x] Membership lookup always verifies route tenant/app scope; a user belonging to multiple tenants reveals only the requested authorized membership.
- [x] Pages are bounded with authenticated scope/filter-bound cursors; per-page user/role lookups and provider requests have explicit limits and cancellation.
- [x] Passwords, hashes, phone/device/IP data, OTP/session/security stamps, invite tokens/links and arbitrary profile metadata never appear in lists, details, logs or audit.
- [x] Isolated provider fixtures cover active/disabled users, pending/expired/revoked invitations, large tenants, missing users and cross-tenant/app substitution.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 474 tests passed, zero failures/skips, including signed user/invite reads over read-only storage with unchanged invitation state. See Hubbsly IBM-0102 for delivery evidence.

### IBM-0103 / TCM-006: Expose portal tenant and user directory routes with safe forwarding

Estimate: **5 points**. Lane: Done. Delivery order: 60. Hubbsly ID: ed0d6dcf-7d2a-4dbc-a218-64dcd63ce38c.

Dependencies: IBM-0101, IBM-0102.

Implemented IManagementCustomerIdentityClient and typed IManagementPortalCustomerDirectory<T> services through the opt-in ManagementPortalCustomerDirectoriesController. GET selected-app tenants, tenant users and invitations (list/detail) with live authorization, signed fixed-path HTTPS, safe typed projection and encrypted support-user/app/query-bound pagination. See docs/management/customer-identity-api.md.

Proposed paths: IBeam.Management.Services/CustomerIdentity/, IBeam.Management.Api/Controllers/ManagementTenantsController.cs, IBeam.Management.Api/Controllers/ManagementCustomerUsersController.cs, IBeam.Tests.Management.CustomerIdentity/, docs/management/customer-identity-api.md. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] Every portal route checks the selected app, required read grant and target capability before resolving secrets or issuing a target request; direct service invocation enforces the same checks.
- [x] Read responses follow IBeam envelopes with data arrays and top-level paging metadata; an empty page with continuation remains traversable.
- [x] Portal accepts only validated allowlisted target DTO fields and applies body/page/timeout limits; malformed/oversized/provider error responses become safe failures.
- [x] Existing SSRF protections cover these target paths, redirects are rejected and no caller-supplied target URL or storage connection is accepted.
- [x] Cross-user/app/organization/realm/filter cursor replay fails; revoked assignments and changed connections invalidate subsequent requests without re-login.
- [x] Tests exercise two apps with overlapping tenant/user IDs and prove isolation; neither portal persistence nor diagnostic logging stores returned directory payloads.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 570 Management tests passed, zero failures/skips; three affected packages packed and inspected. All six signed portal read routes, isolated apps, safe projection and cursor/access failures verified. See Hubbsly IBM-0103.

## TCM-F03: Reliable tenant creation and administration

Estimate: **15 points**. Hubbsly feature: 3445dc1f-020b-4802-abbd-8f416746e3e6.

Create and rename customer tenants through scoped operations with durable receipts and retry protection. Support operators never become customer members automatically.

Feature dependencies: TCM-F01, TCM-F02.

Feature acceptance:

- [x] Retries do not duplicate tenants or overwrite concurrent changes.
- [x] Mutation receipts and safe audit intent survive process restarts and uncertain target responses.
- [x] Existing tenants remain usable and portal staff are not automatically linked to customer organizations.

### IBM-0104 / TCM-007: Persist target operation receipts, idempotency and safe mutation audit

Estimate: **5 points**. Lane: Done. Delivery order: 70. Hubbsly ID: c29951e3-c511-42c2-a03a-4b269162f3a9.

Dependencies: IBM-0100.

Add ManagementIdentityOperationEntity/IManagementIdentityOperationRepository and Azure Table adapter in IBeam.Management.Repositories.AzureTable, configured against target management metadata storage. Persist request fingerprints, deterministic reserved resource IDs, step/status, versions, safe result references, actor/credential/correlation and mutation audit intent. Add IManagementIdentityOperationService and GET /api/management-identity/operations/{operationId}, mirrored by the portal. Schema creation is explicit trusted setup.

Proposed paths: IBeam.Management/Entities/, IBeam.Management/CustomerIdentity/, IBeam.Management.Repositories.AzureTable/CustomerIdentity/, IBeam.Management.Identity.Target.Services/Operations/, IBeam.Management.Identity.Target.Api/Controllers/ManagementIdentityOperationsController.cs, IBeam.Management.Services/CustomerIdentity/, IBeam.Management.Api/Controllers/ManagementIdentityOperationsController.cs. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] Idempotency-Key is mandatory for mutating customer-management requests; scope includes support organization, target realm/app, operation and resource, with a canonical payload fingerprint.
- [x] Same key and same request returns the existing operation/result; same key with changed request returns 409; concurrent requests cannot reserve duplicate work.
- [x] Operation rows use atomic claims/ETags and stable resource IDs; a crash after a downstream write resumes or reconciles that write instead of blindly creating it again.
- [x] Safe audit intent is persisted before a mutation; inability to record it prevents a new mutation, and persisted outbox-style events can be delivered without repeating customer changes.
- [x] Receipts contain no raw request bodies, email/password/invite secrets or customer profiles; retention, idempotency expiry and safe polling limits are documented.
- [x] Operation reads require the original write authority or explicit onboarding.read as appropriate plus app scope; unrelated apps/organizations cannot enumerate or retrieve receipts.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 601 Management tests passed, zero failures/skips. Six packages packed and inspected. Real storage/signed receipt reads, concurrent claims, crash/lease recovery, safe outbox redelivery and portal authority/projection tests verified. See Hubbsly IBM-0104.

### IBM-0105 / TCM-008: Create and rename target tenants through existing Identity services

Estimate: **5 points**. Lane: Done. Delivery order: 80. Hubbsly ID: 6a9b1040-b64d-4fca-a839-f45338820449.

Dependencies: IBM-0101, IBM-0104.

Add IManagementTenantAdministrationService with POST /api/management-identity/tenants and PATCH /tenants/{tenantId}. Reuse IIdentityTenantService and optional IIdentityTenantExtension under the verified external management actor context. Add ManagementTenantAdministrationEntity metadata only for source (support-provisioned/unknown), external reference and operation linkage; existing/self-signup tenants remain unknown unless a trusted consumer reports provenance. Never set current/default tenant or Owner membership for the support operator.

Proposed paths: IBeam.Management.Identity.Target.Services/Tenants/, IBeam.Management.Identity.Target.Api/Controllers/ManagementTenantsController.cs, IBeam.Management/Entities/, IBeam.Management.Repositories.AzureTable/CustomerIdentity/, IBeam.Identity/Interfaces/IIdentityTenantService.cs, IBeam.Identity.Services/Tenants/IdentityTenantService.cs. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] Create validates bounded name/optional external reference, reserves a stable tenant ID and creates exactly one tenant through Identity services; duplicate external references follow an explicit target-scoped uniqueness policy.
- [x] Support creation returns only tenant metadata/operation reference and grants the operator no customer membership, customer role, token or default tenant.
- [x] Rename requires tenants.update and the returned version/If-Match; missing or stale versions cannot silently overwrite another change.
- [x] Existing Identity store interfaces remain compatible; any concurrency addition uses an additive optional port and unsupported adapters advertise no update capability.
- [x] Consumer extension failures are recorded as recoverable operation steps; retries reconcile the existing tenant and no untracked partial tenant or destructive automatic rollback occurs.
- [x] Legacy tenant rows are not rewritten on reads; provenance never infers self-signup from missing metadata and arbitrary consumer profile fields are rejected.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 907 tests passed, zero failures/skips; eight packages packed and inspected. Signed tenant creation/versioned rename, real provider lifecycle recovery/reference uniqueness, safe provenance and legacy column/casing preservation verified. See Hubbsly IBM-0105.

### IBM-0106 / TCM-009: Expose portal tenant create and update with operation reconciliation

Estimate: **5 points**. Lane: Done. Delivery order: 90. Hubbsly ID: 2a754d75-3fdc-4a4c-9dd1-9d0ae99f7d33.

Dependencies: IBM-0103, IBM-0104, IBM-0105.

Add POST /api/management/applications/{applicationId}/tenants and PATCH /tenants/{tenantId}, with operation polling under /operations/{operationId}. Forward caller idempotency keys and safe concurrency versions through fresh bound actor assertions. Document 201/200 completed results, 202 pending operation responses, 409 key conflict, 412 stale version and safe unavailable/unknown outcomes.

Proposed paths: IBeam.Management.Api/Controllers/ManagementTenantsController.cs, IBeam.Management.Api/Controllers/ManagementIdentityOperationsController.cs, IBeam.Management.Services/CustomerIdentity/, IBeam.Tests.Management.CustomerIdentity/, docs/management/customer-identity-api.md. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] Create and rename each require their own live human grant and target machine capability; tenants.read or support administration alone cannot mutate.
- [x] Portal preserves one idempotency identity across target retries and issues a fresh signed assertion per attempt; non-idempotent writes are never retried blindly by generic HTTP resilience policies.
- [x] Lost/timeout responses return a safe pending/unknown operation reference with a documented reconciliation path; the UI is never told a tenant definitely failed when the target may have committed.
- [x] Version/If-Match is preserved without weakening provider compare-and-swap; stale or missing versions use the documented conflict/precondition responses.
- [x] Thin controllers delegate to service operations and audit captures actor, app, tenant ID, outcome and correlation without raw request content.
- [x] HTTP tests prove two-app isolation, duplicate submissions, concurrent rename, target timeout after commit and revocation before retry.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 638 Management tests passed, zero failures/skips; five packages packed and inspected. Two-app mutations, after-commit uncertainty, strict versions, stateless scoped tracking and read-only signed lookup verified. See Hubbsly IBM-0106.

## TCM-F04: Enterprise onboarding and first administrator invitation

Estimate: **13 points**. Hubbsly feature: 5f6859b2-cb89-41d5-a996-eb5d6e4c3b0f.

Provide one recoverable operation to create an enterprise tenant and invite its first customer administrator. The customer accepts through the application's own Identity flow and signs in normally.

Feature dependencies: TCM-F03.

Feature acceptance:

- [x] One enterprise onboarding request creates one tenant and one recoverable administrator invitation.
- [x] Customer ownership and password/email verification stay in the application's established Identity acceptance flow.
- [x] Partial completion and uncertain email delivery are visible and safely recoverable.

### IBM-0107 / TCM-010: Orchestrate enterprise tenant and initial administrator invitation

Estimate: **8 points**. Lane: Done. Delivery order: 100. Hubbsly ID: 5bf8918a-cd6e-4186-b8d3-2f0b0f7fb559.

Dependencies: IBM-0105, IBM-0104, IBM-0099.

Add IManagementEnterpriseOnboardingService. Request includes tenant name, initial admin email, optional external reference and a server-approved onboarding policy key. Use the operation journal to reserve tenant/invite IDs and coordinate tenant creation, extension completion, invitation creation/delivery and owner acceptance status. Reuse ITenantInviteService through a management-aware external actor adapter; do not pass a support user GUID as a fictitious customer inviter. Durable invite identity/delivery deduplication must be added where existing APIs cannot reconcile after crashes.

Proposed paths: IBeam.Management/CustomerIdentity/, IBeam.Management.Identity.Target.Services/Onboarding/, IBeam.Identity.Services/Invites/, IBeam.Identity/Interfaces/ITenantInviteService.cs, IBeam.Management.Repositories.AzureTable/CustomerIdentity/, IBeam.Tests.Management.CustomerIdentity/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] Onboarding requires onboarding.create plus tenants.create, invites.manage and approved roles.assign authority on both human and target credentials; direct service calls cannot bypass composite checks.
- [x] One idempotent request creates one tenant and one pending initial-admin invitation; retries after each crash point reuse the same tenant/invitation instead of creating duplicates.
- [x] Admin/Owner role templates are host-allowlisted and target-local; arbitrary role names, support roles, wildcard permissions or caller-provided redirect URLs are rejected.
- [x] New and existing customer identities must satisfy the existing verified recipient/password-setup acceptance flow; management never sets/returns passwords or grants support staff customer membership.
- [x] Status distinguishes tenant creation, extension completion, invitation persisted, delivery sent/failed/unknown, awaiting acceptance, completed and recoverable failure; sending an email alone never marks onboarding complete.
- [x] Delivery failure does not delete the tenant or create another invitation automatically; explicit retry/resend reconciles invitation state and records possible at-least-once email delivery.
- [x] Existing self-signup routes remain available and unchanged; pending onboarding never grants customer access before the intended administrator has valid tenant membership.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 942 tests passed, zero failures/skips; seven packages packed and inspected. Durable tenant/invite/delivery recovery, verified acceptance and scoped ciphertext/CAS checks passed. See Hubbsly IBM-0107.

### IBM-0108 / TCM-011: Expose enterprise onboarding create, status and safe retry APIs

Estimate: **5 points**. Lane: Done. Delivery order: 110. Hubbsly ID: 57012a51-990f-4deb-898f-8a057cfb9393.

Dependencies: IBM-0106, IBM-0107.

Implemented opt-in enterprise onboarding create/retry routes at target and portal, authoritative business status on shared operation polling, safe lost-response reconciliation and current composite retry authorization. See docs/management/enterprise-onboarding-api.md.

Proposed paths: IBeam.Management.Identity.Target.Api/Controllers/ManagementEnterpriseOnboardingController.cs, IBeam.Management.Api/Controllers/ManagementEnterpriseOnboardingController.cs, IBeam.Management.Services/CustomerIdentity/, IBeam.Tests.Management.CustomerIdentity/, docs/management/customer-identity-api.md. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [x] Create validates required name/email/policy/idempotency and returns a resumable operation; all progress is scoped to the selected application/environment and customer tenant.
- [x] Read and retry enforce their explicit operations; retry also rechecks every underlying write grant before executing unfinished steps.
- [x] Terminal completed operations do not create another tenant/invitation; changed payloads require a new request and cannot modify an existing operation through retry.
- [x] Acceptance status is reconciled from authoritative target invitation/membership state; revoked/expired/wrong-recipient invitations never produce completed onboarding.
- [x] Only safe error codes, step names, timestamps and resource IDs are returned; SMTP/provider exceptions, invite links/tokens and customer payloads remain private.
- [x] Tests cover email delivery failure, lost response after commit, revoked permission while waiting, cross-app operation ID replay and successful acceptance by a new or existing customer.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

Verification evidence: 960 tests passed, zero failures/skips; five packages built/packed/inspected. Signed target/portal recovery, live revocation, isolation and actual verified new/existing customer acceptance passed. See Hubbsly IBM-0108.

## TCM-F05: Customer provisioning and invitation management

Estimate: **15 points**. Hubbsly feature: a1c149de-3381-4546-b836-bd8709a99412.

Add individual customers to an existing enterprise tenant and manage their invitations. Role choices come from target-owned templates and customer acceptance remains in the customer application.

Feature dependencies: TCM-F02, TCM-F03.

Feature acceptance:

- [ ] Operators can discover permitted roles and invite/provision customers into an existing tenant without handling passwords.
- [ ] Invitation list, detail, resend and revoke are tenant-scoped and retry-safe.
- [ ] A customer already in another tenant keeps that identity and existing memberships.

### IBM-0109 / TCM-012: Add approved role catalog and target customer provisioning adapter

Estimate: **5 points**. Lane: Triage. Delivery order: 120. Hubbsly ID: d63fbbd6-863c-4e0a-aa14-32460571b041.

Dependencies: IBM-0102, IBM-0104.

Add GET /api/management-identity/tenants/{tenantId}/assignable-roles and POST /tenants/{tenantId}/users/provision. IManagementCustomerProvisioningService coordinates ITenantUserProvisioningService/ITenantInviteService via a management-aware actor context. Initial release is invitation-based provisioning for both new and existing users; no temporary password, forced email verification, automatic takeover or silent privileged membership linking. Host-approved role catalog is limited to the selected tenant.

Proposed paths: IBeam.Management.Identity.Target.Services/Users/, IBeam.Management.Identity.Target.Api/Controllers/ManagementCustomerUsersController.cs, IBeam.Identity.Services/Tenants/TenantUserProvisioningService.cs, IBeam.Identity/Models/TenantUserProvisioningModels.cs, IBeam.Tests.Management.CustomerIdentity/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [ ] Provision requires users.provision, invites.manage and roles.assign for requested roles on both sides; catalog visibility never grants assignment permission.
- [ ] Only host-allowlisted tenant role IDs/templates are returned/accepted; support, cross-tenant and caller-invented roles or access-grant payloads are rejected.
- [ ] New users receive the existing secure invitation/password-setup path; existing users prove recipient ownership before gaining the new tenant membership.
- [ ] Existing user email/password/global state and other tenant memberships are preserved; email matching is target-local and does not leak cross-app/global user existence.
- [ ] Idempotent retries produce one effective invitation/provisioning outcome per tenant/recipient/policy and safe operation references, including crash after user or invitation persistence.
- [ ] Pending invites, active members, disabled members, duplicate recipients, disabled tenants, role-policy denials and provider failure have documented safe outcomes.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

### IBM-0110 / TCM-013: Implement paged invitation lifecycle with safe resend and revoke

Estimate: **5 points**. Lane: Triage. Delivery order: 130. Hubbsly ID: 20c2764e-b7b2-4e07-9363-22bca6f7cb96.

Dependencies: IBM-0109.

Add IManagementCustomerInvitationService and target GET /tenants/{tenantId}/invites[/{inviteId}], POST /invites/{inviteId}/resend and POST /invites/{inviteId}/revoke. Reuse Identity lifecycle behavior with bounded store reads, operation receipts, rate limits and a trusted target redirect policy. Do not add management invitation acceptance endpoints.

Proposed paths: IBeam.Management.Identity.Target.Services/Invitations/, IBeam.Management.Identity.Target.Api/Controllers/ManagementCustomerInvitationsController.cs, IBeam.Identity.Services/Invites/TenantInviteService.cs, IBeam.Identity.Repositories.AzureTable/Stores/AzureTableTenantInviteStore.cs, IBeam.Tests.Management.CustomerIdentity/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [ ] List/detail requires invites.read, resend/revoke requires invites.manage; all calls verify tenant, app, current invitation state and live credentials.
- [ ] Responses expose invitation ID, recipient email, permitted roles, status and timestamps only; raw redemption tokens/URLs and delivery credentials are never serialized or audited.
- [ ] Resend/revoke require idempotency and expected version; replay, concurrent acceptance, revocation and expiration produce deterministic safe outcomes.
- [ ] Resend rate limits are keyed by target/tenant/invitation and enforced server-side; resend cannot expand roles or replace recipient/redirect destinations.
- [ ] Existing Identity acceptance verifies the intended recipient; revoked/expired/superseded tokens are rejected according to documented token-rotation behavior.
- [ ] Pagination remains bounded and scope-bound; integration tests prove no duplicate invitations or silent success on mail failure and no cross-tenant invitation access.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

### IBM-0111 / TCM-014: Expose portal provisioning, role catalog and invitation lifecycle routes

Estimate: **5 points**. Lane: Triage. Delivery order: 140. Hubbsly ID: b221b8cb-8baf-4717-a06f-22b61c98a29b.

Dependencies: IBM-0103, IBM-0106, IBM-0109, IBM-0110.

Mirror assignable-roles, users/provision and invitation list/detail/resend/revoke under /api/management/applications/{applicationId}/tenants/{tenantId}. Add typed safe DTO forwarding, operation receipt reconciliation, target-host-owned policy metadata and frontend examples for selecting a tenant/role, inviting a user and tracking status.

Proposed paths: IBeam.Management.Api/Controllers/ManagementCustomerUsersController.cs, IBeam.Management.Api/Controllers/ManagementCustomerInvitationsController.cs, IBeam.Management.Services/CustomerIdentity/, IBeam.Tests.Management.CustomerIdentity/, docs/management/customer-identity-api.md. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [ ] Portal exposes the documented role/provision/invite routes through thin controllers with the same independent operation checks as target services.
- [ ] Unauthorized role/recipient/tenant substitution is rejected before mutation, and live assignment/target policy is rechecked on every attempt.
- [ ] Mutation forwarding preserves idempotency and version checks; uncertain target writes remain reconcilable through operation polling.
- [ ] Known existing customer identities are reused only by target Identity logic; portal does not maintain a customer account table or a global email lookup.
- [ ] Safe envelopes distinguish pending, sent, failed/unknown delivery, accepted, expired and revoked states without exposing credentials or inviting operator impersonation.
- [ ] HTTP tests cover multiple applications, support users with read-only/provision-only grants, accepted/expired/revoked invites and permission loss between role discovery and submit.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

## TCM-F06: Tenant and membership access lifecycle

Estimate: **10 points**. Hubbsly feature: beb41f69-3580-4fa2-86f5-17b40a5b19a4.

Allow authorized operators to disable/reactivate a tenant or manage one customer's membership and permitted roles. Changes are limited to the selected tenant and prevent last-administrator lockout.

Feature dependencies: TCM-F03, TCM-F05.

Feature acceptance:

- [ ] Tenant status and customer membership/role operations are independently authorized and audited.
- [ ] Membership changes do not globally disable the customer or alter other tenants.
- [ ] Concurrent operations cannot remove the last eligible administrator or bypass access revocation.

### IBM-0112 / TCM-015: Manage tenant membership status and approved roles without global account changes

Estimate: **5 points**. Lane: Triage. Delivery order: 150. Hubbsly ID: ef522ad3-49a9-43cd-8cc6-680c64bfa566.

Dependencies: IBM-0109, IBM-0111, IBM-0104.

Add PATCH /tenants/{tenantId}/users/{userId}/membership for isEnabled and PUT /tenants/{tenantId}/users/{userId}/roles for approved role IDs, target and portal mirrors. IManagementCustomerMembershipService calls existing role/membership services through additive versioned ports where needed. Preserve global account state and default-tenant semantics; no password reset, MFA reset, delete or impersonation endpoints.

Proposed paths: IBeam.Management.Identity.Target.Services/Users/, IBeam.Management.Identity.Target.Api/Controllers/ManagementCustomerUsersController.cs, IBeam.Management.Api/Controllers/ManagementCustomerUsersController.cs, IBeam.Identity/Interfaces/, IBeam.Identity.Services/Tenants/, IBeam.Identity.Repositories.AzureTable/Stores/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [ ] Membership enable/disable requires memberships.manage; role changes require roles.assign and the approved target-local role catalog.
- [ ] Both operations require expected versions and idempotency, are serialized/guarded per tenant where needed, and cannot disable/demote the final eligible administrator under concurrent requests.
- [ ] Disabling a membership preserves the customer's global account and other memberships; re-enable never restores privileges that were separately removed.
- [ ] Effective access is rejected at customer tenant authorization/refresh boundaries with documented bounded revocation semantics; a test covers an already-issued customer token.
- [ ] Rejected or partially completed changes leave a reconcilable operation/audit result and never silently report success; reasons use bounded safe codes without arbitrary sensitive free text.
- [ ] Cross-app/tenant IDs, privilege escalation, stale versions, multi-tenant accounts and concurrent last-administrator changes are covered by service and provider tests.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

### IBM-0113 / TCM-016: Expose scoped tenant disable/reactivate and enforce customer access status

Estimate: **5 points**. Lane: Triage. Delivery order: 160. Hubbsly ID: 973792bb-c084-4d3a-8a13-a53980f45d1b.

Dependencies: IBM-0106, IBM-0112.

Add POST /tenants/{tenantId}/deactivate and /activate at target and portal. IManagementTenantAdministrationService reuses Identity tenant status services with safe versioned concurrency. Deactivation blocks customer tenant use while preserving data; management can still read/recover the disabled tenant when explicitly authorized. Publish the customer authorization integration needed to honor tenant status.

Proposed paths: IBeam.Management.Identity.Target.Services/Tenants/, IBeam.Management.Identity.Target.Api/Controllers/ManagementTenantsController.cs, IBeam.Management.Api/Controllers/ManagementTenantsController.cs, IBeam.Identity.Services/, IBeam.Identity.Api/Authorization/, IBeam.Tests.Management.CustomerIdentity/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [ ] Status changes require tenants.status, expected version and Idempotency-Key on both portal and target; tenant read/create alone cannot change status.
- [ ] Deactivation preserves Identity/business data, user global state and other tenants; no delete or automatic licensing/billing cancellation occurs.
- [ ] Target customer tenant authorization and refresh reject disabled-tenant use within the documented revocation bound, including an already-issued token; custom authorization hosts have an explicit required integration contract.
- [ ] Invitation/provisioning and role-changing writes fail for disabled tenants except specifically documented recovery operations; support read/activate remains independently permitted.
- [ ] Reactivation preserves previous membership/role disables and does not reactivate unrelated accounts or failed onboarding steps.
- [ ] Integration tests cover concurrent activate/deactivate, stale versions, lost responses, two tenants sharing a user, valid management recovery and safe correlated audit.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

## TCM-F07: Consumer policies and self-service signup compatibility

Estimate: **8 points**. Hubbsly feature: d438c6c4-ab70-4c31-be89-144ce26bb5d2.

Let consuming apps choose onboarding rules while sharing the existing Identity lifecycle. Preserve framework reuse and keep application profiles, billing and SSO out of the management core.

Feature dependencies: TCM-F03, TCM-F04, TCM-F05.

Feature acceptance:

- [ ] Target-owned onboarding policies determine permitted roles, redirects and optional application initialization.
- [ ] Self-signup and support-provisioned tenants share Identity services without duplicate account stores or changed public registration policy.
- [ ] Unsupported optional adapters fail clearly and advertise only their implemented capabilities.

### IBM-0114 / TCM-017: Add target-owned onboarding policy and idempotent consumer extension hooks

Estimate: **5 points**. Lane: Triage. Delivery order: 170. Hubbsly ID: 25134d07-2f7c-48fd-84c2-838e76c049a4.

Dependencies: IBM-0107, IBM-0109.

Add IManagementCustomerOnboardingPolicy, trusted policy-key discovery and optional idempotent initialization hooks layered over IIdentityTenantExtension. Host policy controls whether enterprise onboarding is enabled, initial-admin/member role templates, approved redirects and required app initialization. Hooks receive operation/tenant IDs and safe typed context, not portal-supplied arbitrary customer profile JSON. Existing Licensing/Billing/Credits can be integrated by consumers through these hooks without becoming required dependencies.

Proposed paths: IBeam.Management/CustomerIdentity/, IBeam.Management.Identity.Target.Services/Policies/, IBeam.Management.Identity.Target.Services/Onboarding/, IBeam.Identity/Interfaces/IIdentityTenantExtension.cs, docs/management/enterprise-onboarding.md, IBeam.Tests.Management.CustomerIdentity/. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [ ] Only host-registered enabled policy keys are discoverable/accepted; caller input cannot select arbitrary implementations, storage accounts, redirect hosts or service URLs.
- [ ] Default Identity-only onboarding works without Billing, Licensing, Credits, SSO, application-profile repositories or a particular consumer app.
- [ ] Optional initialization executes with a durable operation key and idempotent/reconcilable semantics; restart/retry cannot duplicate consumer resources.
- [ ] A required policy/hook denial or failure blocks the affected workflow step with a safe recoverable status and creates no silent partial success.
- [ ] Role templates and redirect destinations are validated by target-owned policy; the management user cannot supply permissions or customer data through an unvalidated extension bag.
- [ ] Tests use two consumer policies and failure injection to prove optional composition and preserve separate support/customer identity containers.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

### IBM-0115 / TCM-018: Verify self-signup coexistence and document trustworthy tenant provenance

Estimate: **3 points**. Lane: Triage. Delivery order: 180. Hubbsly ID: 1610c64c-908f-47da-b949-3effb1c8b2df.

Dependencies: IBM-0105, IBM-0114.

Define tenant provenance values support-provisioned, self-service and unknown. Reuse ManagementTenantAdministrationEntity as optional target-local metadata, with a trusted event/hook for consumers to record self-service creation. Do not rewrite old tenants or infer their signup channel. Document and test that ordinary registration/provisioning continues using existing Identity services while enterprise management uses its separate authorization entry point.

Proposed paths: IBeam.Management.Identity.Target.Services/Tenants/, IBeam.Management.Repositories.AzureTable/CustomerIdentity/, IBeam.Tests.Management.CustomerIdentity/, IBeam.Tests.Identity.Services/, docs/management/enterprise-onboarding.md. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [ ] Support-created tenants record their known provenance and operation ID; existing tenants remain unknown unless a trusted migration/event supplies evidence.
- [ ] Self-service provenance can be recorded only by a trusted target service integration, never by a public signup body or support query parameter.
- [ ] Existing signup, invitation acceptance, MFA, tenant selection and default-membership behaviors pass focused regressions without adding management credentials or forcing a new signup mode.
- [ ] The same customer can retain a self-service tenant and accept an enterprise invitation without duplicate global Identity accounts or accidental role transfer.
- [ ] Directory provenance filters, when supported, use bounded target queries and expose unknown explicitly; missing metadata is not described as self-signup.
- [ ] Docs distinguish management application, support organization and customer tenant, and explain storage/realm choices and optional provenance setup.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

## TCM-F08: End-to-end verification and baseline application handoff

Estimate: **13 points**. Hubbsly feature: f0c0d853-3d88-4597-b4c8-0bf0d0b02d59.

Prove the enterprise onboarding lifecycle using packaged consumers and isolated synthetic data, then publish an exact API handoff for the baseline portal application.

Feature dependencies: TCM-F01, TCM-F02, TCM-F03, TCM-F04, TCM-F05, TCM-F06, TCM-F07.

Feature acceptance:

- [ ] A package-only portal and two isolated customer targets complete enterprise onboarding and customer lifecycle flows.
- [ ] Security, partial failure, audit and existing diagnostics regressions pass.
- [ ] A repeatable local demo and frontend handoff document actual routes, auth, schemas, permissions and failure handling.

### IBM-0116 / TCM-019: Verify enterprise lifecycle, authorization matrix and payload-free audit end to end

Estimate: **8 points**. Lane: Triage. Delivery order: 190. Hubbsly ID: cb1b22fa-39e0-4fee-ad55-8e25b0b1312d.

Dependencies: IBM-0108, IBM-0111, IBM-0112, IBM-0113, IBM-0115.

Create focused service/HTTP/provider suites and a package-only smoke sample with one support API and two isolated customer Identity targets. Test enterprise create -> admin invitation -> customer acceptance -> directory -> additional member -> role/status changes. Exercise permission/credential revocation, signed delegation, retries/crashes, provider failures, multi-app storage isolation and metadata-only audit. Validate Azure Table as the initial supported adapter; other providers must not advertise unimplemented query/concurrency capabilities.

Proposed paths: IBeam.Tests.Management.CustomerIdentity/, IBeam.Tests.Management.Applications/, IBeam.Tests.Management.Identity/, IBeam.Tests.Identity.Services/, IBeam.Tests.Identity.Api/, samples/Management.CustomerIdentity.PackageSmoke/, docs/management/customer-identity-verification.md. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [ ] Package-only consumers restore built NuGets without source ProjectReferences and run the full enterprise flow against two isolated targets with overlapping customer IDs.
- [ ] Matrix covers no auth/wrong realm, insufficient human grants, insufficient machine capabilities, invalid assertions, cross-app/tenant IDs/cursors, disabled entities and live revocation.
- [ ] Crash injection after each durable step, duplicate/concurrent requests, unknown email delivery and lost HTTP responses produce one tenant/invitation and reconcilable outcomes.
- [ ] Customer acceptance proves recipient ownership, grants only allowed tenant roles and preserves existing other-tenant memberships; last-admin and tenant-disable rules hold for active sessions.
- [ ] Success, denial, conflict, retry and failure audit events correlate portal actor and target credential/resource IDs without email/profile/payload/token/secret canaries, including provider exception paths.
- [ ] Existing errors/logs, sanitization, support identity/assignment and customer signup/auth regressions pass; read-only permissions issue no customer writes.
- [ ] Build/pack checks, exact tested provider support and known compatibility/migration requirements are recorded; no NuGet publish, production deployment or release tag is performed by this card.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

### IBM-0117 / TCM-020: Deliver repeatable enterprise demo seeds and baseline app API handoff

Estimate: **5 points**. Lane: Triage. Delivery order: 200. Hubbsly ID: bffc8020-903f-40c5-83cd-f6f323b297d5.

Dependencies: IBM-0116.

Extend the existing portable NuGet-consuming host/sample and add a separate opt-in local target Identity host/fixtures as necessary. Keep the temporary IBeam.ADMIN.API repository as host placement only; no rename/new repository is required. Provide trusted idempotent setup/seeds for synthetic apps, existing/self-signup/enterprise tenants, active/pending/disabled customers and support access profiles. Update baseline app prompt and exact API contracts for tenant/user UI and enterprise onboarding. Put reusable package-only sample/docs in framework first; temporary host edits occur when implementing this card.

Proposed paths: samples/Management.CustomerIdentity.PackageSmoke/, docs/management/customer-identity-api.md, docs/management/enterprise-onboarding.md, IBeam.ADMIN.API/.scripts/, IBeam.ADMIN.API/docs/baseline-app-prompt.md, IBeam.ADMIN.API/docs/agent-management-handoff.md. Paths beginning IBeam.ADMIN.API refer to the separate temporary consumer host, not a new framework directory.

- [ ] Fresh documented local startup builds the package-only support/target hosts, seeds synthetic identities/tenants and allows a real support login/MFA enterprise workflow without customer production access.
- [ ] Seeding is trusted CLI-only, repeatable and preserves deliberately changed/revoked access by default; reset/restore behavior is explicit and isolated to the demo instance.
- [ ] Demo uses isolated local storage/target fixtures, generated private credentials and real authorization checks; no anonymous setup endpoints, hard-coded OTP bypass or production network/security exceptions are introduced.
- [ ] Handoff inventories exact routes/DTOs/envelopes, paging, permissions, idempotency keys, versions, pending/unknown operations, invite states, role catalog and safe customer acceptance links.
- [ ] Baseline frontend needs only API URL and normal support login; machine keys, signing material, target storage settings and customer passwords never reach browser code.
- [ ] Repeatable smoke checks demonstrate tenant/user visibility, enterprise onboarding and multi-app denial; frontend implementation and production deployment remain outside this card.

Verification: Focused service/HTTP tests and isolated provider fixtures demonstrate the checklist; build/pack changed packages, preserve existing customer and diagnostics behavior, and document public contracts. One card must leave the repository buildable.

## Deferred scope

- Customer business records or arbitrary profile payloads
- Impersonation, password/MFA reset, global account deletion/disable or a cross-app God account
- Enterprise SSO/domain verification/SCIM, bulk CSV onboarding and billing/licensing product workflows
- Frontend implementation, repository rename, NuGet publishing and production deployment
