# Ownership consumer migration (IBM-0125)

Ownership safety and transfer are development capabilities for the next coordinated release, currently targeted at **2.17.0**. Stable **2.16.1 is the isolated invite compatibility patch** and does not contain IBM-0121–0125. This card validates framework packages and the synthetic bootstrap; it does not implement a frontend, update Qurvia, publish a stable package or deploy production.

## Upgrade and activation order

1. Keep all related Identity, provider and Management packages on one validated train. For development validation, generate the normal local NuGet feed and use its exact version. Existing 2.16 constructors and info DTO constructor remain available; old targets retain their installed operations.
2. Fence or upgrade every native membership writer before enabling owner protection. This includes bootstrap, invitation acceptance, role changes and consumer-written adapters. Older binaries and direct table writers must not retain concurrent write access. Azure Table implements the atomic ownership protocol; unsupported adapters fail startup rather than falling back to sequential role changes.
3. Explicitly configure immutable owner-role names and a SingleOwner or MultipleOwners transfer policy in the customer target. Administrator recovery roles remain a separate policy. See [native configuration](tenant-ownership-transfer.md). Never infer these roles from browser input or deprecated tenant columns.
4. Initialize each existing tenant's owner guard through trusted `IIdentityTenantOwnershipProtection.EnsureAsync(tenantId)` using that tenant's actual immutable definitions. Include the same priming in trusted new-tenant setup after its owner-role definitions exist. Owner lookup does not initialize metadata; a missing guard returns a safe conflict until setup completes. Normalize legacy name-only or inconsistent role records through a fenced trusted migration before transfer. SingleOwner mode rejects an existing multiple-owner tenant until it is explicitly reconciled.
5. Explicitly register the [target and portal ownership modules](customer-ownership-api.md). Preserve normal support MFA, live app assignments, machine/realm bindings and customer JWT/request/refresh enforcement. Do not register customer Identity in the support host.
6. Grant the new operations deliberately at every existing ceiling: target enabled operations, scoped machine credential, support person's app assignment and management administrator's per-app delegation configuration. No existing assignment/key/administrator gains these permissions merely because the catalog recognizes them.

Removing an opt-in setting does not erase persisted owner protection. Changing the protected owner definitions requires a trusted maintenance migration; deleting the guard or widening a role policy is not an upgrade strategy. Global account disable/delete remains outside ownership protocol 1 and its explicit guard rejects it.

## Replace UI-only safety and stale owner badges

| Existing consumer behavior | Supported replacement |
| --- | --- |
| Owner badge reads deprecated `Tenants.OwnerUserId` | GET the scoped ownership endpoint and join its owner IDs to the independently authorized user directory |
| UI alone prevents deleting Owner from a role form | Keep the UI guard, with backend policy-bound replacement and independent last-owner/last-admin protection as authority |
| Owner transfer reuses `roles.assign` | POST the fixed ownership transfer endpoint under its independent `management.identity.owners.transfer` grant |
| A timeout triggers another transfer with a new key | Keep the unknown result/reference, poll read-only, and retry only the original complete intent/key when explicitly needed |

Ordinary role editing is still **full replacement**, not an additive patch. The selected host-owned policy must authorize retained/requested roles **and every removed role**. A Member-only policy cannot silently remove Owner by omitting it, nor can it submit Owner as though it were editable. Such a form must remain disabled for that unsupported full role set, or use an explicitly approved complete policy/workflow. Do not broaden provisioning policy just to bypass the guard. An explicitly broad native role policy remains an intentional host authority; the new transfer endpoint is independently authorized and never follows implicitly from `roles.assign`.

Last owner and last administrator are independent invariants against one fenced snapshot. A remaining Administrator does not replace the last Owner. Transfer preserves unrelated roles and membership status; the destination must already be an enabled non-owner member in the same active tenant. It does not assign seats, change licensing, alter the global account/default tenant, move business records or update a consumer-owned owner projection. Consumer role/ownership events and commercial adapters are separate later epic work.

## Frontend contract

The support session calls the Management API; it never receives target API keys or signs actor proofs. Under `/api/management/applications/{applicationId}`:

- GET `/tenants/{tenantId}/ownership` with `owners.read` returns the exact ownership version and current owner IDs/membership versions. It does not return profiles or role lists.
- Obtain the destination's exact membership version through the separately granted `users.read` directory.
- POST `/tenants/{tenantId}/ownership/transfers` with `owners.transfer`, a new Idempotency-Key, If-Match containing the ownership version, and source/destination IDs plus both membership versions. The strict DTO accepts no roles, status overrides or policy names.
- A succeeded receipt confirms the atomic native transfer; refresh owner/user views. A 202/unknown response is not success or proof of absence. Follow the personal/app-bound Location to poll; polling never transfers ownership or repairs native projections.
- Preserve the complete original body, ownership version and key for an explicit uncertain-result retry. A changed source, destination or version under the same tenant/key conflicts. A corrected request after 412 uses fresh reads and a new intent/key.

Customer tokens are separate from support tokens. After committed role/ownership/membership changes, the affected membership versions invalidate already-issued JWTs and refresh sessions. Reauthentication obtains current authority. Other tenants' memberships and sessions remain usable. New fresh tokens must derive claims from current native roles, never cached frontend selections.

## Synthetic package-only demo

The portable bootstrap project targets stable 2.17.0 from NuGet.org. Local validation can supply a generated package feed and its exact version; the scripts apply that version consistently to both restore and publish. Development prereleases currently use the repository's existing GitHub Packages channel, not public NuGet.org; a successful development publish must not be mistaken for a new stable NuGet release.

From `samples/Management.Bootstrap`, using PowerShell 7:

```powershell
$feed = 'C:/path/to/generated/feed'
$version = '2.17.0-dev.example.1' # The actual generated version, not a floating or invented package.
./.scripts/start-enterprise-demo.ps1 -PackageFeed $feed -PackageVersion $version -Ownership -Instance management-ownership
./.scripts/test-enterprise-demo.ps1 -Instance management-ownership
./.scripts/test-ownership-demo.ps1 -Instance management-ownership
./.scripts/stop-all.ps1 -Instance management-ownership
```

Choose new instance names and unused ports for isolated validation. `-Ownership` is an explicit fresh-instance opt-in; restarting an existing instance does not turn it on, change credentials or restore revoked/edited assignments. Existing seed/setup markers remain authoritative. Native protected seeds use versioned membership administration rather than a legacy destructive writer. A synthetic enabled member gives transfer a real destination, and real support/customer password/MFA is completed through private local outboxes. No test bypass issues a token before MFA.

The ownership smoke checks all three isolated apps, rejects narrow role removal/final-owner disable, transfers and explicitly restores the fixture owner, checks stale customer JWT rejection and fresh MFA authority, and reads authoritative owners. The enterprise smoke also proves repeat trusted setup preserves a revoked assignment. Secrets, OTPs, signing material and customer credentials remain in protected ignored `.local` files. Do not copy those files into source, packages or frontend configuration.
