# Native ownership lookup and transfer (IBM-0123)

This optional native Identity adapter adds authoritative owner lookup and atomic transfer. It does not expose a support HTTP endpoint or grant an operation: the target/portal integration follows in IBM-0124. Continue using [independent owner protection](tenant-ownership-protection.md) for all supported native writers.

## Explicit host policy

Register `services.AddIBeamIdentityTenantOwnershipAdministration(configuration)` in the **customer Identity target**, with its native membership, role and tenant stores and `IServiceOperationExecutor`. The support host does not register customer Identity. This registration is default-off and idempotent. It requires the compatible atomic transfer provider, protocol 1 ownership fencing, and the existing owner protection opt-in. Unsupported providers fail enabled registration rather than falling back to sequential role assignments.

```json
{
  "IBeam": {
    "Identity": {
      "TenantOwnership": { "Enabled": true, "OwnerRoleNames": ["Owner"] },
      "TenantOwnershipTransfer": {
        "Enabled": true,
        "Mode": "SingleOwner",
        "MaximumMembers": 5000
      }
    }
  }
}
```

`Mode` must explicitly be `SingleOwner` or `MultipleOwners`. Single-owner transfer rejects a tenant that already has multiple eligible owners; it does not silently demote them. Multiple-owner transfer moves only the selected source's protected owner roles, leaving other owners unchanged. The destination must be an enabled, existing non-owner member in the same active tenant. Neither mode is an arbitrary owner-grant API. No global account, commercial licence/seat, default-tenant, invitation or other-tenant data is changed. Eligibility means enabled native tenant membership; global account lifecycle remains outside protocol 1.

## Contracts and preconditions

`IIdentityTenantOwnershipAdministrationService.ReadOwnersAsync(tenantId)` returns `IdentityTenantOwnerSnapshot`: tenant ID, exact ownership snapshot version, and bounded owner user IDs with membership versions. It resolves the configured active immutable system-role definitions against current primary memberships, including supported legacy name-only authority. It never consults the deprecated tenant `OwnerUserId` column and never creates/changes the guard. Initialize protection through trusted `IIdentityTenantOwnershipProtection.EnsureAsync` before using this adapter. Reads on an unconfigured tenant fail closed without initializing metadata.

`TransferAsync(IdentityTenantOwnershipTransferRequest)` requires the tenant, source/destination user IDs, both exact membership versions, the exact ownership snapshot version, and a stable nonempty operation ID. Wildcard/missing versions, stale snapshots, disabled/nonexistent/foreign destinations and same-user/already-owner destinations fail. Get a destination's current membership version through the native membership directory; owner lookup exposes owner versions only. Normalize role-name-only memberships through a trusted migration before transfer; lookup may report them, but transfer never guesses how to preserve unrelated legacy authority.

The source loses only its protected owner roles and the destination gains those same roles. All unrelated roles, status, customer columns and protected administrator eligibility are preserved. The service uses the operation executor as a trusted native adapter, with permission/audit delegated to the calling native or Management service, matching native membership administration. Do not expose the provider primitive or this trusted adapter through an unauthenticated route. This card deliberately adds no `roles.assign` bypass, new Management grant or capability advertisement.

## Atomicity and recovery

`IIdentityTenantOwnershipTransferStore` is an additive optional provider interface. The Azure Table adapter commits four rows in one existing `TenantUsers` tenant-partition transaction:

- `!IBEAM|MEMBERSHIP`: advance the shared revision with the exact expected ETag.
- Source `USR|...`: merge only role fields, last operation ID and update time with its exact ETag.
- Destination `USR|...`: merge the same fields with its exact ETag.
- `!IBEAM|OWNERSHIP|{operationId}`: insert an immutable metadata-only transfer receipt containing scoped IDs, intent hash and commit time.

No ownerless intermediate state is visible. Failed preconditions roll the whole batch back. Every supported concurrent native membership writer already advances the same fence. The receipt's intent hash binds the tenant, both users, both original member versions, original ownership version and host mode. Reusing an operation ID with different intent fails. Receipt rows are excluded from membership snapshots and contain no contact/profile data.

The service repairs both user-to-tenant projections only after the primary transaction. Retry the **same operation and original preconditions** after a lost response or projection failure. The durable receipt makes retries acknowledge the original transfer without applying it again, even after later membership changes. Repairs use current primary state, preserve default-tenant preference, and fail rather than inventing a missing secondary membership. Receipts must not be deleted while retries can occur. There is no automatic retention cleanup in this card.

Snapshots accept at most 5,000 memberships, with a configurable lower bound and a 20-second service deadline. Larger providers need an explicitly compatible bounded adapter. Request cancellation propagates; provider details are replaced with safe errors. Store availability failures must not be interpreted as a completed transfer. Existing enabled ownership writers and customer JWT/request/refresh enforcement remain required; raw database edits and older binaries must stay fenced during activation.

All related packages must come from the same validated release train. IBM-0123 produces source and private validation packages only; stable 2.16.1 contains the invite fix, not this new transfer implementation.
