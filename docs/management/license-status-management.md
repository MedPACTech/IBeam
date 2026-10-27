# Licence status Management adapters

IBM-0127 adds optional `IBeam.Management.Licensing`, native conditional status administration and opt-in portal endpoints. Core `IBeam.Management`, services and API retain no native Licensing/Identity/Billing dependency. Three registered contracts are independent: `management.licenses.read`, `management.licenses.suspend` and `management.licenses.restore`. Each write also explicitly requires the read grant, so the operator can obtain its exact revision. No existing tenant, membership or administrator permission is upgraded.

## Registrations and routes

The target host already supplies native Licensing services and its app-scoped `ILicensingStore`. Explicitly register:

```csharp
services.AddIBeamManagementLicenseStatusTarget(o => {
    o.Enabled = true;
    o.EnableRestore = true;
});
```

Supply a compatible `IManagementOperationAccessService` and a target-owned `ILicenseStatusRestorePolicy`. They must perform live authorization/policy checks, not trust posted context, stale claims or customer roles. The access boundary must bind organization, application, realm, human actor, tenant, resource, signed If-Match and stable operation intent to the current verified request; check machine capabilities, human assignments, active application and prerequisites independently. Protocol 1 is required. A mutation's context operation ID must be stable for the same actor/app/operation/resource/idempotency intent and must come from trusted signed dispatch. The handler cannot create this authority.

Register the separate capability/target dispatch layer and explicitly enable these names under `IBeam:Management:Operations:Target:EnabledOperations`. Also issue appropriate machine capabilities, assign the human and configure the app-scoped delegation ceiling. Registration changes none of those grants. Restore is separately opted in and fails startup without a restore policy. Missing native conditional support or missing live authorization fails an explicitly enabled host at startup; an uninstalled/disabled module has no licence handlers/capabilities and leaves ordinary diagnostics/Identity startup unchanged.

Target descriptor paths are fixed:

| Method | Path | Operation |
| --- | --- | --- |
| GET | `/api/management-operations/tenants/{tenantId}/licenses/{licenseId}` | `management.licenses.read` |
| POST | Same path plus `/suspend` | `management.licenses.suspend` |
| POST | Same path plus `/restore` | `management.licenses.restore` |

`ManagementLicenseTargetService` and the typed registered handlers implement native reads/transitions/receipt lookup. `ManagementLicensePortalService` uses `IManagementOperationClient`, which owns live portal authorization, fixed-route signing/transport and stable unknown-outcome references. IBM-0129 supplies the standard signed extension dispatcher/client and its HTTP discovery integration; until then a trusted host integration can implement these explicit ports. This card does not install an unauthenticated target HTTP proxy or claim the later standard transport is complete.

The portal explicitly registers both `AddIBeamManagementLicensePortal()` (from the optional package) and `AddIBeamManagementPortalLicenseApi(configuration)` (from Management API). The API registration requires the typed service/client at startup and uses the host's named Management authentication scheme. Base API assembly scanning exposes none of these optional controllers.

Portal routes are the same read/suspend/restore suffixes under `/api/management/applications/{applicationId}/tenants/{tenantId}/licenses/{licenseId}`. POST accepts strict empty JSON `{}` plus exact `If-Match` and `Idempotency-Key` headers. The controller constructs the typed contract; rules stay in services. No URL, replacement licence status, plan, provider data, role or membership input is accepted. Receipt lookup is GET on `/operations/{operationId}?operation=management.licenses.suspend` (or restore) under that licence path, requiring the original current authority. Responses are no-store and HTTP body logging is disabled.

The safe status DTO contains tenant ID, licence ID, operational status and revision. A transition result contains those IDs, stable operation ID and `succeeded` or `unknown`. Unknown results have no status/revision and return HTTP 202 with an authorized polling Location. Known transitions return 200. Stale revisions return 412, incompatible intents/state 409, invalid requests 400, denied authority 403 and unavailable adapters 503. Status is not a new entitlement or permission. Existing customer sessions, memberships, roles, tenant status, Billing data, seats, provider fields and licence contents are not modified by a status transition.

## Native conditional provider

`ITenantLicenseService` gains optional default-interface administration members; older implementations report protocol 0 and fail these calls closed while preserving existing methods. The standard service delegates the optional members to `ITenantLicenseStatusAdministrationService`. Native trusted administration has no public customer endpoint; the Management caller authorizes and audits it.

`ILicenseStatusConditionalStore` protocol 1 must atomically compare the exact licence revision, write only the status/private resume state, and insert an immutable receipt bound to tenant, licence, operation ID, authority-scope fingerprint and complete intent hash. Lookup is read-only and must verify scope/transition. Same-intent replay returns the original receipt before considering current state; changed versions/scope/payload under that intent conflict. Every native writer, including legacy update/revoke/delete and provider callbacks, must participate in the same fence. A read must not initialize a guard or mint a new revision.

Production stores must declare durable receipts and retain the fence/receipt across crashes and instances. A typical SQL adapter stores revision/resume metadata on its licence row and commits a separate status-operation receipt in the same transaction. Existing application-specific providers implement this optional contract; this feature does not add an Azure/SQL provider or silently claim durability from an in-memory dictionary.

The shipped `InMemoryLicensingStore` implements this protocol for synthetic tests. Every upsert mints a revision and freezes collection inputs. The first conditional transition atomically enables its write fence; stale/unversioned subsequent upserts and unversioned deletes fail closed. Native version-preserving update/revoke paths remain usable and cannot overwrite a concurrent transition. Reads do not enable the fence. Receipts and private resume state last only for that process. Management registration rejects this provider in Production, even if `AllowVolatileDevelopmentStore=true`. Only explicit Development/Testing plus that flag permits it; never treat its receipts as production evidence.

Suspend requires a currently runtime-eligible licence and retains its original effective status privately. Restore can return only to that original active/trialing/grace/manual status, after checking current native start/expiry/revocation/grace eligibility and the target's restoration policy. It cannot clear revocation, extend expiry, invent entitlements, reactivate members or rewrite commercial state. Native revision comparison prevents a concurrent revocation/update from being overwritten. Hosts must deny restoration when their own compliance/payment/operator policy disallows it, using the safe native conflict exception for a known rejection.

## Recovery, audit and testing

A lost provider reply may follow commit. The target returns unknown scoped metadata and does not retry. Explicit receipt lookup reads only; it cannot change status. A later same-intent POST verifies the original fingerprint and returns the immutable receipt, without reapplying an old suspension after a subsequent restore/revocation. A receipt's historical status/revision is evidence of that transition; read the licence again for current state. A transport-level uncertain exception is accepted by the portal only for its exact app/tenant/licence/operation ID, never another scope.

All Management orchestration uses `IServiceOperationExecutor` and the dedicated metadata-only audit sink. Unknown mutation replies audit as an unsuccessful/uncertain attempt, not a successful transition. Receipt queries have the separate `management.operations.lookup` action; query success does not imply a prior mutation succeeded. Only verified actor/org/app/resource IDs, time, action, result and correlation are retained. No licence entity, provider fields, profiles, bodies, headers, versions, idempotency keys or raw provider exceptions enter audit. Audit availability failures retain the existing fail-closed policy; they do not roll back a native commit.

The focused suite covers independent live grants, scope substitution, exact versions, original-status restoration, expiry/revocation/policy rejection, native writer fencing, simultaneous competing/same-intent requests, lost replies, read-only lookup, disabled/missing adapters, production volatile-store rejection, metadata audit canaries and real portal HTTP behavior. The generated-package regression additionally restores the optional module and native Licensing suites from a fresh NuGet feed; diagnostics/Identity consumers still do not reference it.
