# Portal customer directory reads (IBM-0103)

The support host calls a selected application's opt-in customer Identity target over HTTPS. It does not register the target's customer Identity services or read its storage. Support identity remains in its existing isolated realm. These routes expose framework tenant, membership and invitation metadata only.

## Registration

After the normal support authentication, application/access repositories, scoped secret store and audit registrations, call:

```csharp
services.AddIBeamManagementActorDelegation(configuration);
services.AddIBeamManagementPortalCustomerDirectoriesApi(configuration);
```

Register `IManagementActorSigningKeyProvider` with a dedicated RSA signing key and configure explicit issuer/audience/application bindings as described in [delegated actors](customer-identity-delegation.md). The target must trust the corresponding public key, opt into the directory modules and enable each read operation. Startup fails if required service/signing dependencies are missing. Discovery alone does not expose these routes.

`IBeam:Management:PortalCustomerIdentity` requires a separate secret `ProtectionKey` (base64 encoding of 32 random bytes). Keep it in a secret store or environment variable. Defaults: `TokenLifetimeMinutes=15` (1–60), `TimeoutSeconds=20` (1–30), `MaximumResponseBytes=1048576` (32768–2097152). Do not share this key with target cursors or diagnostic tokens.

## Routes

All paths below begin with `/api/management/applications/{applicationId}` and use the host's named support authentication scheme. Administrators need the same live assignment as every other support person.

| GET path | Required independent operation |
| --- | --- |
| `/tenants` | `management.identity.tenants.read` |
| `/tenants/{tenantId}` | `management.identity.tenants.read` |
| `/tenants/{tenantId}/users` | `management.identity.users.read` |
| `/tenants/{tenantId}/users/{userId}` | `management.identity.users.read` |
| `/tenants/{tenantId}/invites` | `management.identity.invites.read` |
| `/tenants/{tenantId}/invites/{inviteId}` | `management.identity.invites.read` |

Lists accept `pageSize` (default 50, maximum 100), opaque `continuationToken`, optional `status` and `namePrefix` (maximum 100 characters). Tenant statuses are `Active`/`Disabled`; membership statuses `active`/`disabled`; invitation statuses `pending`/`sent`/`expired`/`redeemed`/`revoked`. Filtering is bounded as documented by the target modules; an empty page can still contain a continuation. Reuse the same filters and page size for subsequent pages. The response has the normal IBeam `success`, `data` array and top-level `pageSize`/`continuationToken` fields. Detail uses a `data` object. Responses are `Cache-Control: no-store`.

Portal continuations encrypt the target token and bind it to organization, support person, application/environment, realm, current connection/version, operation, tenant, filters and page size. Their original expiry is retained across pages. Target cursors over 7000 characters are rejected as invalid target responses; portal cursors are bounded by the existing authenticated token codec. A revoked assignment, disabled support user/app or changed connection is rechecked around every read. Clients must restart pagination after any such change.

## Layers and transport

`ManagementPortalCustomerDirectoriesController` calls one typed `IManagementPortalCustomerDirectory<T>` method. `ManagementPortalCustomerDirectory<T>` owns query validation, live authorization, encrypted pagination, deadlines and metadata-only audit. `ManagementCustomerIdentityConnectionResolver` checks current app/assignment/capabilities before reading a scoped secret. `ManagementCustomerIdentityClient` sends fixed GET paths, verifies live target info and signs the exact encoded path/query with the named support actor. These transport-neutral ports and DTOs live in `IBeam.Management`; the support host has no customer Identity dependency.

Transport reuses approved HTTPS origins, public DNS checks, IP-pinned sockets and normal TLS. Redirects, proxies, cookies and distributed trace propagation are disabled. Browser authentication headers are never forwarded. There is no arbitrary URL, raw proxy or storage-connection input. Responses have a byte/depth/page limit and are reconstructed from typed allowlisted fields. Target profiles, credentials, token hashes, invitation links and arbitrary extra JSON are discarded. Returned directory payloads, filters, cursors and actor assertions are never placed in portal persistence or service audit snapshots. Audit records contain only trusted scope/actor identifiers, action, time and result.

Safe failures: 400 invalid query/cursor, 403 denied/stale access, 404 missing tenant/user/invitation, 429 target throttling, 502 invalid target response (including redirects), 503 unavailable target/deadline/provider. Provider response bodies and headers are not forwarded. Caller cancellation is preserved.

The target contracts are described in [tenant reads](customer-tenant-directory.md) and [tenant user/invitation reads](customer-user-directory.md). Invitation writes are delivered separately; including invitation reads here completes the directory feature without granting invitation-management authority.

## Portal tenant writes

IBM-0106 adds optional tenant create/rename and opaque operation tracking with independent write grants, signed idempotency/version headers, 202 unknown outcomes and scoped read-only receipt lookup. See [tenant portal writes and frontend flow](customer-tenant-portal-writes.md).

See [enterprise onboarding services](enterprise-onboarding.md) for IBM-0107 target orchestration, recovery storage, policy configuration and customer acceptance. HTTP/portal onboarding routes follow in IBM-0108.

IBM-0108 [enterprise onboarding API](enterprise-onboarding-api.md) adds explicit target/portal create and retry routes and authoritative business status on shared operation polls.
