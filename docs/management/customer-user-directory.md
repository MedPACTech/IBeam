# Tenant-scoped customer users and invitations

IBM-0102 adds independent read-only target modules. Portal forwarding is IBM-0103 and invitation writes are IBM-0110. There is no global customer account or email directory and no customer profile-table access.

## Explicit registrations and storage

After target API/delegated actor setup, supply the existing target customer tenants reader for scope checks, plus the explicitly chosen TenantUsers and/or TenantInvites table readers:

- AddIBeamManagementAzureTableCustomerTenantReader(existingTenantsTable)
- AddIBeamManagementAzureTableCustomerUserReader(existingTenantUsersTable)
- AddIBeamManagementAzureTableCustomerInvitationReader(existingTenantInvitesTable)

Use AddIBeamManagementCustomerUserDirectoryApi(configuration) and/or AddIBeamManagementCustomerInvitationDirectoryApi(configuration). The modules can be installed independently. A users-only host needs no invitation store; an invitations-only host needs no membership store. Missing required adapters fail startup rather than advertising a working route. Read-only SAS credentials suffice; no schema, identity indexes, OTP/session tables or global provider are created/replaced.

Configure IBeam:Management:CustomerIdentity:UserDirectory:ProtectionKey as a dedicated random 32-byte base64 server secret. TokenLifetimeMinutes defaults 15 (1-60) and TimeoutSeconds 10 (1-30). Options do not serialize the key. The key is shared by these two directory modules with operation/tenant/source/actor-separated authenticated token contexts; it is independent of the tenant-directory key.

Each module requires its own explicit machine capability and target EnabledOperations setting: management.identity.users.read or management.identity.invites.read. The verified support user must hold the corresponding app assignment. users.read does not imply invites.read or tenants.read. The internal existence check does not disclose tenant names or require broad tenant-directory permission.

## Routes and safe projection

- GET /api/management-identity/tenants/{tenantId}/users[/{userId}]
- GET /api/management-identity/tenants/{tenantId}/invites[/{inviteId}]

Every route uses server-owned operation metadata and the signed actor/machine checks described in customer-identity-delegation.md. Direct service calls without a verified current actor fail before storage access.

User metadata contains tenant/user IDs, membership email, active/disabled membership status, role IDs/names, available created/updated times and version. Email/roles come from the existing tenant membership's denormalized fields, matching the framework's existing directory behavior. This is membership metadata, not global account validity or runtime authorization. No global user/role lookup is performed. Member first/last names are null because the existing membership table does not store them; no consumer profile data is consulted.

Invitation metadata contains tenant/invite IDs, email destination when it is an email invite, optional typed first/last name hints, roles, pending/sent/expired/redeemed/revoked status, available timestamps and version. SMS destinations are never serialized as phone numbers. Pending/sent invitations past ExpiresUtc project as expired without changing the stored row or ETag. Redeemed/revoked states remain explicit.

The readers never select membership phone/disabled-reason fields or invitation TokenHash, redirect URL, access-grants JSON, arbitrary metadata/profile JSON or credentials. The only selected invitation JSON is the bounded typed role-name array. Outbound DTOs never expose hashes, OTPs, sessions, device/IP metadata, redemption tokens/links or raw provider state. Bounded role/text projection suppresses malformed historical rows; redacted ToString methods avoid accidental identity payload logging.

## Bounded queries and paging

Azure reads one exact TEN|tenant-ID partition and the current USR|user-ID or INV|invite-ID row prefix. Point reads bind both route IDs; denormalized IDs must agree with physical keys. Token-hash index partitions are never queried. No other memberships of a multi-tenant user are included.

Each request performs one tenant existence point read and at most one directory provider page, or one directory point read, in addition to bounded live authorization checks. There are zero per-user profile/role/global-user lookups. Page size defaults 50 and must be 1-100. Role counts and text are bounded. This remains buildable for large tenants without materializing the existing unbounded Identity directory/list methods.

Use status active/disabled for members, or pending/sent/expired/redeemed/revoked for invitations. Optional namePrefix (at most 100 characters) matches invariant uppercase email/name metadata within the bounded page. Filters can yield empty pages with continuation; clients must keep going while a token exists. Order follows Azure row-key order, not email/name order. There is no snapshot/indexed-search claim or new index/backfill requirement.

Authenticated encrypted cursors bind operation, tenant, support organization/person, machine credential, app/environment/realm, both tenant and entity physical source hashes, filters/page size and fixed expiry. Cross-user/tenant/type/filter/source replay fails. Responses retain standard data arrays plus top-level pageSize and continuationToken. Invalid queries return generic 400, missing resources 404 and provider failures/timeouts 503. Audit records only trusted IDs, operations, outcomes and correlation, never email/name, roles, query/cursor, private provider errors or invitation payloads.
