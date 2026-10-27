# Independent tenant owner protection (IBM-0122)

This optional native Identity feature protects an owner role set independently of the administrator recovery group. An enabled Administrator membership does not satisfy the owner invariant. Ownership is derived from tenant-scoped membership role IDs and host-selected active immutable system-role definitions. No client-supplied role name, permission claim or legacy `OwnerUserId` field defines ownership.

## Configuration and registration

Keep related packages on the same release train. This card adds framework source and private validation packages; it does not change the published 2.16.0 packages.

```json
{
  "IBeam": {
    "Identity": {
      "TenantOwnership": {
        "Enabled": true,
        "OwnerRoleNames": ["Owner"]
      }
    },
    "Management": {
      "CustomerIdentity": {
        "Memberships": {
          "AdministratorRoleNames": ["Owner", "Administrator"]
        }
      }
    }
  }
}
```

`Owner` is an example, not a default. The policy accepts 1–8 distinct explicit names when enabled; an absent policy remains off. Names must be nonempty, bounded and free of wildcard/comma/control characters. Disabled policy has an empty name list. Owner definitions must exist in each tenant as active system roles. Ambiguous, missing, mutable or foreign definitions reject that tenant's mutation. A tenant awaiting its first owner's verified invitation acceptance is not magically assigned an owner; ordinary destructive membership commands cannot leave it ownerless. Trusted native bootstrap can establish initial membership authority without bypassing owner protection.

Normal `AddIBeamIdentityServices(configuration)` and Management's `AddIBeamManagementCustomerMembershipApi(configuration)` integrate the option. A host composing native services individually must explicitly register:

```csharp
using IBeam.Identity.Services.Tenants;
services.AddIBeamIdentityTenantOwnershipProtection(configuration);
```

The Azure Table provider derives its `RequireOwnershipProtection` write fence from `IBeam:Identity:TenantOwnership:Enabled`. Manually constructed stores/adapters must opt into the equivalent fence too. Enabling the service with unfenced or incompatible stores fails startup. Repeated registration does not duplicate option arrays. Native and support hosts remain separate; install this policy in the customer Identity target.

Load the ownership configuration before registration. Native customer API hosts must retain their tenant request/refresh enforcement, such as `AddIBeamIdentityTenantAccessEnforcementApi()` after their standard customer JWT registration. The native services alone do not install an HTTP authentication handler; Management membership APIs continue to require an explicit compatible access-enforcement integration. Custom authentication must validate current tenant/membership versions before declaring that integration.

## Invariants and persistence

`IIdentityTenantOwnershipConditionalStore` extends the optional membership persistence protocol; `IIdentityTenantOwnershipRoleStore` supplies bounded exact-name role lookup. Both declare protocol 1 and participating native writers. `IdentityTenantMembershipSnapshot` gains additive owner IDs and protocol metadata while retaining its existing constructor. The native administration service's four-argument constructor remains available for compiled consumers.

The existing `!IBEAM|MEMBERSHIP` tenant-partition revision stores `OwnerRoleIds` and `OwnershipProtocolVersion` alongside the independent `AdministratorRoleIds`. Membership state and revision commit in the same transaction. A service checks the proposed state for at least one enabled member carrying a protected owner role before committing. Two concurrent removals of different owners cannot both succeed from the same revision. Role replacement, membership disable and native role revocation share this rule. Administrator protection remains independently enforced.

Owner policy is sticky: ordinary configuration cannot clear, shrink or replace the installed owner IDs. Removing the opt-in flag does not erase stored protection. A changed definition set requires a trusted maintenance migration that fences all writers; deleting guard metadata is not a supported way to change policy. No policy-reset or owner-bypass HTTP endpoint is provided.

Snapshots are bounded to the existing 5,000-member limit and include only membership IDs, status, roles and versions. Scope errors, missing ownership metadata, incompatible protocol and unavailable providers fail closed. Exact retries and projection repair preserve one primary mutation and its operation identity. Unrelated roles, consumer columns, default-tenant preferences and other tenants remain intact.

## Native writers and legacy boundaries

Native grants and invitation/bootstrap writes advance the same revision. In an owner-protected realm they cannot remove existing role IDs or overwrite an enabled membership as disabled through an uncoordinated write. Names-only legacy authority cannot be erased by granting an unrelated role; normalize it through trusted versioned native administration first. Enabled legacy name-only owners can count against the configured immutable definitions using existing native case-insensitive name semantics; inconsistent nonempty IDs do not gain ownership merely from a matching name.

Low-level legacy membership disable, raw role revocation and uncoordinated role deletion fail closed. Use the native versioned administration service for supported member changes. A native role service constructed without the administration service cannot bypass the store fence. System owner-role definitions cannot be deleted or renamed to another identity. Owner-safe versioned role-definition deletion is not introduced by this card.

`ReplaceStateAsync` and guard configuration are trusted provider primitives, not public authorization APIs. The service owns eligibility and permissions; repositories own conditional persistence. Custom providers, wrappers and writers must implement and preserve protocol 1 before declaring support. Stop or upgrade all older writer instances before activation: an old binary or an external process writing tables directly is outside this protocol and must not retain write access during cutover. Protocol markers declare a verified host integration; they cannot certify an unknown external writer.

## Global accounts and authentication

Eligibility here means an enabled native **tenant membership**, not a promise about a global account's login availability. Native `IdentityUser`/`IIdentityUserStore` do not expose global disable/delete operations. This card does not add them or inspect/mutate global profiles, credentials or sessions.

Protocol 1 does not coordinate a global account change across tenant partitions. `IIdentityTenantOwnershipProtection.RequireGlobalAccountMutationAsync` therefore rejects global disable/delete workflows. A consumer with its own such route must compose this guard and deny that operation in an ownership-protected realm, or provide a separately designed cross-tenant protocol before claiming safety. The absence of this optional service is not permission for a global account mutation. A per-tenant preflight followed by an unguarded global disable is insufficient. Password, email, MFA and temporary login-policy behavior retain their existing native rules; membership ownership is not a replacement for those authentication policies.

## Discovery and support behavior

The target info DTO optionally reports `ownershipProtectionProtocolVersion: 1` when the explicitly configured compatible native protection service and installed membership capabilities are available. Absent/default policy reports zero, omitted from JSON. This describes the native membership protocol; individual tenant commands still resolve and validate that tenant's immutable role definitions. It grants no permission and does not advertise or authorize ownership transfer. Explicit atomic transfer is IBM-0123/IBM-0124 work.

Support commands keep existing human/machine grants, realm binding, signed request identity, exact `If-Match`, idempotency and metadata audit. A rejected last-owner change maps to the existing safe 409 conflict; concurrent/stale writes map to precondition failure and provider uncertainty to unavailable/recoverable status. No role/profile/provider details are included in errors. Customer JWT/refresh enforcement continues to follow native membership versions; rejected owner changes do not revive or revoke another tenant's access.

Provider and signed-HTTP tests cover independent administrator/owner sets, custom owner names, two-owner races, native revocation, bootstrap/legacy fences, sticky policy, source-compatible constructors, invalid/ambiguous definitions, unavailable storage, projection repair, opt-in discovery and global-account mutation denial. The package-only matrix exercises the same changed packages without framework source references.
