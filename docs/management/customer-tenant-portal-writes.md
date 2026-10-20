# Portal tenant writes and outcome reconciliation (IBM-0106)

The support portal can create and rename tenants in a selected application through the target's explicit Identity management API. Customer identities remain in the target. Portal services use fixed signed HTTPS paths and scoped secrets; they never read customer storage or persist tenant request/response bodies.

## Registration and prerequisites

After the normal support authentication, application/access repositories, live user state, scoped secret store and actor signer:

```csharp
services.AddIBeamManagementActorDelegation(configuration);
services.AddIBeamManagementPortalTenantAdministrationApi(configuration);
```

This explicitly installs tenant POST/PATCH plus operation tracking, including the earlier known-GUID receipt GET. It reuses approved origins/TLS/DNS/IP pinning, named support authentication and the independent live `management.identity.tenants.create` / `management.identity.tenants.update` grants. Read grants and support administrator status confer neither write. Direct service calls enforce the same checks.

The target needs [tenant administration](customer-tenant-administration.md), [durable operations](customer-identity-operations.md), the matching machine capabilities/host policy, and the receipt lookup API from this version. Live `/api/management-identity/info` advertises `operationReceiptProtocolVersion: 1` only when the endpoint module and a usable lookup adapter are installed. Services/storage alone do not advertise it. This is protocol discovery, not a grant. The portal verifies it, app/environment/realm and operation before writing. Consumers should enable write UI only when both the effective operation grant and receipt protocol are available.

Keep `IBeam:Management:PortalCustomerIdentity:ProtectionKey` as the separate base64 32-byte secret already required for portal reads; mutation references use a different authenticated purpose. Share it across portal instances and preserve it for the reference lifetime. `TokenLifetimeMinutes` defaults to 15 (1–60). Mutation phase options under `IBeam:Management:PortalCustomerIdentity:Mutations` are `AttemptTimeoutSeconds=15` (1–20) and `AuthorizationTimeoutSeconds=5` (1–5), with attempt plus three authorization budgets at most 30 seconds. A fresh authorization budget remains after an outbound timeout so the portal can safely return tracking state. The existing response byte/depth limits still apply.

## Requests

Use the support bearer session for portal calls. Machine keys and target actor proofs are server-owned.

```http
POST /api/management/applications/{applicationId}/tenants
Authorization: Bearer <support-session>
Idempotency-Key: <one-random-key-for-this-intent>
Content-Type: application/json

{"name":"Enterprise Customer","externalReference":"CRM:123"}
```

Creation validates/normalizes the same bounded typed fields as the target. Extra profile, role, source, owner, tenant-ID and arbitrary properties are rejected. Keep one idempotency key and the original normalized request until the outcome is known. The portal issues one target write attempt per call, with a fresh signed actor proof; there is no general HTTP write-retry policy. It can safely accept a repeated caller submission with the same key/request because the target reserves one scoped intent/resource.

```http
PATCH /api/management/applications/{applicationId}/tenants/{tenantId}
Authorization: Bearer <support-session>
Idempotency-Key: <one-random-key-for-this-rename>
If-Match: <exact-returned-version>
Content-Type: application/json

{"name":"Renamed Customer"}
```

The exact version is preserved in the signed `If-Match` header and target payload fingerprint. Missing/wildcard versions return 400; stale versions return 412. Idempotency/reference conflicts return 409. Refreshing a version for a deliberately new rename requires a new intent/key. Retry an uncertain original rename with its **original** key, request and expected version so the target can recognize an already committed update before checking a fresh precondition.

## Results and frontend flow

Completed create returns 201; completed rename returns 200. Both use IBeam envelopes:

```json
{
  "success": true,
  "data": {
    "tracking": {
      "reference": "d1_<opaque-reference>",
      "status": "succeeded",
      "operation": { "operationId": "<guid>", "operation": "management.identity.tenants.create", "resourceId": "<tenant-guid>", "step": 1, "status": "succeeded", "createdUtc": "<utc>", "updatedUtc": "<utc>", "expiresUtc": "<utc>" },
      "expiresUtc": "<reference-expiry-utc>"
    },
    "tenant": { "tenantId": "<guid>", "name": "Enterprise Customer", "status": "Active", "version": "<opaque-version>" },
    "source": "support-provisioned",
    "externalReference": "CRM:123"
  }
}
```

Pending/running/uncertain target receipts, lost replies, malformed target responses and outbound deadlines produce 202 tracking. If the result cannot be verified, `tracking.status` is `unknown`, `tracking.operation` is null and `tenant` is null. This does not assert that the customer change failed. A create returns a tracking `Location`; all responses are no-store. Use the returned reference for either command:

```http
GET /api/management/applications/{applicationId}/operations/{opaqueReference}?authority=management.identity.tenants.create
Authorization: Bearer <support-session>
```

For rename, authority is `management.identity.tenants.update`. Poll data is the tracking object itself (reference/status/operation/expiry), without a tenant/profile payload. Succeeded polling returns 200; unresolved or in-progress state returns 202. Once known, `operation.resourceId` identifies the tenant. Load current tenant details only with the independent tenant read grant. Known target GUIDs can also use the existing `/operations/{operationId:guid}?authority=...` route, which returns the safe target receipt.

The UI should keep the intent key/request/version during uncertainty, wait at least two seconds between polls and back off while state is unchanged. It can resubmit the same original command with a fresh support request to resume target lifecycle work. Do not generate a new key merely because a reply was lost. A browser cancellation or portal restart may prevent delivery of any reference; the same-intent resubmission remains the recovery path. Authentication/access/audit outages also do not prove that an earlier customer write failed. Follow the host's request-rate limits and diagnostic logging policy for management paths; opaque references should not be recorded as request payloads.

## Safe lookup and reference lifetime

An unknown reference encrypts the operation, opaque intent key, resource scope/ID and fixed expiry. It contains no tenant name, profile, external reference, version/body, API key or signing credential. The portal stores no payload or tracking table. References bind the current support organization/person, selected app/environment/realm, credential/connection version and operation. Cross-user/app/realm/authority replay, expiry and changed connections fail. Revoked assignments/users/apps are checked before resolving secrets and again before every result. Another support administrator has no bypass.

To resolve a lost reply the portal uses a fixed target `POST /api/management-identity/operations/resolve`, with the original idempotency key in a signed header and a typed body containing operation/resource scope only. This endpoint is read-only with respect to customer data and operation state: it reconstructs the existing reservation ID, performs a scoped point read and never invokes a mutation handler. It still requires a fresh one-use actor proof, current original authority (or appropriate onboarding read authority), machine capability and host policy. It has no API-key listing, arbitrary target URL, receipt-list endpoint or original customer payload input. A missing receipt remains unknown because the original write may still be in flight. Extra target fields and unsafe response/status/scope combinations are discarded or produce tracking uncertainty.

Once the target operation ID is known, polling uses the signed target receipt GET. Polling keeps the reference's original expiry; it does not extend it. After reference expiry or connection rotation, resubmit the unchanged original command/key under current authority to obtain new tracking. Target receipts retain their separate documented lifetime. There is no implicit target lookup or customer write retry under an expired/revoked support assignment.

Metadata-only audit records distinguish create/update/reconciliation and their `succeeded`, `unknown`, `pending`, `running` or `uncertain` outcome actions. Known validated resource IDs are recorded as subjects; unknown outcomes have no invented tenant ID. `Succeeded` on a generic audit transaction means the gateway call completed; the outcome action identifies customer-operation state. App/actor/correlation IDs join portal and target events. Names, external references, raw intent keys, assertions, opaque reference values and request bodies are excluded from audit snapshots.
