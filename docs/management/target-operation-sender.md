# Typed target operation sender

IBM-0142 exposes `IManagementTargetOperationSender` from `IBeam.Management.Operations`.
It inherits the existing `IManagementOperationClient` contract. Default registration
resolves both ports to the same scoped `ManagementRegisteredOperationClient`:
there is one authorization/signing/send pipeline, not an extra proof or retry layer.
Existing clients, constructors and default receipt reconciliation remain compatible.

Register shared DTOs and the fixed descriptor in trusted portal code using
`AddIBeamManagementOperationContract<TRequest, TResult>(descriptor)`, then call
`AddIBeamManagementRegisteredOperationsPortal(configuration)` and inject the sender.
The target separately installs its typed handler and registered-operation API.
Registration/discovery alone never grants authority. Keep the existing explicit
issuer, audience, signing-key, operation-ID key, approved origin, connection secret
reference and target grant configuration from [registered operations](registered-operations.md).

```csharp
var result = await sender.ExecuteAsync<AppCommand, AppResult>(
    applicationId, AppOperations.Command, tenantId, resourceId,
    request, stableIdempotencyKey, exactVersion, cancellationToken);

// After an ambiguous write, recover only its original scoped receipt.
var receipt = await sender.ReadReceiptAsync<AppResult>(
    applicationId, AppOperations.Command, tenantId, resourceId,
    originalOperationId, cancellationToken);
```

Reads use an empty idempotency key and null version. Mutations require a valid
stable key and exact non-wildcard version; reuse the original identity for recovery.
Do not automatically retry unknown writes. Live support assignment, enabled app/user,
explicit grant closure, selected connection and fresh compatible target capability
are checked. The service resolves the credential reference itself. Actor, typed
serialized bytes, fixed method/path, scoped IDs, version and idempotency are bound
into the existing signed request and validated at the target.

The public sender accepts no URL, arbitrary route, method, raw JSON, caller credential,
header dictionary or CLR schema from a request. The immutable registry resolves a
descriptor and requires exact request/result types. Only its GUID route slots vary.
Its strict bounded codec rejects undeclared fields and reconstructs safe typed results.
Target provider/body errors never become public exception text.

Compiled native Identity and diagnostic clients keep their established wire protocols
and public APIs. They, registered licence/seat/export/purge/app clients and connection
discovery now share the same internal HTTP client construction. Production requires
approved HTTPS origins, public-only DNS validation and IP-pinned sockets with normal
TLS hostname/certificate checks. Redirects, proxies, cookies, automatic decompression
and propagated trace headers remain disabled. Protocol-specific response bounds,
deadlines, cancellation, scope checks and result projection remain in their callers.
The internal transport primitive is not a public proxy or authorization bypass.

For default composition, existing portal export/purge services using the legacy port
therefore use the same sender instance. Hosts replacing these ports should explicitly
register their intended implementation for both interfaces; an existing explicit legacy
registration is preserved by `TryAdd`, not silently overwritten.

## Portal workaround migration

Replace an app's bespoke `ManagementTargetSender` and manual signature/header code
with this typed sender. Define shared minimal DTOs, independently registered operation
names and a fixed `/api/management-operations/...` route; install the corresponding
target handler, explicit grants and host policy before enabling it in the portal.
Move or adapt older app-specific routes into that fixed registered boundary rather
than allowing the portal to forward arbitrary paths. Existing borrowed permissions
must not automatically become new operation grants. Keep business work and business
data in the consumer API, with safe metadata results only. No MPTPortal or Qurvia
repository is modified by this framework migration guide.

The synthetic `management.fixture.projection.read` contract in the transport tests
demonstrates independent human/machine/capability grants and a real signed typed
target request. It does not install a running demo capability or represent a Qurvia
business endpoint. Common safe outbound telemetry follows separately in IBM-0143.
