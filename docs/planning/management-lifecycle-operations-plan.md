# Management API: Ownership, Tenant Lifecycle and App Operations

Created 2026-10-27. Baseline: IBeam 2.16.0. The preceding Management API epics remain shipped.

Hubbsly epic: `6b9422fc-0205-4c3b-88cb-63ff7f114cd1`. Stage: `landing`. 7 features, 29 cards, **165 points**.

Completed: **29/29 cards, 165/165 points**.

Feature points are sums of their cards. Implement in the order below; each card depends on its stated predecessors and must merge into development before the next starts. Future scope stays in triage until selected.

**Priority update:** Work IBM-0146 through IBM-0149 before resuming IBM-0123. This hotfix uses the supplied diagnosis, an origin-default-branch IBeam patch baseline, development PRs and explicit user approval before pushes, merges, publication or production deployment. Its approval gates override the ordinary merge-on-completion workflow: verified PRs and a release/deploy handoff may be complete while awaiting approval to merge/publish/deploy. Production order: IBeam package, Qurvia.API, Qurvia.APPS. MPTPortal repositories remain outside scope.

| Order | Feature | Points | Dependencies |
| --- | --- | ---: | --- |
| 1 | MLO-F07: Fix Qurvia invite links and release a compatible IBeam patch | 13 | None |
| 2 | MLO-F01: Ownership and role safety | 34 | None |
| 3 | MLO-F02: Explicit operations and supported app extensions | 23 | MLO-F01 |
| 4 | MLO-F03: App-owned tenant export | 23 | MLO-F02 |
| 5 | MLO-F04: Recoverable tenant purge | 31 | MLO-F03 |
| 6 | MLO-F05: Committed membership events and consumer synchronization | 18 | MLO-F04 |
| 7 | MLO-F06: Shared target transport, telemetry and bulk invitation revocation | 23 | MLO-F05 |

## Shared requirements

Portal API -> Management services -> typed signed target client -> target API -> native/application services -> entity-focused stores. Custom operations use IServiceOperationExecutor. Business data handlers run only in the consumer target; commercial and Identity bridges remain optional.

Effective authority is the intersection of current support assignment, explicit operation grant, target credential, installed capability and host-owned policy. No support impersonation, arbitrary target proxy, customer business-data response in the support portal, global account deletion under tenant purge, or automatic privilege migration. Keep exact versions, replay guards, durable same-intent recovery and metadata-only audit.

Each card must build/pack its changed production packages, pass focused behavior and meaningful negative/provider tests, update its public contracts/docs, merge into development and record commit/test evidence before Done. Later integration cards validate fresh NuGet-only consumers. Never mark unimplemented criteria complete. User update 2026-10-27: run new/focused tests per card; defer the full legacy regression to feature completion. Required repository PR checks still run before each merge. IBM-0129 completes the current operation-extension feature, so its feature-completion validation includes the full legacy matrix.

The live Hubbsly MCP exposes no dependency-edge fields or tools. Record ordered predecessor references in card descriptions and feature outcomes, and retain an explicit acyclic dependency graph in this JSON. Resolve references to real IBM source keys after publication.

The data-export/purge operation family is separate from Identity (`management.tenants.export`, `management.tenants.delete`). Ownership transfer belongs to Identity (`management.identity.owners.transfer`). These are proposed future capabilities until their implementation cards complete; no new stable package publication or production data operation is part of this backlog.

## MLO-F07: Fix Qurvia invite links and release a compatible IBeam patch (13 points)

Completed: 13 points across four cards (5+3+2+3). All three management invitation token paths use shared unpadded base64url; Qurvia API accepts legacy encoded-plus previews; APP sends token-based OTP after failed preview. Development and isolated Test PRs passed and merged. Stable 2.16.1 is published; all 56 NuGet packages and fresh public consumer restore/build verified. Test API/APP deployment and read-only smoke checks passed. Abram confirmed deployment completion on 2026-10-27. Old encoded-slash invitations require a user resend. Optional ReleaseBot repair is separate from the successful approved manual stable release.

Feature dependencies: None.

Feature acceptance criteria:

- All three management invite token paths are base64url without padding; raw-token SHA-256 storage semantics are unchanged.
- Qurvia publish output retains ASP.NET Core hosting configuration and accepts encoded plus paths through local IIS; encoded slashes require resend.
- The APP fallback can send token-based invite OTP when preview fails, while normal sign-in retains email validation.
- Approved-push PRs target development with green required CI; a short PowerShell release/deploy handoff waits for explicit merge/publication/deployment approval.

Delivery rules: Use the supplied production diagnosis; do not repeat the investigation. Windows and PowerShell only. IBeam fix starts from origin's default branch, not unreleased development features; feature branches and PRs target development in each repo. Ask Abram before any push, merge, package publication or production deployment. Prepare concrete local changes and validation before asking. Do not touch MPTPortal repositories. No production writes except explicitly approved deployment. Never print app settings or keys. PowerShell 5.1: parenthesize ConvertFrom-Json output before filtering arrays, or use az --query. Production order is IBeam patch package, Qurvia.API, then Qurvia.APPS. New tokens use base64url without padding; SHA-256 still hashes the raw token string. Existing '+' links need IIS allowDoubleEscaping; existing '%2F' links require resend. Do not weaken IIS to accept encoded slashes.

### IBM-0146 / INV-001: Generate URL-safe management invite tokens and prepare the IBeam patch

**5 points.** Lane: `done`. Order: -40. Dependencies: None.

In IBeam, replace plain Convert.ToBase64String invitation tokens in ManagementCustomerInvitationService resend, ManagementCustomerProvisioningService and ManagementEnterpriseOnboardingService. Prefer one natural shared helper matching TenantInviteService.CreateInviteToken: replace '+' with '-', '/' with '_', TrimEnd('='). Read CLAUDE.md, README and release workflows; prepare the next compatible patch on the 2.16 release line (2.16.1 if unused), isolated from unreleased ownership/lifecycle features. Honor approval gates before pushes, merges and publication.

Paths/surfaces: `IBeam.Management.Identity.Target.Services/Invitations/ManagementCustomerInvitationService.cs`; `IBeam.Management.Identity.Target.Services/Provisioning/ManagementCustomerProvisioningService.cs`; `IBeam.Management.Identity.Target.Services/Onboarding/ManagementEnterpriseOnboardingService.cs`; `IBeam.Tests.Management.CustomerIdentity/`; `README.md`; `.github/workflows/`.

Acceptance criteria:

- [x] Tokens returned by resend, direct customer provisioning and enterprise onboarding all match ^[A-Za-z0-9_-]+$ and contain no '+', '/' or '='; tests exercise all three actual paths.
- [x] One shared helper is used where natural; entropy and SHA-256 hashing of the raw token string are preserved. Existing stored tokens are neither migrated nor rewritten.
- [x] Work begins from origin's verified default branch; the patch excludes unreleased development features and follows the repository's documented release process.
- [x] Changed packages build/pack and focused negative/retry and existing invitation tests pass; exact candidate version and test evidence are recorded.
- [x] A feature branch and reviewable PR content targeting development are prepared. Obtain user approval before pushing/opening the PR, merging or publishing; after an approved push, the PR is open and required CI is green.

Merged implementation: `a24646d6c10a88d10ec8e30ec0afa93254dfd3e3`.

2026-10-27 release/deployment reconciliation: Abram confirmed "Everything is deployed." Stable IBeam 2.16.1 release PR #31 merged with explicitly approved admin merge after build-test-pack passed; tag/main a24646d6c10a88d10ec8e30ec0afa93254dfd3e3. GitHub Release https://github.com/MedPACTech/IBeam/releases/tag/v2.16.1 . Production NuGet workflow 40552398410 succeeded; all 56 packages are now verified on public NuGet, and a new public-only consumer restored and built Target.Api and Target.Services 2.16.1 with zero warnings/errors. Approved development PRs #29/#21/#14 and isolated Test PRs #30/#22/#15 are merged. Test API deployment 40551032435 and Test APP deployment 40551768867 succeeded. Synthetic read-only Test checks passed: plain and encoded-plus invite preview reach the controller with the expected 400; APP OTP shell returns 200. No email/invitation was created by these checks. Production deployment completion is user-reported; the agent did not perform additional production writes. Existing encoded-slash invitations still need a user resend. The optional ReleaseBot private-workflow reuse defect was not repaired: the user chose the successful approved manual stable release path instead. The two added pipeline prerequisites are superseded by that decision and public-package availability, not claimed as implemented. All original hotfix criteria remain met; card estimates stay 5+3+2+3=13 points.

Dependency keys: None.

### IBM-0147 / INV-002: Accept legacy plus-sign invites in Qurvia API and consume the IBeam patch

**3 points.** Lane: `done`. Order: -30. Dependencies: IBM-0146.

In Qurvia.API, bump $(IBeamVersion) to the approved new IBeam patch. Add or adjust web.config system.webServer/security/requestFiltering allowDoubleEscaping=true while retaining the ASP.NET Core Module handler configuration. Validate the actual publish output and local Windows/IIS encoded-plus routing. Existing encoded slash links require resend. Use local candidate packages for preparation while publication awaits approval.

Paths/surfaces: `Qurvia.API/IBeamVersion property`; `Qurvia.API/web.config`; `Qurvia.API/project publish configuration`; `Qurvia.API/invite-preview integration tests`.

Acceptance criteria:

- [x] Qurvia.API references the chosen IBeam patch consistently; a local candidate feed may be used before approved public publication, without committing secrets or machine-specific feed paths.
- [x] Published web.config includes allowDoubleEscaping=true and retains the existing ASP.NET Core Module handler/hosting configuration.
- [x] A local Windows/IIS test of GET /api/invites/{a token containing encoded %2B}/preview reaches the controller rather than IIS's empty 404; controller-level 'Invite not found' is acceptable for a synthetic token. Kestrel-only routing is not used as proof of IIS filtering behavior.
- [x] Existing encoded slash tokens are documented as requiring resend; unrelated routes and normal invite preview still work.
- [x] Feature branch/PR targets development; ask before push or merge. After approved push, the PR is open with required CI green. No production API deployment occurs without separate user approval and prior IBeam package publication.

Merged implementation: `c5a9440fafd71e6942b55b48e8cc354e263b12d3`.

2026-10-27 release/deployment reconciliation: Abram confirmed "Everything is deployed." Stable IBeam 2.16.1 release PR #31 merged with explicitly approved admin merge after build-test-pack passed; tag/main a24646d6c10a88d10ec8e30ec0afa93254dfd3e3. GitHub Release https://github.com/MedPACTech/IBeam/releases/tag/v2.16.1 . Production NuGet workflow 40552398410 succeeded; all 56 packages are now verified on public NuGet, and a new public-only consumer restored and built Target.Api and Target.Services 2.16.1 with zero warnings/errors. Approved development PRs #29/#21/#14 and isolated Test PRs #30/#22/#15 are merged. Test API deployment 40551032435 and Test APP deployment 40551768867 succeeded. Synthetic read-only Test checks passed: plain and encoded-plus invite preview reach the controller with the expected 400; APP OTP shell returns 200. No email/invitation was created by these checks. Production deployment completion is user-reported; the agent did not perform additional production writes. Existing encoded-slash invitations still need a user resend. The optional ReleaseBot private-workflow reuse defect was not repaired: the user chose the successful approved manual stable release path instead. The two added pipeline prerequisites are superseded by that decision and public-package availability, not claimed as implemented. All original hotfix criteria remain met; card estimates stay 5+3+2+3=13 points.

Dependency keys: INV-001.

### IBM-0148 / INV-003: Allow invite OTP when Qurvia APP invite preview fails

**2 points.** Lane: `done`. Order: -20. Dependencies: IBM-0146.

Implement the user's optional small resilience fix in Qurvia.APPS apps/qurvia/src/routes/(auth)/otp/+page.svelte. When an invite token is present but preview fails, allow Send code to call startTenantInviteOtp with that token in the POST body without requiring an unavailable read-only email. Keep ordinary sign-in email validation and successful invite-preview behavior.

Paths/surfaces: `Qurvia.APPS/apps/qurvia/src/routes/(auth)/otp/+page.svelte`; `Qurvia.APPS/invite OTP tests`.

Acceptance criteria:

- [x] An invite token with failed preview can send an OTP using the token body without an empty read-only email triggering 'Enter your email'.
- [x] Ordinary sign-in still requires a valid email; successful invite previews and server-side token validation remain intact.
- [x] Focused UI/action coverage proves preview-failure, preview-success and non-invite sign-in behavior; APP checks/build pass.
- [x] Feature branch/PR targets development; ask before push or merge. After approved push, the PR is open with required CI green. APP deployment requires approval and follows the approved package/API deployments.

Merged implementation: `59597c92e45b5306fa2e3d66a9dfaf44851d9d23`.

2026-10-27 release/deployment reconciliation: Abram confirmed "Everything is deployed." Stable IBeam 2.16.1 release PR #31 merged with explicitly approved admin merge after build-test-pack passed; tag/main a24646d6c10a88d10ec8e30ec0afa93254dfd3e3. GitHub Release https://github.com/MedPACTech/IBeam/releases/tag/v2.16.1 . Production NuGet workflow 40552398410 succeeded; all 56 packages are now verified on public NuGet, and a new public-only consumer restored and built Target.Api and Target.Services 2.16.1 with zero warnings/errors. Approved development PRs #29/#21/#14 and isolated Test PRs #30/#22/#15 are merged. Test API deployment 40551032435 and Test APP deployment 40551768867 succeeded. Synthetic read-only Test checks passed: plain and encoded-plus invite preview reach the controller with the expected 400; APP OTP shell returns 200. No email/invitation was created by these checks. Production deployment completion is user-reported; the agent did not perform additional production writes. Existing encoded-slash invitations still need a user resend. The optional ReleaseBot private-workflow reuse defect was not repaired: the user chose the successful approved manual stable release path instead. The two added pipeline prerequisites are superseded by that decision and public-package availability, not claimed as implemented. All original hotfix criteria remain met; card estimates stay 5+3+2+3=13 points.

Dependency keys: INV-001.

### IBM-0149 / INV-004: Verify invite hotfix PRs and prepare the approved release/deploy handoff

**3 points.** Lane: `done`. Order: -10. Dependencies: IBM-0146, IBM-0147, IBM-0148.

Assemble the tested cross-repository hotfix and a short PowerShell release/deployment runbook. Ask for approval to push each prepared branch/PR, then verify required CI is green. Keep merges, package publication and production deployments gated on explicit user approval. Supply exact patch, PR links, validation and package -> API -> APP ordering. No production invite resend or customer mutation by the agent.

Paths/surfaces: `IBeam release/deploy handoff`; `Qurvia.API PR and publish artifact`; `Qurvia.APPS PR and build artifact`; `Hubbsly validation evidence`.

Acceptance criteria:

- [x] IBeam and Qurvia.API PRs are open against development with required CI green following approval to push; Qurvia.APPS has the same evidence when the optional fallback is included.
- [x] Short PowerShell release/deploy instructions identify the actual patch, package artifacts, commit/PR links and checks, with package -> API -> APP deployment ordering.
- [x] No push, merge, package publication or production deployment is performed without the corresponding user go-ahead; no MPTPortal repositories or production data are changed.
- [x] The handoff explicitly calls out existing '%2F' invites requiring resend, and asks the user to resend the Test Account invite for armstrongmd08@gmail.com from the Portal Account page after approved deployments, then verify the link opens without the load error.
- [x] Hubbsly points, acceptance checks, dependencies and evidence reflect actual completion; this card ends at verified PRs and a release/deploy handoff awaiting approval, not an unapproved production change.
- [x] The approved stable release path succeeds, all 56 public packages are restorable, and consumer deployment completion is recorded accurately; optional ReleaseBot repair remains explicitly outside this hotfix.

Merged implementation: `c5a9440fafd71e6942b55b48e8cc354e263b12d3`.

2026-10-27 release/deployment reconciliation: Abram confirmed "Everything is deployed." Stable IBeam 2.16.1 release PR #31 merged with explicitly approved admin merge after build-test-pack passed; tag/main a24646d6c10a88d10ec8e30ec0afa93254dfd3e3. GitHub Release https://github.com/MedPACTech/IBeam/releases/tag/v2.16.1 . Production NuGet workflow 40552398410 succeeded; all 56 packages are now verified on public NuGet, and a new public-only consumer restored and built Target.Api and Target.Services 2.16.1 with zero warnings/errors. Approved development PRs #29/#21/#14 and isolated Test PRs #30/#22/#15 are merged. Test API deployment 40551032435 and Test APP deployment 40551768867 succeeded. Synthetic read-only Test checks passed: plain and encoded-plus invite preview reach the controller with the expected 400; APP OTP shell returns 200. No email/invitation was created by these checks. Production deployment completion is user-reported; the agent did not perform additional production writes. Existing encoded-slash invitations still need a user resend. The optional ReleaseBot private-workflow reuse defect was not repaired: the user chose the successful approved manual stable release path instead. The two added pipeline prerequisites are superseded by that decision and public-package availability, not claimed as implemented. All original hotfix criteria remain met; card estimates stay 5+3+2+3=13 points.

Dependency keys: INV-001, INV-002, INV-003.
## MLO-F01: Ownership and role safety (34 points)

Completed: 34 points across IBM-0121–0125 (5+8+8+8+5). Backend role replacement policy protects unassignable roles; owner and administrator invariants are independent. Optional authoritative owner lookup and atomic exact-version transfer use durable same-intent receipts and shared fencing. Independent signed target/portal ownership operations, safe audits and read-only polls are implemented. Fresh package-only regressions, actual JWT/refresh compatibility and three-app real-MFA synthetic demo passed; migration and frontend handoff documented. Merged into development; no stable ownership release or production deployment is claimed.

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

**8 points.** Lane: `done`. Order: 20. Dependencies: IBM-0121.

Add explicit target/native ownership policy and optional conditional ownership store contracts. Extend the tenant-partition guard to track owner protection separately from administrator protection. All supported role, membership disable/revoke and destructive native writers participate; do not hardcode Owner names or use the legacy tenant owner column. Define global account-disable and unsupported-writer behavior explicitly and fail closed when owner safety cannot be established.

Paths/surfaces: `IBeam.Identity/Interfaces/`; `IBeam.Identity.Services/Tenants/`; `IBeam.Identity.Repositories.AzureTable/Stores/`; `IBeam.Management.Identity.Target.Services/Memberships/`; `IBeam.Tests.Management.CustomerIdentity/`.

Acceptance criteria:

- [x] Host-owned policy resolves active immutable owner role IDs within the tenant; invalid, ambiguous or missing policy cannot advertise owner-safe capabilities.
- [x] A remaining Admin without an Owner does not satisfy owner protection; role removal, membership disable and revocation cannot remove the last eligible owner.
- [x] Two same-snapshot writes against different owners have at most one winner using a shared persistence revision; no process-local lock is treated as the authority.
- [x] Native role services, invitations/bootstrap and supported membership writers respect the guard; legacy direct destructive paths and global-disable paths have a documented fail-closed boundary.
- [x] Providers lacking the new conditional protocol retain existing unrelated behavior and do not advertise ownership transfer; default configurations are not silently granted ownership authority.
- [x] Race, retry, unavailable-provider and legacy-role tests preserve other tenants, global identity records and existing last-administrator protection.

Merged implementation: `b423c6450c08215b5e043c89afb60c24cbde0fa2`.

82 focused provider/signed-HTTP/discovery checks and 1,119 fresh NuGet-only regressions passed, zero failures/skips. Built/packed 31 production dependencies at private validation version 2.17.0-dev.mlo.122.3; inspected 16 management package roots; package-only portal/two-target smoke passed. Coverage includes independent owner/admin sets, same-snapshot owner removal races, native role revocation, bootstrap/invitation and legacy-writer fences, immutable role identity, sticky policy, provider outages, exact retry/projection repair, default-off and invalid/incompatible opt-in, and discovery metadata round trips. No stable package release or running app upgrade was performed.

Dependency keys: MLO-001.

### IBM-0123 / MLO-003: Implement authoritative owner lookup and atomic native ownership transfer

**8 points.** Lane: `done`. Order: 30. Dependencies: IBM-0122.

Add an optional native ownership service and provider operation that changes the source and destination memberships plus guard in one tenant-partition transaction. Expose current owner IDs and versions from the same authoritative membership model; do not create another unsynchronized OwnerUserId field.

Paths/surfaces: `IBeam.Identity/Interfaces/`; `IBeam.Identity.Services/Tenants/`; `IBeam.Identity.Repositories.AzureTable/Stores/`; `IBeam.Tests.Management.CustomerIdentity/`.

Acceptance criteria:

- [x] Transfer requires an active eligible destination membership in the same tenant and exact source, destination and ownership snapshot versions.
- [x] A stable operation ID makes retries and lost replies reconcile to one transfer; source removal and destination grant never expose an ownerless intermediate state.
- [x] Unrelated roles, other memberships, global user status, default tenant and commercial entitlements remain unchanged; destination is never implicitly reactivated.
- [x] Single-owner versus multiple-owner semantics are explicit host policy; transfer cannot become an unrestricted role grant.
- [x] Lookup is bounded and read-only, resolves current role membership and cannot report a stale legacy tenant-column owner.
- [x] Provider races, rollback of unapplied transactions, projection repair and unsupported-adapter behavior are tested.

Merged implementation: `d6384b4c9a541571959a6d43d806ad0e0b3ece57`.

IBM-0123: optional native authoritative owner lookup and atomic transfer. 100 focused membership/ownership/provider checks and 1,142 fresh NuGet-only regressions passed, zero failures/skips. Built/packed 31 dependencies at private validation version 2.17.0-dev.mlo.123.1, inspected 16 management roots, and passed package-only portal/two-target smoke. Tests exercise active same-tenant eligibility, exact source/destination/snapshot versions, explicit single/multiple-owner semantics, shared-fence races, full transaction rollback on real stale ETag, immutable same-intent receipts, lost replies, projection repair without reapplying old roles, bounded read-only lookup, stale legacy OwnerUserId, unsupported provider/default-off configuration and unrelated role/consumer/default/other-tenant preservation. Docs: docs/management/tenant-ownership-transfer.md. Native adapter is trusted; caller authorization/audit and Management routes are IBM-0124. No stable publication or production ownership operation.

PR: https://github.com/MedPACTech/IBeam/pull/32. Source head: 1c4eee34e9eef0af2fcf52e88c59723233a864f6. State: MERGED.
Merged into development: d6384b4c9a541571959a6d43d806ad0e0b3ece57. Applicable PR CI passed before merge. No deliveredUtc is set because development integration is not a production release.

Dependency keys: MLO-002.

### IBM-0124 / MLO-004: Expose scoped owner transfer through target and portal Management APIs

**8 points.** Lane: `done`. Order: 40. Dependencies: IBM-0123.

Add management.identity.owners.transfer and its fixed typed target/portal routes and clients, reusing the existing delegation, exact-version, idempotency and safe receipt mechanisms. Proposed portal path: POST /api/management/applications/{applicationId}/tenants/{tenantId}/ownership/transfers; target equivalent under /api/management-identity.

Paths/surfaces: `IBeam.Management/CustomerIdentity/`; `IBeam.Management.Identity.Target.Services/`; `IBeam.Management.Identity.Target.Api/`; `IBeam.Management.Services/CustomerIdentity/`; `IBeam.Management.Api/`; `IBeam.Management.Identity.Services/`.

Acceptance criteria:

- [x] Transfer is independently authorized by live human assignment, machine capability, target policy and app/environment/realm binding; roles.assign does not imply transfer.
- [x] Discovery advertises transfer only with a working atomic ownership adapter and enforcement protocol; older targets remain usable for their installed operations.
- [x] Actor proofs bind operation, method, route, payload, idempotency and all required versions; reused or cross-app proofs fail.
- [x] Portal polls are read-only and expose safe operation metadata; unknown outcomes do not trigger a second transfer or claim success.
- [x] Metadata-only audit records source/destination IDs and operation outcome without customer profiles or credentials.
- [x] Target/portal integration tests cover revoked assignments, stale versions, unsupported targets, independent permission ceilings and lost replies.

Merged implementation: `773814b5e870f2b3ddd2bdd9f31e5d484472158f`.

IBM-0124: independently scoped target/portal ownership lookup and transfer. 12 portal HTTP checks also passed. 117 focused membership/ownership/provider checks and 1,167 fresh NuGet-only regressions passed, zero failures/skips. Built/packed 31 dependencies at private validation version 2.17.0-dev.mlo.124.3, inspected 16 management roots, and passed package-only portal/two-target smoke. Tests exercise active same-tenant eligibility, exact source/destination/snapshot versions, explicit single/multiple-owner semantics, shared-fence races, full transaction rollback on real stale ETag, immutable same-intent receipts, lost replies, projection repair without reapplying old roles, bounded read-only lookup, stale legacy OwnerUserId, unsupported provider/default-off configuration and unrelated role/consumer/default/other-tenant preservation. Docs: docs/management/customer-ownership-api.md. Independent owners.read/owners.transfer grants, compatible discovery, signed all-version binding, metadata-only source/destination audit, revoked assignment checks and read-only unknown-outcome polling are implemented; roles.assign does not grant transfer. Consumer demo/migration is IBM-0125. No stable publication or production ownership operation.

PR: https://github.com/MedPACTech/IBeam/pull/33. Source head: e892ad74f0986c72f97f152c873edef961e087d7. State: MERGED.
Merged into development: 773814b5e870f2b3ddd2bdd9f31e5d484472158f. Applicable PR CI passed before merge. No deliveredUtc is set because development integration is not a production release.

Dependency keys: MLO-003.

### IBM-0125 / MLO-005: Validate ownership compatibility and publish consumer migration guidance

**5 points.** Lane: `done`. Order: 50. Dependencies: IBM-0124.

Complete package-only native and portal ownership coverage, update bootstrap policy/source and handoff docs, and document the upgrade from UI-only Owner guards and stale owner badges. Existing 2.16 contracts retain their defined editable-role replacement behavior, with unsafe removals rejected.

Paths/surfaces: `scripts/Test-ManagementCustomerIdentityPackages.ps1`; `samples/Management.Bootstrap/`; `docs/management/`; `IBeam.Tests.Management.CustomerIdentity/`.

Acceptance criteria:

- [x] A fresh NuGet-only consumer proves ordinary role editing cannot strip ownership, explicit transfer succeeds, and last-owner and last-admin invariants both hold.
- [x] Already-issued customer JWT/refresh behavior follows committed membership versions after transfer or disable; unrelated tenants remain usable.
- [x] The synthetic demo uses real support/customer MFA, preserves existing seed access changes and contains no private credentials in source or package artifacts.
- [x] Migration guidance identifies full replacement semantics, protected policy boundaries, authoritative owner discovery and required provider/configuration changes.
- [x] No frontend UI, Qurvia implementation, stable publication or production deployment is claimed by this card.

Merged implementation: `55d84e71238c19c7297fc77f96ba476439e31fe5`.

IBM-0125: 120 focused native/target/consumer checks, 12 portal HTTP checks and 1,170 fresh NuGet-only regressions passed, zero failures/skips. Framework code is unchanged from merged IBM-0124, so fresh consumers reuse its validated 31-package 2.17.0-dev.mlo.124.3 feed; 16 management roots inspected, no source framework references. Added actual JWT/refresh tests prove committed transfer or disable invalidates old membership sessions while another tenant remains usable. Constructor and permission compatibility are tested. Portable bootstrap restored/published with 30 framework packages and zero source references; a new non-deploying CI guard repeats that consumer build with generated packages.

Both synthetic smokes passed across three isolated apps with real support and customer password/MFA: narrow role removal and final-owner/admin disable rejected; explicit atomic transfer, authoritative owner lookup, stale JWT rejection, fresh MFA authority and explicit fixture restoration verified. Enterprise acceptance and repeat trusted seed preserving revoked assignments passed. Generated private credentials were checked absent from changed source; private state excluded from Git/packages and test processes stopped. Docs and baseline-app handoff explain full replacement, policy bounds, trusted guard priming, independent grants, token behavior and provider/config migration. Sample explicitly targets upcoming 2.17.0 and accepts an exact generated PackageVersion; stable 2.16.1 is invite-only. No frontend/Qurvia implementation, stable publication or production deployment is claimed.

PR https://github.com/MedPACTech/IBeam/pull/34, state MERGED, source abf4291f1d075bb8d6481fcc98a509009bc5d5f8.
Merged development implementation: 55d84e71238c19c7297fc77f96ba476439e31fe5; applicable CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-004.
## MLO-F02: Explicit operations and supported app extensions (23 points)

Complete in development: IBM-0126 through IBM-0129, all 23 points (8+5+5+5). Immutable typed registry, independent optional native licence and seat administration, signed target/client discovery and dispatch, live explicit human/machine grants, compatible connections and consumer migration guidance. Feature-completion legacy and fresh NuGet-only regression passed 1,401 tests plus required CI. Production licensing requires consumer-provided durable conditional storage and policy; no stable publication or production operation is claimed.

Feature dependencies: MLO-F01.

Feature acceptance criteria:

- Unknown operations and arbitrary URL forwarding remain denied; app extensions are explicitly registered and typed.
- Licensing stays optional and reuses existing Licensing services; tenant access, licence status, seats and ownership remain separate authorities.
- Discovery, human/machine grants, signing, receipts and auditing agree on every registered operation.

### IBM-0126 / MLO-006: Introduce a bounded registered operation catalog and extension contracts

**8 points.** Lane: `done`. Order: 60. Dependencies: IBM-0125.

Generalize fixed operation validation through an immutable host-owned descriptor registry while retaining all built-in 2.16 operation names. Descriptors bind namespace, method, route template, request/result schemas, installed handler, required grants, receipt protocol and audit action. Include explicit data-operation families management.tenants.export and management.tenants.delete without presenting business data as Identity DTOs.

Paths/surfaces: `IBeam.Management/`; `IBeam.Management.Services/`; `IBeam.Management.Identity.Target.Services/`; `IBeam.Management.Identity.Services/`; `IBeam.Tests.Management.CustomerIdentity/`.

Acceptance criteria:

- [x] Names alone cannot grant or advertise authority; only installed registered handlers with valid nonoverlapping descriptors become capabilities.
- [x] Wildcard namespaces, caller-defined routes/URLs, duplicate descriptors, unsafe schemas and incompatible versions fail closed at registration or discovery.
- [x] Credential capabilities, human assignments, delegation, payload limits and audit allowlists use the same registry and preserve independent live access checks.
- [x] Existing diagnostics-only, Identity-only and combined 2.16 targets remain compatible and acquire no new grants automatically.
- [x] Typed extension contracts remain provider-neutral and do not force Identity, Licensing, Billing or consumer assemblies into core Management.

Merged implementation: `cf8106e0411b2843b533e17340abe08c18aa534b`.

IBM-0126 adds an immutable host-owned bounded registry and typed provider-neutral extension contracts, preserving all existing diagnostic/customer Identity names and old public constructor/static overloads. Extension descriptors bind exact GUID routes, method, strict versioned sealed DTO schemas, handler interface, explicit prerequisite closure, receipt version, bounded payloads and exact metadata-only audit action. Duplicate/overlapping/wildcard/unsafe/incompatible registrations fail closed. Export/delete families are reserved but uninstalled and confer no authority. Credential validation/issuance, live human app assignments, support Identity app-scoped ceilings, request binding and audit allowlists use the host catalog. Optional app capability preflight independently verifies live machine state, enablement and actual installed handlers, separate from Identity DTOs; it invokes no handler and installs no generic HTTP dispatch (IBM-0129). No new grants are added automatically.

Validation: 76 focused registry/legacy-discovery tests and 12 portal ownership/membership checks passed, no failures/skips. Built local 2.17.0-dev.mlo.126.1 feed: 31 dependencies and 16 management roots inspected, package-only portal/two-target smoke passed. Eight fresh NuGet-only suites passed 1,220 regressions with zero source framework references and no failed/skipped fixtures. Tests cover descriptor/schema/version/route substitution, typed body limits, live human/key revocation, missing adapters, explicit ceilings and audit payload/provider canaries. Private emulator processes stopped after validation. Documentation describes trusted registration, safe metadata, compatibility and staged dispatch boundaries. No stable package publication or production operation is claimed.

PR https://github.com/MedPACTech/IBeam/pull/35, state MERGED, source 1ee2d170ab3a7580032dd9f0068bc92e4c7de6bf.
Merged development implementation: cf8106e0411b2843b533e17340abe08c18aa534b; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-005.

### IBM-0127 / MLO-007: Add optional licence suspend and restore Management adapters

**5 points.** Lane: `done`. Order: 70. Dependencies: IBM-0126.

Provide independent management.licenses.suspend and management.licenses.restore descriptors and typed target/portal operations that delegate to ITenantLicenseService in an optional integration module. Do not reuse tenants.status or turn Billing state into runtime authorization.

Paths/surfaces: `IBeam.Management/`; `IBeam.Management.Licensing/ (new optional module)`; `IBeam.Management.Services/`; `IBeam.Management.Api/`; `IBeam.Tests.Licensing/`.

Acceptance criteria:

- [x] Suspension and restoration require explicit independent grants and only affect the selected app/tenant/license.
- [x] Restoration respects current licence validity, revocation, expiry and host policy; it cannot resurrect an invalid entitlement or reactivate memberships.
- [x] Exact versions, idempotency, unknown-outcome reconciliation and safe audit cover both transitions.
- [x] Missing Licensing adapters advertise no licence capability and do not affect Identity/diagnostic startup.
- [x] Adapter tests reuse native Licensing rules and prove tenants.status alone cannot suspend or restore a licence.

Merged implementation: `3d19ddc14db6ed6ba96b96d9965846e0b1674cff`.

IBM-0127 adds optional IBeam.Management.Licensing with independent management.licenses.read/suspend/restore descriptors, typed target/portal adapters and opt-in portal read/POST/receipt endpoints. Base Management API/core retain no native Licensing dependency and expose no optional controller automatically. Writes require their own grant plus explicit read authority; tenants.status/memberships.manage are not borrowed. Native trusted status administration uses exact revisions, all-writer fencing and atomic immutable same-intent receipts. It suspends only runtime-eligible licences and restores only the original active/trialing/grace/manual status, checking current expiry/start/revocation/grace and target-owned policy. Other licence contents, commercial/provider data, seats, memberships, roles and tenant status are preserved. Production registration requires a durable conditional provider; the shipped in-memory implementation is explicit Development/Testing only. Existing constructors/interface methods remain compatible with default unavailable administration.

A provider reply lost after commit produces scoped unknown metadata (202/Location in portal), never a mutation retry. Polls are read-only under current authority; same-intent replay cannot reapply an older suspension after later changes. Metadata-only audit distinguishes uncertain attempts and receipt lookup and excludes payloads, versions and provider errors. Standard signed extension dispatch/client and target HTTP discovery remain IBM-0129 per the existing dependency order; this card supplies typed integration ports/handlers and portal endpoints and does not claim that future transport is implemented.

Validation: 41 new native/adapter/registration/concurrency/audit/portal HTTP checks and 57 existing native Licensing tests passed. Fresh local 2.17.0-dev.mlo.127.1 feed: 34 dependencies, 20 inspected roots, package-only portal/two-target smoke and 1,318 NuGet-only regressions across ten suites passed, zero source framework references, failures or skips. Legacy registry/discovery focused checks (76) and portal ownership/membership checks (12) also passed. Inspection caught two existing missing agent guides in Licensing.Credits and Credits.Services; documentation entries were added and only their unchanged assemblies repacked before first consumption. Private fixture processes were stopped. No stable publication or production operation.

PR https://github.com/MedPACTech/IBeam/pull/36, state MERGED, source 629505b3b6c727789ca44fc792058ae9ef73aac5.
Merged development implementation: 3d19ddc14db6ed6ba96b96d9965846e0b1674cff; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-006.

### IBM-0128 / MLO-008: Add independent seat assignment and revocation Management adapters

**5 points.** Lane: `done`. Order: 80. Dependencies: IBM-0127.

Expose management.seats.assign and management.seats.unassign through the optional Licensing module, delegating to ILicenseSeatAssignmentService. Use explicit license/assignment/member scope and existing capacity/eligibility policies.

Paths/surfaces: `IBeam.Management.Licensing/`; `IBeam.Management.Services/`; `IBeam.Management.Api/`; `IBeam.Tests.Licensing/`.

Acceptance criteria:

- [x] Seat assignment and revocation have separate grants and do not inherit memberships.manage authority.
- [x] Capacity, active licence, subject eligibility and tenant ownership are checked by existing Licensing services before writes.
- [x] Retries cannot consume a second seat or revoke another assignment; concurrent requests have bounded deterministic outcomes.
- [x] Seat operations do not create memberships, grant roles or change tenant/user status.
- [x] Discovery and error handling cover absent adapters, exhausted seats, revoked access and uncertain provider outcomes.

Merged implementation: `70a3c2fa9a135307df58301eacaeca4552d17040`.

IBM-0128 adds independent optional management.seats.read/assign/unassign descriptors, typed target/portal adapters and opt-in portal read/POST/receipt endpoints. Writes require their own grant plus seat read; memberships.manage and licence/status/role grants are not borrowed. Native trusted administration checks active licence/capacity and explicit customer subject/tenant policy. One licence aggregate revision fences all licence/status/seat writers; exact assignment/user binding and atomic immutable receipts prevent duplicate allocation or removing another/replacement assignment. Legacy protected user writers retain native eligibility policy; unprotected flows and old constructors/default-interface behavior remain usable. No memberships, roles, tenant/user status or licence contents are changed. Production requires a durable native provider; volatile fixtures are explicit Development/Testing only.

Unknown provider/transport replies produce scoped metadata and authorized read-only polling, never automatic mutation retry. Same-intent historical replay never recreates a removed seat or revokes a newer assignment. Read metadata is bounded to 2,000 assignments/256 KiB and excludes opaque native subject strings, names, profiles and provider data. Metadata-only audit distinguishes uncertain writes from lookup and excludes versions/keys/payloads/errors. Standard signed extension client/dispatch remains IBM-0129 per the dependency order; this card does not claim that future transport is complete.

Validation: 78 optional Management Licensing checks (37 new seat cases) and 57 existing native Licensing tests passed. Fresh local 2.17.0-dev.mlo.128.1 feed: 34 dependencies, 20 inspected roots, package-only portal/two-target smoke and 1,355 NuGet-only regressions across ten suites passed, with no source references, failures or skipped fixtures. Tests cover independent live grants, cross-scope denial, disabled/foreign users, inactive licences, exhausted/last-seat capacity races, concurrent same-intent replay, changed intents, replacement assignment safety, native all-writer/version/policy fencing, bounded metadata/profile canaries, absent adapters, production-store rejection, portal HTTP/auth, unknown polling, scoped transport references and actual safe audit. Legacy registry/discovery (76) and portal ownership/membership (12) checks also passed. Private fixture processes stopped. No stable publication or production operation.

PR https://github.com/MedPACTech/IBeam/pull/37, state MERGED, source 6a699c166a48ed7e423839a71400ec40f221c37c.
Merged development implementation: 70a3c2fa9a135307df58301eacaeca4552d17040; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-007.

### IBM-0129 / MLO-009: Wire extension discovery, grants and consumer permission migration

**5 points.** Lane: `done`. Order: 90. Dependencies: IBM-0128.

Complete target/portal typed dispatch and safe operation listing for registered extensions; provide migration guidance for Qurvia licence, seat and owner routes currently borrowing other permissions. Default grants remain unchanged.

Paths/surfaces: `IBeam.Management.Services/`; `IBeam.Management.Api/`; `docs/management/`; `samples/Management.Bootstrap/`.

Acceptance criteria:

- [x] A support user can invoke only advertised operations permitted by both current app assignment and machine credential.
- [x] Borrowed permissions are not automatically upgraded to commercial, transfer, export or deletion authority.
- [x] Client dispatch binds a registered descriptor and scoped resource IDs; there is no free-form target URL/method/body proxy.
- [x] Mixed-version target tests cover unavailable operations and stable diagnostics/customer-management behavior.
- [x] Migration examples explain consumer-owned handler registration and required re-grant/reconnect steps without embedding credentials.

Merged implementation: `e4769e1526c3111cd0e5b32abb1ce24f99ae61b0`.

IBM-0129 completes optional registered operation discovery and signed typed dispatch. New IBeam.Management.Operations.Target.Api has no native Identity, Licensing or consumer dependency. It maps only trusted registered descriptors plus fixed info/read-only receipt paths. The standard portal client binds the exact registered contract/method/path/body, selected app/realm/tenant/resource, current human, idempotency key and If-Match. Live machine key version/status/capabilities and target enablement are rechecked; short-lived distinct RS256 actor proofs use pinned issuer/audience keys and atomic mutation nonce replay protection. The typed licence and seat handlers now supply read-only reconciliation through this transport. Existing Identity/diagnostic protocols, constructor overloads, default two-probe behavior and default permissions remain compatible.

Connection discovery is opt-in and persists compatible operation fingerprints/schema/receipt versions in an optional Azure Table field. Human listing filters by live user/app/assignment and verified key metadata. Each actual call refreshes target discovery and repeats authorization. No arbitrary URL/method/body proxy, automatic borrowed-permission upgrade, payload persistence or mutation retry is introduced. Failed/ambiguous write replies retain only scoped IDs for authorized read-only reconciliation. Consumer docs specify native durable providers, key/realm/trust configuration, typed handler registration, explicit independent licence/seat/ownership grants, key rotation and PUT reconnect; connection Test is read-only. Export/purge remain later features.

Validation: 46 new real signed HTTP tests passed, including native licence/seat execution, independent live assignment matrices, scope/issuer/audience/type/expiry/body/header substitution, replay, revocation races, registered-only negotiation, incompatible metadata and lost/malformed/oversized/redirected write recovery. Feature-completion legacy/fresh NuGet-only regression passed 1,401 tests across eleven suites with zero skipped fixtures or source references; 35 dependencies, 21 inspected package roots and portal/two-target smoke passed using private 2.17.0-dev.mlo.129.1 packages. Additional legacy registry/discovery (76) and portal ownership/membership (12) source checks passed. Test cadence follows the user instruction: new/focused per card, full legacy at feature completion, required PR CI retained. No stable publication or production operation.

PR https://github.com/MedPACTech/IBeam/pull/38, state MERGED, source ded942419cb1144ee1dd72108bce6a7bdd9847a8.
Merged development implementation: e4769e1526c3111cd0e5b32abb1ce24f99ae61b0; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-008.
## MLO-F03: App-owned tenant export (23 points)

Complete in development: IBM-0130 through IBM-0133, all 23 points (5+8+5+5). Versioned safe contracts, opt-in signed target boundary, durable scoped export journal and private delivery recovery, portal submit/status and tested synthetic consumer integration. Full feature legacy/fresh NuGet-only regression passed 1,484 tests plus required CI. Actual business inventory, storage, native policy and customer delivery remain app-owned. No stable publication or production export is claimed.

Feature dependencies: MLO-F02.

Feature acceptance criteria:

- Export adapters define coverage and exclusions; unsupported targets do not claim complete export.
- Business payloads, credentials and download capabilities stay out of support responses, receipts, logs and audit.
- Leased work, cancellation, replay and artifact expiry have explicit durable behavior.

### IBM-0130 / MLO-010: Define export job, coverage manifest and target handler contracts

**5 points.** Lane: `done`. Order: 100. Dependencies: IBM-0129.

Add optional IBeam.Management.TenantData.Target.Services and .Api modules with IManagementTenantExportHandler, versioned job DTOs and a coverage manifest. Define submission/status routes under /api/management-tenant-data and matching scoped portal routes. Separate private artifact references from safe public status.

Paths/surfaces: `IBeam.Management/ (tenant-data contracts)`; `IBeam.Management.TenantData.Target.Services/ (new)`; `IBeam.Management.TenantData.Target.Api/ (new)`; `IBeam.Management.Repositories.AzureTable/`.

Acceptance criteria:

- [x] Contracts distinguish requested, running, partially completed, succeeded, failed, cancelled and expired states without ambiguous success.
- [x] Coverage names expected tenant datasets, attachments and omitted/retained categories without placing their contents in Management metadata.
- [x] Export is opt-in with independent capability, app/environment/tenant scope and a registered usable handler.
- [x] Customer delivery authorization is a target-owned port; support permission alone does not authorize reading exported business data.
- [x] Core contracts remain usable without native Identity or commercial packages.

Merged implementation: `2593caa13bf258f0f22eeeb290e749302e71892f`.

IBM-0130 defines version-1 app-owned tenant export job, coverage/inventory and handler contracts in Core Management, plus optional TenantData.Target.Services and .Api packages without native Identity or commercial dependencies. Submission acceptance/lookup/unknown outcomes are distinct from requested/running/partially-completed/succeeded/failed/cancelled/expired job state. Safe metadata is reconstructed against a bounded static consumer inventory; missing/duplicate/undeclared datasets or attachments cannot report complete success. Private artifact/output references are JSON-hidden and ToString-redacted, with no customer payload, filename, recipient, destination or download URL in support DTOs.

Default disabled registration installs no capability or route. Enabling requires compatible app exporter, target-owned tenant/version and current status policy, customer delivery/retrieval authorization and durable job service; volatile jobs require an explicit Development/Testing opt-in and fail in Production. The standard framework worker/store remains IBM-0131, portal facade IBM-0132, and synthetic full consumer coverage IBM-0133; this card does not claim those implementations or Qurvia's exporter. Hosts can implement the job port independently.

The independent management.tenants.export descriptor permits only its exact Core request/result contracts and compiled POST /api/management-tenant-data/tenants/{tenantId}/exports path. Its GET tenant/job status alias is signed read-only receipt reconciliation with original export permission and exact job/tenant binding. No free-form native-prefix proxy or support artifact download endpoint is added. Borrowed tenant-status/membership grants and the reserved delete operation remain unchanged. Unexpected provider exceptions are translated to fixed safe errors; uncertain results do not become successful export audit, and lookup audit is separate.

Validation: 37 new focused Release tests passed with zero failures/skips, covering versioned states, partial/complete coverage, strict schema bounds, private-reference redaction, absent/incompatible/production-volatility providers, compiled route/hash restrictions and real persisted-key/RS256 HTTP submit/status, independent grants, policy denial, request/body/version/scope tampering, nonce replay, safe provider failures and unknown results. Five changed production modules built, packed and inspected for required package guides at private 2.17.0-dev.mlo.130.1. Full local legacy/fresh NuGet-only regression is deliberately deferred to IBM-0133 feature completion under the user cadence; required repository PR CI remains mandatory. No stable publication or production export.

PR https://github.com/MedPACTech/IBeam/pull/39, state MERGED, source db7ff66488001765de9f3487e8efd644d356abd6.
Merged development implementation: 2593caa13bf258f0f22eeeb290e749302e71892f; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-009.

### IBM-0131 / MLO-011: Implement durable export execution and private artifact delivery

**8 points.** Lane: `done`. Order: 110. Dependencies: IBM-0130.

Implement leased export jobs, idempotent submission, resumable app handler work and target-owned artifact retention. App exporters enumerate tenant data, including historical memberships where their coverage contract requires it, without confusing current seat eligibility with data ownership.

Paths/surfaces: `IBeam.Management.TenantData.Target.Services/`; `IBeam.Management.Repositories.AzureTable/`; `IBeam.Tests.Management.TenantData/ (new)`.

Acceptance criteria:

- [x] Duplicate submissions and worker crashes reconcile one stable job; leases prevent concurrent destructive/redundant phases.
- [x] The handler defines snapshot/consistency boundaries and returns a complete or explicitly partial manifest; missing datasets cannot be reported as complete.
- [x] Private artifacts use bounded lifetime and a customer-authorized retrieval path; guessed, expired, cross-tenant or revoked access is denied.
- [x] Support DTOs, logs, audit and exceptions contain no payloads, filenames with PII, bearer download URLs, keys or storage credentials.
- [x] Cancellation and cleanup are bounded and do not delete unrelated artifacts or another job output.

Merged implementation: `3c47792aac760324d82e8e8a56b91711b486c20b`.

IBM-0131 implements standard durable app-owned export jobs with a dedicated scoped Azure Table journal, atomic same-intent creation, immutable fingerprints and exact ETag updates. Person/app/environment/realm/tenant/job bindings prevent cross-scope access. Frozen inventory distinguishes complete versus explicitly partial coverage; source data and attachments remain in the consumer's private artifact channel, including historical/removed/unseated member data where its inventory requires it. Submission acceptance is not business completion. Native Identity and commercial dependencies remain optional.

The leased worker exposes a current fence and artifact deadline to app-owned idempotent exporter/delivery providers. Stale workers cannot publish through the supported contract. Crash recovery uses the same scoped consumer job, not a new export. Positive pending signals permit same-job reconciliation; unknown export, delivery, storage and cleanup outcomes pause rather than trigger automatic mutation retries. Trusted explicit reconciliation, native policy checks, cancellation, expiration and exact-job cleanup are provided. Historical disabled bindings remain eligible for cleanup, with bounded metadata-only scans or a host-owned scheduler. Accepted jobs are bounded durable commands; support revocation blocks new support calls/polls, and in-flight work is cancelled separately or stopped by current native policy. Provider-owned snapshot consistency, artifact ownership and idempotency are explicit contracts rather than cross-system transaction claims.

Private references use a versioned retained 32-byte key ring and AES-GCM bound to full scope, job, policy/inventory, intent and lifetime. Ciphertext is stored in a separate column and excluded from safe JSON/audit. The server-only customer artifact gate rejects Management principals, guessed/foreign/expired/cancelled/incomplete outputs and revoked customer authorization; it rechecks authority around lookup and private ownership validation. No support download route, bearer URL, payload logging, recipient impersonation or arbitrary destination is added. Worker audit uses bounded system metadata and does not impersonate the initiating support person. Provider errors are fixed safe codes; late audit failures reconcile from the journal without replaying consumer work.

Validation: 24 new focused Release tests passed with zero failures/skips, including the actual private Azure Table provider and standard service/worker through the real persisted-key/RS256 HTTP boundary. Cases cover concurrent duplicate/changed intent, same-job restart, lease expiry/stale-worker fencing, lost export/delivery/storage replies and explicit reconciliation, positive pending work, customer revocation races and foreign scopes, full/partial/missing coverage, cancellation during active work, expiration, historical binding cleanup, no unrelated artifact deletion, key rotation/ciphertext substitution, unavailable storage, secret serialization and no runtime schema creation. Five changed production modules built/packed/inspected at private 2.17.0-dev.mlo.131.1; owned private fixture processes stopped. Full local legacy/fresh NuGet-only matrix remains deferred to IBM-0133 export feature completion under the user's cadence; required repository PR CI is retained. No stable publication, production export or Qurvia business exporter implementation.

PR https://github.com/MedPACTech/IBeam/pull/40, state MERGED, source 876012ff6513373e22a622ef247cb5ccf75a8d6f.
Merged development implementation: 3c47792aac760324d82e8e8a56b91711b486c20b; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-010.

### IBM-0132 / MLO-012: Expose scoped portal export requests and safe progress polling

**5 points.** Lane: `done`. Order: 120. Dependencies: IBM-0131.

Add typed export submit/status clients and Management API routes, reusing registered operation descriptors and signed delegation. Deliver artifacts through the app-owned recipient channel rather than proxying tenant content through the support API.

Paths/surfaces: `IBeam.Management.Services/ (tenant-data clients)`; `IBeam.Management.Api/ (tenant-data controllers)`; `IBeam.Management.TenantData.Target.Api/`.

Acceptance criteria:

- [x] Every submit/poll checks current support app assignment, operation grant, target credential, realm and host policy.
- [x] Unknown HTTP outcomes preserve one job reference and do not blindly launch another export.
- [x] Recipient and destination are validated by trusted target policy; callers cannot supply arbitrary export delivery URLs or use support identity as customer identity.
- [x] Status reports safe coverage/progress only; a separately authorized operator-download feature is not implied.
- [x] Cross-app IDs, replayed actor assertions, unavailable targets and revoked assignments fail safely.

Merged implementation: `83f7e6470f410f177fb3c9da13cfccb024987063`.

IBM-0132 adds explicit scoped portal POST /api/management/applications/{app}/tenants/{tenant}/exports and GET .../exports/{job} with typed Core contracts and IManagementTenantExportPortalService. No native exporter/job store/customer retrieval or commercial dependency is installed in the portal. Both registrations use the same immutable Core operation descriptor. The shared signed client enforces current support user/app/assignment, verified connection, fresh target machine/enablement/schema metadata, exact signed app/realm/person/tenant/job scope and target-native policy.

The strict HTTP input contains only target-owned policyKey plus exact If-Match and Idempotency-Key. Callers cannot choose recipient, destination URL, artifact reference or free-form native route/body. POST acceptance is distinct from business completion; known status is safe coverage/progress only. Both ends reconstruct bounded UTC/version/state/outcome/coverage/failure metadata. The registered client validates returned job identity and export metadata before releasing its known intent, so lost or semantically unsafe mutation replies retain the original scoped unknown job. Authorized polling is read-only and never launches a replacement export; unknown outcomes are not successful business audit. Responses are no-store with body/header logging disabled. No support artifact download or customer impersonation is added, and grants remain explicit.

Validation: 17 new focused Release tests passed with zero failures/skips using real portal HTTP, standard signed RS256/machine-key target transport and the durable journal/worker. Cases cover duplicate acceptance and completed polling, live assignment/user/app revocation before secret resolution, borrowed tenant-status permission and customer-Bearer denial, strict recipient/destination/body/version/key rejection, cross-app connection isolation, machine/capability revocation, lost/foreign job and unsafe native metadata recovery, read-only polling after uncertainty and revocation, and base/optional route absence. Four changed modules built, packed and inspected at private 2.17.0-dev.mlo.132.1. Full local legacy/fresh NuGet-only regression is deferred to IBM-0133 export feature completion under the user's cadence; required repository PR CI remains mandatory. No stable publication or production export.

PR https://github.com/MedPACTech/IBeam/pull/41, state MERGED, source 20e2aa38aba9da44a56c32e22f086e4cddbf008a.
Merged development implementation: 83f7e6470f410f177fb3c9da13cfccb024987063; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-011.

### IBM-0133 / MLO-013: Validate complete export coverage with a synthetic consumer hook

**5 points.** Lane: `done`. Order: 130. Dependencies: IBM-0132.

Supply a synthetic app exporter and package-only tests covering data categories reported missing by consumers. Document the Qurvia implementation contract and manifest expectations; do not claim to implement Qurvia business-table exports in this repository.

Paths/surfaces: `samples/Management.Bootstrap/`; `IBeam.Tests.Management.TenantData/`; `docs/management/tenant-export.md (new)`; `scripts/`.

Acceptance criteria:

- [x] Synthetic coverage includes facilities/settings/commercial metadata/history/attachments and data linked to removed or unseated members where explicitly declared.
- [x] Tests prove business data reaches only the app-authorized recipient and never support metadata or telemetry.
- [x] Expired artifacts, partial coverage, failed handlers, replay/restart and unavailable storage have explicit tested outcomes.
- [x] Fresh NuGet-only installation advertises export only when the exporter and delivery adapters are installed.
- [x] Consumer guidance defines required inventory and consistency decisions; no production export or new legal retention promise is made.

Merged implementation: `952d747dc1efb4e7037af9cdb8b8d06d667f5018`.

IBM-0133 completes the tenant-export feature with an explicitly Development/Testing-only synthetic app provider and a tested consumer integration example. Its static nine-category inventory includes facilities, settings, commercial data, audit history, active/removed/unseated member chats, audio and attachments. The private file-backed snapshot, delivery receipt, exact job ownership, native customer authorization and cleanup use the standard framework durable job/worker/customer gate. Partial coverage declares retained commercial data without exporting its bytes. Provider restart reconciles the same scoped job; storage uncertainty pauses until explicit reconciliation. Support status contains metadata only and exposes no business data, recipient or artifact handle. Production registration is rejected.

The consumer guide covers the real Qurvia inventory, Clinic/Enterprise eligibility, historical member ownership, actual file inclusion, app-owned consistency and approved customer delivery, retention and recovery decisions. This is a synthetic example and integration contract, not an implementation of Qurvia business-table export. No production data or stable publication is involved.

Validation: five new synthetic consumer tests passed with zero failures/skips. Full feature-completion legacy and fresh NuGet-only regression passed 1,484 tests across twelve consumer suites, zero failures/skips/source framework project references, 37 dependencies and 22 inspected management package roots at private 2.17.0-dev.mlo.133.1. The isolated portal/two-target package smoke and 76 registry/discovery plus 12 portal ownership/membership source regressions also passed. New/focused tests run per card; the full local legacy matrix runs at feature completion under the user instruction, with required repository CI retained.

PR https://github.com/MedPACTech/IBeam/pull/42, state MERGED, source 2a69cea398c9c68d17c2a1fec1592f66f12cd770.
Merged development implementation: 952d747dc1efb4e7037af9cdb8b8d06d667f5018; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-012.
## MLO-F04: Recoverable tenant purge (31 points)

Complete in development: IBM-0134 through IBM-0138, all 31 points (5+8+8+5+5). Independent expiring preflight/exact one-use confirmation, durable scoped coordination and all-writer fences, optional verified tenant-only Identity cleanup, typed signed portal progress and synthetic consumer/file recovery are implemented. Shared global identities/other tenants survive, retained/excluded scope stays explicit and unknown work requires trusted same-job reconciliation. Full feature legacy/fresh NuGet-only regression passed 1,621 tests plus required CI. Real app business inventory/providers remain consumer-owned; no stable publication or production purge is claimed.

Feature dependencies: MLO-F03.

Feature acceptance criteria:

- A status/deactivation grant never permits deletion; fresh authorization and an expiring version-bound confirmation are required.
- Application hooks own business tables/files and retention declarations; portal code never directly queries customer storage.
- Stable phases, write fencing and reconciliation prevent duplicate purge, cross-tenant deletion and false complete outcomes.

### IBM-0134 / MLO-014: Add purge planning, scope inventory and confirmation contracts

**5 points.** Lane: `done`. Order: 140. Dependencies: IBM-0133.

Define IManagementTenantPurgeHandler and versioned preview/confirm/status DTOs. Proposed portal routes are POST .../tenants/{tenantId}/purge-plans and POST .../tenants/{tenantId}/purges. Bind confirmation to target app/environment/tenant, inventory/version, actor, expiration and the independent management.tenants.delete grant.

Paths/surfaces: `IBeam.Management/ (tenant-data contracts)`; `IBeam.Management.TenantData.Target.Services/`; `IBeam.Management.TenantData.Target.Api/`.

Acceptance criteria:

- [x] Preflight is read-only and reports affected categories and retained/excluded scope without returning business data.
- [x] Confirmation requires fresh server-validated support authentication, exact intent/version and an unexpired one-use plan reference.
- [x] Tenant status, roles.assign, licence or export grants alone cannot plan/confirm purge.
- [x] Stale inventory, changed scope, disabled credentials or missing required handlers prevent destructive execution.
- [x] Global account deletion is explicitly excluded from tenant purge authority.

Merged implementation: `d90e5619ce2f5789ccf23be44093ce68172abaf6`.

IBM-0134 defines Core versioned purge plan/confirm/status DTOs and IManagementTenantPurgeHandler/Policy/JobService contracts. Independent management.tenants.delete is bound to its compiled typed command, never borrowed from tenant status, roles, licences or export. Optional AddIBeamManagementTenantPurgeTarget/TargetApi installs nothing by default and requires compatible native handlers/policy/live access plus atomic durable plan/job providers. Global accounts must be explicitly excluded; no arbitrary storage selector or business payload is accepted.

Preflight calls only the read-only preview hook and rechecks native revision and live grants. Plans bind application key/environment, org/app/realm/tenant/person, policy, exact version, static inventory and UTC expiry (30-300 seconds). Confirmation verifies the same immutable plan and current inventory/native policy before the provider's atomic consume-and-reserve contract. Same-job replay is distinct from a second intent. Neither preflight nor confirmation calls a destructive hook inline. Status reconstructs safe coverage/fixed failures and cannot claim global-account deletion or success with incomplete required coverage. Provider errors are suppressed; audit is metadata only.

Validation: 36 new focused Release tests passed, zero failures/skips, including strict schema/registry, missing and incompatible providers, explicit development registration, immutable preflight, one-use provider fixture, borrowed grants, scope/version/inventory/expiry tampering, revocation and native revision changes before confirmation, and safe result/error handling. Three changed modules built, packed and inspected at private 2.17.0-dev.mlo.134.1. Full local legacy/fresh NuGet-only regression is deferred to IBM-0138 feature completion under the user's cadence; required PR CI is retained. Production durable coordination is IBM-0135, Identity cleanup IBM-0136 and typed portal integration IBM-0137. No production purge or stable publication.

PR https://github.com/MedPACTech/IBeam/pull/43, state MERGED, source 8b6cb467a9ffebc0b1a7e4a9dcb942660c3b4cff.
Merged development implementation: d90e5619ce2f5789ccf23be44093ce68172abaf6; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-013.

### IBM-0135 / MLO-015: Implement durable purge coordination and app-owned cleanup phases

**8 points.** Lane: `done`. Order: 150. Dependencies: IBM-0134.

Coordinate write/access fencing, app-owned database/blob cleanup, resumable checkpoints and final verification under a stable purge job. Reconcile uncertain app work before retrying; no destructive rollback or resetting disabled access.

Paths/surfaces: `IBeam.Management.TenantData.Target.Services/`; `IBeam.Management.Repositories.AzureTable/`; `IBeam.Tests.Management.TenantData/`.

Acceptance criteria:

- [x] A confirmed purge blocks new tenant access and app writes before deletion through an explicit target integration contract.
- [x] All destructive phases are idempotent or support authoritative reconciliation with operation IDs; unknown outcomes never cause blind duplicate work.
- [x] Worker concurrency, lease loss, partial failure, cancellation boundaries and restart have deterministic durable states.
- [x] No final success is emitted until required app phases and coverage verification complete; retained categories remain visible as metadata.
- [x] Hooks cannot receive arbitrary cross-tenant storage selectors from the Portal and do not expose payloads or secrets in audit.

Merged implementation: `30264cf0d5a248346f98a15539f9769a9a54b31d`.

IBM-0135 supplies the standard IManagementTenantPurgeJobService, entity-focused dedicated Azure Table journal, leased worker and optional disabled-by-default bounded recovery scanner. An exact-ETag transaction consumes the original scope-bound plan and reserves its sole stable job. Replayed plans preserve the original lifetime; known same-intent confirmation receipts remain read-only under current access/status policy after version, inventory or expiry changes. Frozen inventory, required fence keys and native versions remain immutable.

Every required app/native access/write fence must be compatible and job-owned; the native revision is checked again before deletion. Consumers must actually integrate customer requests, refreshes and all writes and implement native idempotent/conditional commits. Missing/incompatible fences and changed host authority stop work. The worker persists uncertainty before each external phase, fences journal writes with bounded leases/ETags and pauses lost replies, crashes or lease loss for explicit trusted same-job reconciliation. Only positive same-job pending signals may resume automatically. Cancellation is limited to before any fence attempt. No reset/reactivation, global-account cleanup, arbitrary storage selector or business payload is introduced. Explicit system audit marks only verified complete coverage successful; retained/partial/unknown states stay explicit. Optional native Identity integration remains IBM-0136, portal endpoints IBM-0137.

Validation: 32 new focused tests passed with zero failures/skips. Real owned private Table fixtures exercise concurrent one-use confirmation, immutable plan expiry, exact ETags, other-tenant/global-account isolation, lost replies in fencing/purging/verification, process/worker restart, lease loss, cancellation boundaries, partial/invalid coverage, explicit verification repair without duplicate native deletion, current host/native version fencing, scoped bounded scans and safe metadata/system attribution. Startup/scanner tests reject missing/incompatible stores/fences, keep scanning disabled by default, rotate bindings and skip uncertain work. Five changed modules built, packed and inspected at private 2.17.0-dev.mlo.135.1. Full local legacy/fresh NuGet-only regression is deferred to IBM-0138 feature completion under the user's cadence; required PR CI remains mandatory. No production purge or stable publication.

PR https://github.com/MedPACTech/IBeam/pull/44, state MERGED, source ae521a8c178cb111e965736b5abe2ea83eb5db98.
Merged development implementation: 30264cf0d5a248346f98a15539f9769a9a54b31d; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-014.

### IBM-0136 / MLO-016: Implement optional tenant-scoped Identity cleanup without deleting shared accounts

**8 points.** Lane: `done`. Order: 160. Dependencies: IBM-0135.

Add an optional IBeam.Management.TenantData.Identity bridge and provider cleanup ports for tenant memberships, roles, invitations, indexes and tenant metadata. Deliberate whole-tenant deletion has its own verified context rather than disabling last-owner checks for ordinary writers.

Paths/surfaces: `IBeam.Management.TenantData.Identity/ (new optional module)`; `IBeam.Identity/Interfaces/`; `IBeam.Identity.Services/`; `IBeam.Identity.Repositories.AzureTable/`.

Acceptance criteria:

- [x] Only a validated confirmed purge context can run cleanup; no general-purpose skip-owner/skip-authorization flag is exposed.
- [x] All tenant-scoped primary and secondary records are inventoried and reconciled, including pending invitations and stale indexes.
- [x] Global user credentials, OTPs, sessions/preferences used by other tenants and unrelated tenant data are preserved; orphan account deletion requires separate explicit account-level authority.
- [x] Concurrent invitation acceptance, role writes, membership creation and refresh cannot recreate access during or after purge fencing.
- [x] Unsupported providers fail closed; partial cleanup reports progress and never claims atomic multi-table deletion.
- [x] Real provider tests cover overlapping users/IDs across two tenants, interruptions and exact-scope deletion.

Merged implementation: `4c736bd190c5892e6842b4e29bfb92ad28f71117`.

IBM-0136 adds optional IBeam.Management.TenantData.Identity with native provider-neutral cleanup ports and opaque authorization created only by a service after trusted confirmed-job verification. The bridge checks original consumed plan, confirmed job, full app/environment/org/app/realm/tenant/person/version scope and active worker lease. Journal-only proof prevents recursive native fence validation; the standard worker supplies that lease on fence rechecks. No support/customer claims, known GUID or generic skip-owner flag can create native deletion authority.

Native Azure protection is explicit opt-in and requires ten distinct pre-provisioned tables with all writers upgraded. Entity-family ports cover metadata, memberships/reverse indexes, roles, pending and historical attributable invitation indexes, tenant API/agent credentials, tenant OAuth consent and catalog overrides. Writers register partitions and join local exact-ETag guards, preventing prepared late writes after cleanup. Bounded durable discovery/fencing/cleanup checkpoints reconcile the same scoped job. Permanent minimal scope/job/index tombstones are declared retained metadata. Global users/passwords, OTPs, shared sessions, unscoped consent/preferences and other tenants are preserved. Ordinary independent last-owner/admin checks remain active. Native invitation and credential lookup stops at the initial tenant fence; marker-only status lookup supports verified receipts after metadata removal. Default bridge disabled; missing app fence, unprotected providers or aliased table families fail startup. No runtime schema creation, native Identity Target API/commercial dependency, business-data response or production purge.

Validation: 21 new focused tests passed, zero failures/skips, against owned loopback Table fixtures. They verify shared-user roles/default/current access and refresh in another tenant, independent last-owner protection, all ordinary writer families denied after purge, actual late prepared token/member-index races, bounded checkpoints across scopes, orphan index cleanup, lost-delete pause and explicit same-job recovery, unscoped account consent preservation, immediate credential/invitation denial, rejected direct native calls and unsupported startup. Eight changed modules built, packed and inspected at private 2.17.0-dev.mlo.136.1. Full local legacy/fresh NuGet-only regression is deferred to IBM-0138 feature completion under the user's cadence; required PR CI remains mandatory. Portal integration remains IBM-0137. No stable publication.

PR https://github.com/MedPACTech/IBeam/pull/45, state MERGED, source 98e6d19e203a5a9105a67968a52be689f33c682c.
Merged development implementation: 4c736bd190c5892e6842b4e29bfb92ad28f71117; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-015.

### IBM-0137 / MLO-017: Expose purge preflight, confirmation and progress through the Portal API

**5 points.** Lane: `done`. Order: 170. Dependencies: IBM-0136.

Connect typed portal/target purge routes, current grants, signed confirmation, stable job receipts and safe polling. The Portal manages intent and progress only; the consumer target owns deletion.

Paths/surfaces: `IBeam.Management.Services/ (tenant-data clients)`; `IBeam.Management.Api/ (tenant-data controllers)`; `IBeam.Management.TenantData.Target.Api/`.

Acceptance criteria:

- [x] Preflight/confirm/poll bind the same app, tenant, actor and plan; replay, resource substitution and stale confirmation fail.
- [x] Revoked support assignment or machine capability is enforced live, including on progress polling and any attempted resume.
- [x] Lost target replies preserve an authoritative status reference and do not create another destructive intent.
- [x] Responses distinguish completed, partially completed, retained, failed and unknown work without leaking customer data.
- [x] Audit records attributable IDs, confirmation/result metadata and phase outcome without secrets or raw exception details.

Merged implementation: `6bf62be1833a2b15b59b51f3727f5c4319dfb171`.

IBM-0137 adds explicit typed portal POST purge-plans, POST purges and GET purges/{jobId} with independent current delete authority. Core metadata reconstruction rejects unsafe/inconsistent fields; the signed client checks the known tenant, operation ID, submitted policy/version and confirmed plan ID before returning. Lost or semantically invalid confirmation replies preserve the original job status Location, with no automatic mutation retry. Preflight is read-only and cannot confirm until a successful explicitly recovered plan; short expiry, native version, scope and atomic one-use consumption remain target-enforced. Current human assignment/user/app and current machine grants/capabilities are rechecked on calls and polling. No support resume/cancel, arbitrary body/route/selector, native provider or worker in the portal. Fixed audit events link plan and job reference IDs to the base app/tenant correlation and distinguish accepted, partial, failed and unknown phase outcomes without bindings, hashes, bodies, keys or provider text.

Validation: 27 new end-to-end tests passed with zero failures/skips against owned loopback Table fixtures and the real signed HTTP target plus standard durable coordinator. They cover exact plan confirmation/replay, stale/substituted/consumed plans, live human/machine revocation, lost and unsafe confirmation/preflight replies, original read-only recovery, app/tenant isolation, partial/failed/unknown progress, explicit route opt-in and safe attributed audit. Three changed modules built, packed and inspected at private 2.17.0-dev.mlo.137.1. Full local legacy/fresh NuGet-only regression remains deferred to IBM-0138 feature completion per the user's cadence; existing required CI is retained. No stable publication or production purge.

PR https://github.com/MedPACTech/IBeam/pull/46, state MERGED, source 40bb40b84e4246fa3b738b4c4eebb0144bf2c36f.
Merged development implementation: 6bf62be1833a2b15b59b51f3727f5c4319dfb171; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-016.

### IBM-0138 / MLO-018: Verify purge completeness, shared-user isolation and consumer recovery guidance

**5 points.** Lane: `done`. Order: 180. Dependencies: IBM-0137.

Add a synthetic app purge hook and fault-injected package-only end-to-end tests. Define consumer inventory and retention coverage, backup/external-system exclusions and operator recovery procedures.

Paths/surfaces: `samples/Management.Bootstrap/`; `IBeam.Tests.Management.TenantData/`; `docs/management/tenant-purge.md (new)`; `scripts/`.

Acceptance criteria:

- [x] A synthetic complete purge removes declared app data/files and tenant Identity records while an overlapping user remains usable in another tenant.
- [x] Every deletion phase is interrupted and resumed without double execution, revived access or false completion.
- [x] Retention, backups and external systems have explicit coverage/exclusion metadata; the API does not promise deletion outside the handler contract.
- [x] Package-only tests reject missing write fences, unsupported cleanup adapters, cross-app proofs and expired confirmations.
- [x] The demo never purges non-synthetic tenants and the implementation card runs no production purge.

Merged implementation: `921021ae678d916cccede51887336e5dff303086`.

IBM-0138 completes the tenant-purge feature with a disabled Development/Testing-only synthetic consumer. The example creates only a new constructor-owned private temporary directory and fresh fake datasets/file bytes; it accepts no existing customer directory or production binding. Its handler and ordinary app access/write path share a durable job-owned fence and exclusive commit lock, persist bounded category checkpoints and reconcile the original job after adapter restart. It deletes declared synthetic dataset/audio bytes while retaining backup/control metadata and explicitly excluding external systems/global accounts. The actual optional native Identity bridge removes all ten tenant-owned families while preserving shared global users, OTP/session artifacts, another tenant's owner/default/access and permanent native fences. It is never enabled automatically in the demo, seeds no purge privilege and performs no production purge.

Consumer inventory and trusted operator recovery are documented: original full job identity/frozen plan/version/fences, authoritative native/app receipt inspection, no reset or replacement intent, explicit same-job target-local reconciliation and truthful partial/retained/unknown outcomes. Portal/frontend guidance lists the real plan/confirmation/status flow, independent grant, exact headers and uncertain response handling. No Qurvia business cleanup adapter is claimed.

Validation: 21 new focused synthetic integration tests passed, zero failures/skips. They interrupt app fence, dataset/file deletion and verification plus lost commit replies in each of the ten native cleanup table families, rebuild the app adapter and recover without duplicate category effects or revived access. Full feature-completion legacy/fresh NuGet-only regression passed 1,621 tests across twelve consumer suites, zero failures/skips/framework ProjectReferences, with 38 dependencies and 23 inspected Management roots at private 2.17.0-dev.mlo.138.1. The isolated portal/two-target package smoke and 76 registry/discovery plus 12 portal ownership/membership source regressions passed. Both consumer examples compile as linked consumer source against the freshly generated packages. New/focused tests run per card and full local legacy tests at feature completion; required CI is retained. No stable publication.

PR https://github.com/MedPACTech/IBeam/pull/47, state MERGED, source c2118841f461ba95fc817ca06321fb1eaf3ec9a1.
Merged development implementation: 921021ae678d916cccede51887336e5dff303086; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-017.
## MLO-F05: Committed membership events and consumer synchronization (18 points)

Complete in development: IBM-0139, IBM-0140 and IBM-0141, 18/18 points (5+8+5). Versioned metadata contracts, atomic native event intent, original attribution, ordered leased delivery and optional durable current-state app projection/bounded trusted reconciliation are implemented. Explicit receiver/storage/app-fence opt-ins and independent support-repair operation requirements preserve native authorization and purge safety. Full feature validation: 1710 fresh NuGet-only checks across 12 consumers, plus 88 focused source checks, all passing. No production adapter or stable release claimed.

Feature dependencies: MLO-F04.

Feature acceptance criteria:

- Events describe committed state and stable operation/version identities across native and management writes.
- Delivery failure cannot lose a committed change or cause a business mutation to replay.
- Consumer synchronization does not borrow roles.assign or expand customer data access.

### IBM-0139 / MLO-019: Define versioned role, membership and ownership lifecycle events

**5 points.** Lane: `done`. Order: 190. Dependencies: IBM-0138.

Add provider-neutral event contracts for role replacement/grant/revoke, membership enable/disable and owner transfer, linked to the same native mutations used by ordinary customer and Management flows. Keep event contents scoped IDs/role IDs/versions and trusted actor metadata.

Paths/surfaces: `IBeam.Identity/Events/`; `IBeam.Identity/Interfaces/`; `IBeam.Identity.Services/Tenants/`; `IBeam.Management.Identity.Target.Services/`.

Acceptance criteria:

- [x] Native and support-origin changes produce the same versioned event shapes with tenant, subject, operation and before/after version identities.
- [x] Events represent committed changes only; rejected/no-op/replayed operations do not invent another logical state change.
- [x] Ownership transfer links both membership changes under one transfer identity without an ownerless intermediate event.
- [x] No credentials, tokens, profiles or consumer business data enter the event contracts.
- [x] Compatibility and event ordering/deduplication rules are documented for existing invite-event consumers.

Merged implementation: `e5284e761db6bb4f9e916ab2d2c5561e6b088757`.

IBM-0139 defines the separate version-1 TenantMembershipChangedEvent and strict shared IdentityTenantMembershipEventFactory for trusted committed native/support output. Typed metadata includes deployment/tenant/user IDs, original event/operation IDs, monotonic tenant sequence, UTC timestamp, logical before/after revision GUIDs, bounded role IDs and original trusted Customer/Support/System/Unknown actor metadata. Grant/revoke/replace, membership link/status changes and combined changes use one shape. An owner transfer is one two-membership envelope; partial/ownerless/unrelated changes fail validation. Rejected/no-op/replayed operations produce no new logical event, and version-only/role-order writes are not logical changes. Arrays are frozen and invalid metadata yields a fixed error. The type does not inherit old invite destination/Metadata fields and grants no authority.

This contract milestone registers only a projector through IServiceOperationExecutor, with no live emitter, native storage/publication callback or durable-delivery claim. The optional committed-reader protocol explicitly requires atomic membership/fence/event persistence, immutable same-operation metadata and bounded scoped reads; native implementation/dispatch is IBM-0140, consumer projection/reconciliation IBM-0141. The guide documents logical revisions versus HTTP ETags, per-tenant ordering, stable IDs/deduplication, transfer atomicity, trusted actor capture and invite compatibility.

Validation: 30 new focused contract/negative tests passed with zero failures/skips. Tests cover native/support shape equivalence, stable original IDs, rejected/no-op/replay suppression, atomic ownership transitions, invalid actor/revision/scope/role metadata, defensive copies, strict JSON and unchanged old invitation contracts. Identity and Identity.Services built, packed and content-inspected at private 2.17.0-dev.mlo.139.1. Full local legacy/fresh NuGet-only regression is deferred to IBM-0141 feature completion under the user's cadence; required repository CI remains mandatory. No stable publication.

PR https://github.com/MedPACTech/IBeam/pull/48, state MERGED, source d21a5e39f41002dfcfb72953baf6550437660100.
Merged development implementation: e5284e761db6bb4f9e916ab2d2c5561e6b088757; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-018.

### IBM-0140 / MLO-020: Persist and dispatch membership events through a durable outbox

**8 points.** Lane: `done`. Order: 200. Dependencies: IBM-0139.

Persist committed mutation and event intent under the same provider transaction/fence where supported, with explicit repair/reconciliation for projections. Add leased dispatch, retries and idempotent consumer acknowledgement; do not claim exactly-once external delivery.

Paths/surfaces: `IBeam.Identity.Services/`; `IBeam.Identity.Repositories.AzureTable/`; `IBeam.Identity/Interfaces/`; `IBeam.Tests.Management.CustomerIdentity/`.

Acceptance criteria:

- [x] A crash after membership commit cannot lose the event; a crash after dispatch may redeliver the same event ID without repeating the mutation.
- [x] Provider contracts bind outbox writes to the same membership revision; unsupported adapters do not claim durable delivery.
- [x] Per-tenant/version ordering and bounded retry/dead-letter behavior support reconciliation after failures.
- [x] Leases and acknowledgements resist duplicate workers; replay preserves original actor/operation attribution.
- [x] Tests cover every commit/dispatch boundary and ensure event failures do not leak private provider messages.

Merged implementation: `b030a1e51ebba6d983e56b04fe523da341c4c497`.

IBM-0140 implements explicit native atomic membership/event intent: membership(s), shared revision/tenant sequence guard, immutable event and operation index join the same tenant transaction and existing purge CAS fences. Atomic owner transfer includes both members, original transfer receipt and one event. Sticky enrollment binds the configured app/environment/realm/tenant and prevents current nonparticipating writers from silently bypassing events. Logical revision markers survive no-op/profile-only writes; rejected/replayed operations create no new logical event. The strict pure metadata protocol is shared by repository preparation and the service projector, with no publication before commit. Native API capture separates customer actors from machines; verified Management membership/ownership callers capture original support org/app/person metadata after live access checks.

Dispatch is disabled by default and requires explicit compatible storage and a durable typed receiver, never a default auth publisher. Exact ETag leases and immutable original metadata give ordered per-tenant at-least-once delivery, bounded receiver timeouts (including uncooperative callbacks), retries/backoff and dead-letter blocking. Trusted exact-version repair retries the same event without changing memberships or actor/sequence/payload. Lost acknowledgements, duplicate workers and expired leases cannot invent a new native change. Bounded metadata scanning is optional. Native purge fences prevent late delivery/acknowledgement recreation; consumer projection app fences remain required. No support HTTP resume/cancel, implicit grant, runtime DDL, raw provider/body audit or production work.

Validation: 68 focused checks passed, zero failures/skips: 28 native Table/startup/dispatch tests, six native actor tests, one real signed support HTTP test and 31 affected contract tests (37 new checks). Cases cover before/after membership commit, lost ownership commit, interrupted link/grant, competing sequence writes, source scope/corruption, no-op/replay/last-owner rejection, current writer disablement, lost receiver/ack replies, duplicate/expired workers, cancellation/timeouts, dead-letter repair and purge cleanup. The signed test verifies support actor attribution, same-intent replay and raw proof rejection. CI exposed an existing same-job purge confirmation race: an initial missing-job read followed by its consumed plan now reloads/validates the original atomic receipt before fresh-plan preconditions. A deterministic stale-read regression and concurrent confirmation case passed; another intent still fails and no new reservation/deletion occurs. Six modules built, packed and inspected at private 2.17.0-dev.mlo.140.2. Full local legacy/fresh NuGet-only regression remains at IBM-0141 feature completion under the user's cadence, with mandatory PR CI retained. No stable publication.

PR https://github.com/MedPACTech/IBeam/pull/49, state MERGED, source 50de5edb95da8e7ad3a7174ea5a1827b80096986.
Merged development implementation: b030a1e51ebba6d983e56b04fe523da341c4c497; exact-head build/test/pack and isolated-management CI passed. No deliveredUtc is set for development-only work.

Dependency keys: MLO-019.

### IBM-0141 / MLO-021: Add consumer projection hooks and bounded reconciliation

**5 points.** Lane: `done`. Order: 210. Dependencies: IBM-0140.

Provide an optional target-local consumer hook and reconciliation port for application member rows. Document replacing Qurvia members/sync routes that currently borrow roles.assign with event-driven synchronization and an independently authorized repair operation when exposed.

Paths/surfaces: `IBeam.Identity/Interfaces/`; `IBeam.Management/ (extension descriptors)`; `samples/Management.Bootstrap/`; `docs/management/`.

Acceptance criteria:

- [x] The synthetic consumer deduplicates event IDs and refuses to apply an older membership version over newer state.
- [x] Reconciliation reads bounded authoritative tenant membership state and repairs only the declared consumer projection.
- [x] Any support-triggered repair has its own registered grant and safe metadata results; roles.assign is insufficient.
- [x] Consumer failure/restart can be retried without changing native membership, roles or ownership.
- [x] Guidance describes the Qurvia adapter boundary without shipping Qurvia business code in IBeam.

Merged implementation: `feef4398018d5bf978abd98498bb60016087074d`.

IBM-0141 implements optional target-local current-native-state application projections: bounded selected IDs, canonical metadata only, per-subject monotonic sequence/revision, exact projection version and durable original-event/repair deduplication. The app store commits the full metadata group, receipt and its own app purge/write guard atomically. Native snapshots and purge fences are rechecked; native membership, roles and ownership are never repaired by this consumer. Ambiguous replies recover the same receipt across restart. The compiled synthetic Table adapter preserves business columns, blocks stale/group-conflicting writes and cannot recreate app rows after purge. Defaults install no receiver, background delivery, support HTTP route or implicit roles.assign repair authority. Any consumer-exposed support repair requires its own fixed registered operation, explicit grant and verified scope/actor; this boundary and the Qurvia-owned adapter migration are documented without Qurvia business code.

Validation: 22 new projection checks, including real-Table scenarios, passed with zero failures/skips. They cover duplicate/reversed delivery, current native reads, selected metadata repair, missing-member tombstones, lost reply/restart, selection/actor conflict, stale version, selection bounds, four scope mismatches, app/native purge fences, snapshot races, per-subject stale-state rejection, atomic two-member conflict and real owner transfer. Full feature-completion regression passed 1710 checks across 12 fresh NuGet-only consumers (zero source framework references), plus 76 focused registry/discovery and 12 portal checks. Fresh private package version 2.17.0-dev.mlo.141.2 was built and validated, including portal/two-target package smoke. This completes the requested legacy cadence at feature completion. No stable release or production work.

PR https://github.com/MedPACTech/IBeam/pull/50, state MERGED, source 724dc9908263f9eb338e57e4df1128910ffc84bc.
Merged development implementation: feef4398018d5bf978abd98498bb60016087074d, exact-head mandatory build/test/pack and isolated-management checks passed.

Dependency keys: MLO-020.
## MLO-F06: Shared target transport, telemetry and bulk invitation revocation (23 points)

Complete in development: IBM-0142–0145, 23/23 points. Shared typed secure sender, safe completion telemetry, independently granted durable native invitation bulk revoke, full fresh NuGet-only regression (1785 checks), optional package-only Swagger/demo composition and frontend/release handoff are implemented and validated. All seven epic features and 29 cards are complete (165 points); stable release publication and production deployment remain separate.

Feature dependencies: MLO-F05.

Feature acceptance criteria:

- Every app operation has correlation, timing, outcome and attributable safe audit without payload or credential logging.
- Shared transport retains exact target/route/authority binding and production HTTPS/DNS protections.
- Bulk revocation reports per-item results and never revokes accepted invitations or creates duplicate destructive work.

### IBM-0142 / MLO-022: Provide one typed signed target sender for registered app operations

**8 points.** Lane: `done`. Order: 220. Dependencies: IBM-0141.

Centralize the existing signed HTTP primitives behind a supported typed IManagementTargetOperationSender. Migrate built-in and synthetic extension clients; provide a migration path for the Portal ManagementTargetSender workaround without accepting arbitrary routes or target origins.

Paths/surfaces: `IBeam.Management/ (transport ports)`; `IBeam.Management.Services/`; `samples/Management.Bootstrap/`; `docs/management/`.

Acceptance criteria:

- [x] A sender resolves only registered operation descriptors and the selected live connection, method/template and scoped resource IDs.
- [x] Production HTTPS, DNS/socket pinning, TLS, deadlines, cancellation, response limits and secret reference handling remain intact.
- [x] Delegated actor, request bytes, resource scope, idempotency and versions are signed once and validated at the target.
- [x] Redirects, arbitrary URLs/methods, undeclared request shapes and response payload leakage are rejected.
- [x] Compatibility tests show equivalent built-in authorization and a working typed synthetic custom route.

Merged implementation: `fa871af2b7566c52067af431788f9913f3fdb971`.

IBM-0142 adds the supported IManagementTargetOperationSender typed port, preserving the legacy IManagementOperationClient contract and default scoped client instance. Fixed registry descriptors and selected live scoped connections remain the only route/origin source; no arbitrary URL/method/header/raw-body proxy is exposed. Native Identity, diagnostics, registered app/commercial/data clients and connection discovery share internal HTTP construction: approved HTTPS origins, revalidation, public-only DNS/socket pinning, normal TLS, bounded connect/attempt deadlines, disabled redirects/proxies/cookies/decompression/trace propagation. Existing typed response limits, strict projection, signed actor/resource/body/version/idempotency binding and same-intent receipt recovery remain in protocol callers. Signing occurs once; fresh capability and independent live human/machine grants are required. Existing custom legacy client registrations/startup remain preserved. Migration documentation replaces bespoke Portal sender/signatures through trusted typed contracts and independently installed target handlers, without editing MPTPortal/Qurvia.

Validation: 14 new checks pass, zero failures/skips: same-instance ports and single proof, real signed fixed synthetic app route, independent human/machine/capability denial, URL/unknown/type/GET-write rejection, redirect/oversize/extra-field rejection with no payload leakage, unknown committed-write original read-only recovery, caller cancellation and real shared-handler socket pin/TLS settings. Two changed modules built, packed and inspected at private 2.17.0-dev.mlo.142.1. Full local legacy/fresh NuGet-only regression is deferred to IBM-0145 feature completion under the user's cadence; mandatory PR CI remains. No stable publication or production work.

PR https://github.com/MedPACTech/IBeam/pull/51, state MERGED, source 67e7b1dfbfbb669f8bec7f17810abf69387ac0f8.
Merged development implementation: fa871af2b7566c52067af431788f9913f3fdb971, exact-head mandatory build/test/pack and isolated-management checks passed.

Dependency keys: MLO-021.

### IBM-0143 / MLO-023: Instrument built-in and app-specific target calls with safe telemetry

**5 points.** Lane: `done`. Order: 230. Dependencies: IBM-0142.

Add common correlation and duration/outcome telemetry around the shared target sender and metadata-only audit on registered operations. Include app-specific failures currently invisible to Portal telemetry.

Paths/surfaces: `IBeam.Management.Services/`; `IBeam.Management.Services/Audit/`; `IBeam.Tests.Management.Diagnostics/`; `docs/management/`.

Acceptance criteria:

- [x] Success, rejection, timeout, network failure, cancellation and unknown mutation outcome produce correlated bounded operation telemetry.
- [x] Telemetry records app/tenant/operation identifiers and safe error classes, never request/response bodies, Authorization headers, keys, OTPs or raw provider errors.
- [x] Existing diagnostics sanitization settings are respected without weakening transport/audit redaction.
- [x] Tracing failures cannot grant authority, retry a destructive request or change the operation result.
- [x] Tests cover the synthetic custom route and prove secret/profile canaries are absent from every emitted record.

Merged implementation: `ce51d8e4afe3c9a67542304242f25a7baa209ae0`.

IBM-0143 adds bounded immutable completion metadata through DiagnosticListener IBeam.Management.TargetOperations and low-cardinality call/duration metrics. Registered typed calls include preflight/type/grant rejection, validated responses, original operation IDs and unknown writes; native Identity/diagnostic transports and discovery share the safe source. Correlation is server-owned validated hex or a fresh GUID, with no ambient Activity baggage. Names are canonical registered/compiled operations or an unregistered sentinel. Only scoped IDs, UTC/duration, fixed outcome/failure classes, optional HTTP status and sent-mutation evidence are emitted: no URLs, filters, header/body objects, keys, assertions, OTPs, profile/clinical fields or raw provider errors. Diagnostic sanitization settings never weaken telemetry redaction. Descriptor-allowlisted audit uses verified delegated/app/tenant/resource scope and no generic snapshots. Legacy constructors and configured durable-audit policy remain unchanged. Diagnostic-source discovery and instrument-publication initialization are guarded against poisoned type initialization. Each tracer/meter failure is isolated and cannot grant authority, change results or repeat writes.

Validation: 19 new focused checks passed with zero failures/skips, including real signed custom route and scoped audit, preflight/unknown-name rejection, deadline/network/caller cancellation, original unknown-write ID/correlation, metric dimensions, native Identity/discovery, both diagnostic modes, malformed correlation/response canaries and throwing listeners/meters. Two changed modules built, packed and inspected at private 2.17.0-dev.mlo.143.2. Full local legacy/fresh NuGet-only regression remains at IBM-0145 feature completion under the user's cadence; mandatory PR CI still gates merge. No stable publication or production work.

PR https://github.com/MedPACTech/IBeam/pull/52, state MERGED, source 912345615d29cd3b4154e1b4fec8b37fcb60dcd9.
Merged development implementation: ce51d8e4afe3c9a67542304242f25a7baa209ae0, exact-head mandatory build/test/pack and isolated-management checks passed.

Dependency keys: MLO-022.

### IBM-0144 / MLO-024: Add bounded version-aware bulk invitation revocation

**5 points.** Lane: `done`. Order: 240. Dependencies: IBM-0143.

Expose a registered independently discoverable bulk-revoke operation with bounded explicit invite IDs or a protected snapshot selection. Reuse native invite transitions, stable per-item intents and safe job/status results rather than an unbounded browser loop.

Paths/surfaces: `IBeam.Management/CustomerIdentity/`; `IBeam.Management.Identity.Target.Services/Invitations/`; `IBeam.Management.Identity.Target.Api/`; `IBeam.Management.Services/`; `IBeam.Management.Api/`.

Acceptance criteria:

- [x] Batch limits, actor/tenant/app scope, selection versions and independent bulk authority are enforced before work starts.
- [x] Each item reports revoked, already revoked, accepted/not eligible, changed version, failed or unknown; partial completion is explicit.
- [x] Retries/resume reuse per-item operation identities and do not revoke accepted invitations, resend mail or create another batch.
- [x] Concurrent acceptance and revoke retain the existing native exact-version guarantees.
- [x] Cancellation, revoked grants, rate limits and provider failures leave safe resumable progress and correlated telemetry.

Merged implementation: `e2d921fe85c2222814f204ca872ba96a26039fd8`.

IBM-0144 adds independently granted management.invites.bulk-revoke, disabled by default. Explicit 1–50 tenant-scoped invite IDs and native versions form one immutable person/org/app/realm/tenant/batch/operation journal. Exact native tenant version gates reservation; SDK CAS checkpoints and canonical per-item original intents preserve bounded same-batch resume. Work-started persists before native conditional transitions. Verified acceptance claims and exact native CAS prevent revoking accepted users; same-op native replay repairs the token index without another primary write. At most ten items/default five seconds run per attempt, with safe metadata-only cancellation cleanup. Read-only receipts never transition native invites or send mail. Current independent human/machine grant, provider/dedicated-realm mapping, target policy, native purge fences and original-person scope remain required. No mail, role/membership/seat, global user or customer business-data changes occur. Expired control records do not create another batch. The dedicated preprovisioned journal stores bounded private ID/version metadata in two binary properties; no runtime DDL or keys/tokens/recipient data. Optional thin portal routes and the shared signed sender validate original batch/child IDs, selected IDs and finite state/reason combinations; contradictory write replies preserve the original uncertain operation. Partial, terminal skips, failed and unknown outcomes remain explicit and correlated by existing safe telemetry/audit.

Validation: 42 new focused checks pass with zero failures/skips: 36 real Table/native/signed-target/portal-route checks plus six real signed portal-client cases. They cover native acceptance races, borrowed grants, altered app/realm/body/version and raw proof replay, before/after native and checkpoint reply loss, concurrent resume, bounded partial/restart, read-only status, grant revocation, rate limits, deadline/caller cancellation, expiry/purge/scope fences, maximum Unicode version journal metadata, opt-in routes/customer-token rejection and contradictory response IDs/selection/reasons. Five changed modules built, packed and inspected at private 2.17.0-dev.mlo.144.1. Full local legacy/fresh NuGet-only regression remains at IBM-0145 feature completion under the user's cadence; mandatory PR CI still gates merge. No stable publication or production work.

PR https://github.com/MedPACTech/IBeam/pull/53, state MERGED, source 5458a014ed6b65545b636310264c0c655c3a483c.
Merged development implementation: e2d921fe85c2222814f204ca872ba96a26039fd8, exact-head mandatory build/test/pack and isolated-management checks passed.

Dependency keys: MLO-023.

### IBM-0145 / MLO-025: Complete package-only regression, demo and frontend migration handoff

**5 points.** Lane: `done`. Order: 250. Dependencies: IBM-0144.

Validate the six features as an optional package graph, update synthetic hooks/demo Swagger/OpenAPI and the baseline application prompt, and document migration from borrowed permissions and custom signing. Prepare release guidance without publishing a stable version or performing production lifecycle work.

Paths/surfaces: `scripts/`; `samples/Management.Bootstrap/`; `docs/management/`; `docs/planning/`; `IBeam.Tests.Management.TenantData/`.

Acceptance criteria:

- [x] A fresh package-only consumer runs ownership, explicit commercial operations, export/purge hooks, event delivery, telemetry and bulk revoke with real scoped authentication.
- [x] Negative tests cover cross-app/realm, shared users, stale versions, missing adapters, assignment revocation and private-data canaries.
- [x] Old diagnostics-only and 2.16 Identity-only consumers remain usable and gain no new destructive/download/commercial rights.
- [x] Bootstrap setup preserves passwords, keys and changed access, uses synthetic data only and exposes an accurate Development API document.
- [x] Frontend handoff lists real operation names/routes, required grants, status/unknown semantics, confirmation flow and data-delivery boundaries.
- [x] Build/pack, meaningful tests and package content inspection pass; release publication remains a separate explicit request.

Merged implementation: `4b9a310370ae18a8d8f2897f7db46bbaf2bbed59`.

IBM-0145 completes the optional package-only lifecycle integration and consumer handoff. The bootstrap reports the loaded IBeam package version, serves Development-only local Swagger UI/OpenAPI with support Bearer metadata and leaves optional routes absent by default. Explicit LifecyclePortal composition installs typed availability/licence/seat/export/purge/bulk portal contracts without target providers/workers, generated keys, new grants or a free-form proxy. Missing required registered signing options fail closed. Existing synthetic native Identity/ownership targets remain distinct from public HTTPS registered-operation targets. Full lifecycle app-owned exporter/purger/projection examples are validated in isolated signed package fixtures, not advertised as production Qurvia adapters. Frontend/release guidance specifies exact routes, independent/composite grants, typed app sender migration, metadata-only telemetry, last-owner safety, event/projection boundaries, stable original-intent recovery, purge confirmation and customer-only export delivery. Repeat setup preserves credentials and changed/revoked access.

Feature-completion validation: 1785 checks pass with zero skips/failures in 12 fresh generated-NuGet consumer suites and zero framework source project references, covering ownership/commercial operations/export/purge/events/projections/shared sender/telemetry/bulk revoke plus legacy Identity/diagnostics, real scoped proofs, provider races, shared users, stale versions, revoked assignments, missing adapters and private-data canaries. Generated package graph/content inspection and isolated portal/two-target smoke passed at private 2.17.0-dev.mlo.145.1; source registry/provider and portal regressions passed (76+12). Package-only three-app demo passes normal support/customer password and MFA, enterprise acceptance, atomic ownership transfer/stale-token protection, realm/app denials and repeat setup preserving revoked access. 45 additional host checks prove local Swagger/loaded version/anonymous login/protected optional routes/typed bulk schema/credential preservation and opt-in route discovery. No stable publication or production lifecycle work.

PR https://github.com/MedPACTech/IBeam/pull/54, state MERGED, source cb298154e445dffd196d8abc94dfa7b86c7020a5.
Merged development implementation: 4b9a310370ae18a8d8f2897f7db46bbaf2bbed59; exact-head mandatory build/test/pack and isolated-management checks passed.

Dependency keys: MLO-024.
