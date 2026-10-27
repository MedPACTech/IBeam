# Registered Management operation dispatch

IBM-0129 completes the optional transport for the IBM-0126 registry and the independent licence/seat modules. These changes are on development for the next framework release; installing the published 2.16.1 packages does not supply them.

## Target host

Reference `IBeam.Management.Operations.Target.Api` and the modules the consumer implements. This API package depends on Management Services, with no Identity, Licensing or app-specific dependency. Existing diagnostic and customer Identity routes keep their own protocols and authentication schemes.

```csharp
using IBeam.Management.Operations.Target.Api;
using IBeam.Management.Licensing;

// Register native Licensing and a production store implementing its durable
// conditional status/seat protocols, plus the consumer's eligibility policies.
services.AddIBeamManagementLicenseStatusTarget(o => {
    o.Enabled = true;
    o.EnableRestore = true; // requires an explicit ILicenseStatusRestorePolicy
});
services.AddIBeamManagementSeatsTarget(o => o.Enabled = true);
services.AddIBeamManagementRegisteredOperationsTargetApi(configuration);

// After building the ASP.NET host and its authentication/authorization pipeline:
app.MapIBeamManagementRegisteredOperations();
```

The host supplies `IManagementApiCredentialRepository`, `IManagementAuditRepository`, pinned `IManagementActorValidationKeyProvider` keys, and a shared atomic `IManagementDelegationReplayStore`. A volatile replay store or licence store is unsuitable for production across instances. Native licensing's in-memory provider is available only through explicit Development/Testing fixture opt-ins; the module fails startup without the required compatible persistence and policies. This feature does not add a production licensing persistence provider.

Configure the target deployment's `IBeam:Management:Diagnostics:ApplicationKey` and `Environment`, and `IBeam:Management:Operations:Target` with:

- `RealmId`: the consumer's stable native realm GUID; it must match Identity discovery when both modules are installed.
- `DedicatedApplicationRealm: true`: shared customer identity realms are unsupported by protocol 1.
- `EnabledOperations`: the complete explicit matrix of enabled licence/seat or registered consumer operation names. Empty by default.
- `TrustedPortals`: explicit `OrganizationId`, `ApplicationId`, `Issuer`, `Audience` bindings. These are host configuration, never browser input.

Issue a deployment key with the new `management.operations.info` capability and the exact selected operations, including their read prerequisites. The info permission is a machine discovery permission, not a human assignable operation. Existing diagnostics/Identity keys are unchanged; adding a module does not widen their grants. Optionally restrict `IBeam:Management:Credentials:Network:AllowedNetworks` to trusted proxy/portal CIDRs, using correctly configured trusted forwarded headers before authentication.

The target accepts exactly one persisted `X-IBeam-Management-Key`. Its dedicated named scheme rejects customer Bearer authentication, cookies, and query-string key fallbacks. Each invocation additionally requires a short-lived RS256 `X-IBeam-Management-Actor` proof. The distinct `ibeam-management-operation+jwt` type, pinned issuer/audience/key, organization/app/realm/person/tenant/resource, operation, exact method/path/body, idempotency key and If-Match are verified before dispatch. Mutations claim a fresh assertion nonce atomically; receipt reads do not apply mutations. A machine key alone cannot invoke handlers.

## Portal host

Reference `IBeam.Management.Api` and the matching contracts/modules. Configure existing support Identity with its management-only authentication and MFA, live support-user and application-assignment providers, connection/credential repositories, secret store, and metadata audit. Customer authentication does not grant support access.

```csharp
using IBeam.Management.Api;
using IBeam.Management.Licensing;

services.AddIBeamManagementLicensePortal();
services.AddIBeamManagementSeatsPortal();
services.AddIBeamManagementRegisteredOperationsApi(configuration);
services.AddIBeamManagementPortalLicenseApi(configuration);
services.AddIBeamManagementPortalSeatsApi(configuration);
```

Configure the existing named management API authentication scheme, approved HTTPS connection origins and secret-store references. `IBeam:Management:Operations:Portal` requires `Issuer`, a persistent secret `OperationIdKey` containing 32 random bytes encoded in Base64, `LifetimeSeconds` between 10 and 60 (default 30), and `Targets` binding each `ApplicationId` to its target `Audience`. Supply `IManagementActorSigningKeyProvider` from a managed key store; the target pins the matching public keys. Store the operation ID key and signing material in the host's secret provider. Do not put them in source, cards, browser configuration, or audit. Preserve the operation ID key across restarts/instances; rotating it changes same-intent identities and requires a deliberate recovery policy.

`AddIBeamManagementRegisteredOperationsApi` enables the third connection discovery request and the human endpoint `GET /api/management/applications/{applicationId}/operations`. The response is filtered by current app state, support user, explicit assignment and verified machine capability. It exposes compatible schema metadata only. Disabled, missing or incompatible modules grant nothing. Existing two-probe diagnostic/Identity consumers keep their default behavior unless registered discovery is explicitly enabled.

`IManagementOperationClient` is a server-side typed port. It accepts a registered operation, selected app/tenant/resource GUIDs and its exact contract type; callers cannot supply an origin, route, method, arbitrary JSON schema or headers. It refreshes target metadata before each call, rechecks human state around it, and uses the verified connection's approved HTTPS origin with public DNS validation, pinned sockets, standard TLS and no redirects/proxy/cookies. Both target and portal must register the same descriptor fingerprint and schema version. Protocol 1 supports one tenant GUID and at most one other selected resource GUID in a route; unsupported multiple-resource templates fail target startup. Info and receipt paths are reserved.

For custom modules register `AddIBeamManagementOperationContract<TRequest,TResult>` in the portal and `AddIBeamManagementOperation<TRequest,TResult,THandler>` at the target. Mutating handlers must also implement `IManagementOperationReceiptHandler<TResult>` with a genuinely read-only durable receipt lookup. Descriptor registration is trusted code, never downloaded CLR type information. A handler must enforce its own native eligibility, all-writer version fence, atomic idempotency and receipt rules; the transport does not provide an app-data transaction.

## Consumer permission migration

Deploy the target module and matching portal contracts before enabling a capability. A support administrator with explicit per-app delegation ceilings then grants only the intended independent permissions:

| Existing workaround | Explicit permission |
| --- | --- |
| Licence reads / suspend / restore borrowing `tenants.status` | `management.licenses.read`, `management.licenses.suspend`, `management.licenses.restore` |
| Seat reads / assignment / removal borrowing `memberships.manage` | `management.seats.read`, `management.seats.assign`, `management.seats.unassign` |
| Ownership transfer borrowing `roles.assign` | Existing dedicated `management.identity.owners.read` / `management.identity.owners.transfer` Identity operations |

Write grants require their corresponding read grant. No role, assignment, deployment key or stored connection is automatically upgraded. Reissue/rotate the target key with its explicit capabilities, save the portal connection using its existing PUT connection API, and grant each support user the selected operations. Connection `Test` is read-only; it does not persist newly discovered capabilities. Remove the borrowed grants/routes only after verifying the new routes with a least-privilege support account. Existing 2.16 consumers continue through their compiled Identity/diagnostic endpoints; they do not become generic operation hosts.

## Failures, recovery and evidence

Mutation calls require a valid idempotency key and exact resource If-Match. Same intent derives one stable scoped operation GUID; every HTTP attempt gets a fresh signed nonce. Changed intent under the same operation ID conflicts in the native adapter. Never automatically retry a write after a timeout, redirect, invalid/oversized reply or lost connection: the operation may have committed. The client throws `ManagementOperationUncertainException` with only scoped GUIDs; the typed licence/seat portal services return unknown and a read-only reconciliation location. Polling uses the original independent operation permission at the fixed target receipt route and cannot repeat its write. An absent selected seat assignment is not proof that the customer has no other seat; refresh seat metadata when needed.

The target and portal return no-store responses. Registered actions use the metadata-only audit pipeline; request/response bodies, assertion/key headers, provider text and app records are excluded. This transport supplies operation audit; the broader cross-app transport telemetry and bulk invitation work remain later cards.

`IBeam.Tests.Management.Operations` exercises real in-process HTTP machine authentication, RS256 proofs, native licence/seat adapters, independent live assignments, discovery/fingerprint negotiation, scope/header/body substitution, replay, revocation races and uncertain-write receipt recovery. Feature completion also runs the legacy suites and fresh NuGet-only consumer matrix; new/focused checks run per card.
