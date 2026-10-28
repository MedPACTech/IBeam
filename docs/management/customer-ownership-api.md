# Scoped customer ownership APIs (IBM-0124)

The customer target and support portal explicitly opt into ownership management. This builds on [native atomic transfer](tenant-ownership-transfer.md) and [independent last-owner protection](tenant-ownership-protection.md). It adds no customer Identity tables or customer authentication to the support host.

## Registration and deployment

In the customer target, configure native `TenantOwnership` and `TenantOwnershipTransfer` as described in the native guide, keep all native membership writers fenced, and retain compatible live customer request/refresh access enforcement. Explicitly register:

```csharp
services.AddIBeamManagementCustomerOwnershipApi(configuration);
```

This composes the existing delegated machine authentication, operation receipts and optional native ownership adapter. Supply the existing native tenant/membership/role stores, atomic operation/audit/replay storage, issuer verification keys and app/environment/realm bindings. Explicitly enable `management.identity.owners.read` and/or `management.identity.owners.transfer` in the target-owned `CustomerIdentity:EnabledOperations` ceiling. Unsupported atomic providers, disabled native transfer policy or missing live customer enforcement fail startup.

In the support portal host, separately register:

```csharp
services.AddIBeamManagementPortalCustomerOwnershipApi(configuration);
```

Use the normal support Identity login, current application assignments, verified target connection, secret store and delegated signing configuration. The portal retains its existing approved HTTPS origins and network restrictions. No request accepts a target URL, API key, actor ID or consumer policy override.

These capabilities are included in 2.17.0. Stable 2.16.1 contains the invitation compatibility fix and lacks IBM-0123/0124. Upgrade related package versions together and explicitly activate the native adapters, trust and grants. Installing packages and running the synthetic tests do not perform production ownership transfers.

## Routes and independent permissions

| Target route under `/api/management-identity` | Permission | Result |
| --- | --- | --- |
| GET `/tenants/{tenantId}/ownership` | `management.identity.owners.read` | Tenant ID, ownership snapshot version, owner user IDs and their membership versions |
| POST `/tenants/{tenantId}/ownership/transfers` | `management.identity.owners.transfer` | Safe operation receipt, 200 when succeeded or 202 when still processing/uncertain |

Portal mirrors use `/api/management/applications/{applicationId}` instead of `/api/management-identity`. Read and transfer grants are independent. `roles.assign`, `memberships.manage`, `tenants.status` and existing administrator delegation settings never imply ownership transfer. Existing support assignments, machine capabilities and target opt-ins are not automatically expanded. Hosts grant new operations explicitly per app/environment and human delegation ceiling.

Discovery advertises the installed operations only with their usable adapter, permitted machine capability and target ceiling; `ownershipTransferProtocolVersion: 1` identifies this adapter. Missing metadata remains zero/omitted, so older targets remain usable for their installed operations. Native owner-protection protocol 1 alone is not transfer support. The typed client validates the ownership protocol before sending the new routes.

## Exact request and frontend sequence

1. Fetch current ownership using `owners.read`. It is bounded, read-only and never initializes metadata or reads deprecated `Tenants.OwnerUserId`. Trusted native configuration must have initialized ownership protection first.
2. Select an existing enabled non-owner destination. Its current membership version comes from the customer user directory, which requires its separate `users.read` grant.
3. Send a new stable `Idempotency-Key` and `If-Match` containing the exact ownership snapshot version, with this strict body:

```json
{
  "sourceUserId": "<current-owner-guid>",
  "destinationUserId": "<enabled-member-guid>",
  "sourceVersion": "<exact-source-membership-version>",
  "destinationVersion": "<exact-destination-membership-version>"
}
```

The signed actor proof binds the operation, method, full scoped path, serialized body, idempotency key and If-Match. Every required version is therefore covered. Proofs are single-use; an explicitly retried same intent gets a new short-lived proof. Extra fields, role names/IDs, status overrides and wildcard versions are rejected. Source/destination must be distinct memberships in the same active tenant. The target-owned SingleOwner/MultipleOwners policy determines allowed transfer semantics; a request cannot choose it or reactivate the destination.

Receipts and key lookup are scoped to the tenant ownership resource, not a body-selected source user. Changing the source or destination under the same key therefore conflicts rather than reserving a second transfer. The safe receipt ResourceId is the tenant ID.

An authentic stale version returns 412, invalid ownership state/policy returns safe 409, missing membership returns safe 404, and authorization failures return 401/403. Use fresh reads and a new intent/key after correcting a stale or conflicting request. Never substitute new preconditions under an existing idempotency key. Responses exclude profiles, role data, credentials and provider errors; an invalid target response becomes a safe portal 502.

## Unknown outcomes and polling

An ambiguous transport response returns 202 with `tracking.status: unknown` and a Location for the existing portal mutation poll. Follow that Location with the current support session. References remain encrypted, personal and app/connection-bound; each poll rechecks live assignment and connection authority. Polls call only existing receipt GET/resolve operations. They do not call transfer, initialize owners or repair projections. A missing/unknown receipt does not establish that no write occurred, and the portal does not automatically submit another transfer.

If an explicit retry is needed, preserve the original complete body, ownership version and idempotency key. The target journal derives the stable native operation ID; the native atomic receipt reconciles a lost response or unfinished projections without moving ownership twice. A receipt can remain uncertain until the user explicitly retries the same intent after lease expiry. Refresh owner/user directories only after a succeeded receipt.

Metadata-only audit records the source and destination IDs with outcome and existing delegated actor/application/correlation context. The destination receives an audit entry on both successful and failed attempts, plus the known returned receipt status when available. No body, version string, credential, actor assertion, native receipt hash, profile or exception text is copied into Management audit.
