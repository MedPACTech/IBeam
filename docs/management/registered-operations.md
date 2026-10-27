# Registered Management operations

IBM-0126 introduces an immutable host-owned catalog in `IBeam.Management.Operations`. Core still has no Identity, Licensing, Billing, storage, service or ASP.NET dependency. The existing diagnostics and customer Identity operations keep their public names, fixed compiled HTTP routes, DTOs, grants and protocol versions. A new name does not install an endpoint or expand an existing credential, support assignment or administrator ceiling.

App operations use `ManagementOperationDescriptor`: an exact operation name, HTTP method, fixed route template, typed request/result schema IDs and versions, typed handler service, complete required-grant matrix, receipt protocol, audit action and byte limits. The explicit `management.tenants.export` and `management.tenants.delete` constants reserve app-data families; neither is enabled, advertised or implemented by declaring its constant. Business data is not an Identity DTO.

## Trusted registration

Register contracts in host code, never from browser input, configuration type names or a downloaded target manifest:

```csharp
using IBeam.Management.Operations;
using IBeam.Management.Services;

services.AddIBeamManagementOperation<InspectRequest, InspectResult, InspectHandler>(
    new ManagementOperationDescriptor(
        "management.apps.example.inspect", "POST",
        "/api/management-operations/example/tenants/{tenantId:guid}/inspect",
        new(typeof(InspectRequest), "example.inspect.request"),
        new(typeof(InspectResult), "example.inspect.result"),
        typeof(IManagementOperationHandler<InspectRequest, InspectResult>),
        ["management.apps.example.inspect"],
        ReceiptProtocolVersion: 1,
        AuditAction: "management.apps.example.inspect"));
```

The extension method installs the scoped typed handler and registers its descriptor together. Use distinct request/result pairs for distinct handlers; ambiguous handler bindings are rejected. `AddIBeamManagementServices()` supplies the same singleton registry to runtime services. It is frozen on resolution and validated on host start. Do not build multiple containers during registration or replace the registry after resolving services. An invalid registration stops startup; no partial catalog is accepted.

Contracts are public sealed DTO classes/records. Supported fields are strings, booleans, integer/decimal numbers, GUIDs, UTC-capable timestamps, enums, nullable values, nested sealed DTOs and typed arrays. Trees are bounded to four levels, 64 visited contract nodes and 32 properties per object. Objects, arbitrary JSON, dictionaries, recursive/polymorphic contracts, byte buffers, streams, extension-data fields and custom JSON converters are rejected. GET contracts should be empty DTOs; resource GUIDs belong to the trusted operation context. Each operation has distinct version-1 schema IDs. Changing a contract requires a deliberate compatible protocol/schema migration; unsupported versions fail closed.

Extension routes must start with `/api/management-operations/`. Only literal path segments and uniquely named nonempty GUID slots are accepted. Absolute URLs, query strings, percent escapes, wildcard/catch-all paths, duplicate names, overlapping method/routes and built-in Identity/diagnostic shadows are rejected. The catalog is bounded to 128 operations including built-ins. Request bodies are at most 32 KiB and results at most 256 KiB, with smaller per-operation limits. Mutations require receipt protocol 1; GET uses protocol 0. The audit action must exactly equal the registered operation name.

`ManagementOperationPayloadCodec` enforces exact CLR contracts, UTF-8 byte/depth bounds, strict property names, required constructor parameters, nullable annotations, duplicate/unknown property rejection and bounded result storage. It never logs request/result data. `ManagementDelegationBinding.Hash(request, operation, registry)` additionally binds an extension to its registered method, concrete GUID route, body limit, idempotency key and optional If-Match. The original hash overload retains its existing Identity-only behavior. Neither method authorizes or dispatches an operation.

## Independent authorization and metadata

Credential issuance/validation, live human app assignment checks, explicit app-scoped delegation claims and metadata-only audit filtering share this host registry. Registration itself grants nothing. Human assignment changes still require live support administrator state and the per-application delegation ceiling. Every use rechecks current human, app and assignment state; existing compare-and-swap, revocation and composite operation checks remain in place. Support Identity rebuilds configured ceilings from live management roles using the same catalog. Legacy constructor/static overloads remain available and use only the original built-ins.

`AddIBeamManagementOperationCapabilities(configuration)` installs the optional read-only `IManagementOperationCapabilityService`. Set explicit names in `IBeam:Management:Operations:Target:EnabledOperations`; the default is empty. Discovery checks the current machine credential's exact deployment/version, active status, expiry, revocation, complete grant matrix and the actual installed handler. It returns only schema/protocol metadata. An authenticated diagnostics-info or Identity-info credential can ask for metadata, but receives no extension capability unless it also holds all explicit extension grants. A missing handler, disabled operation or missing prerequisite is omitted. A revoked/stale credential is denied on the next call. No handler is invoked by discovery.

This service is separate from `ManagementCustomerIdentityInfo`. Identity metadata continues to reject non-Identity capabilities, preserving diagnostics-only, Identity-only and combined 2.16 targets. The optional registry/capability service does not install an app-data HTTP route or turn a machine credential into a human grant. The later extension-dispatch/discovery card wires this metadata through target/portal HTTP, verified connections and typed dispatch; until then extension calls through the existing Identity transport fail closed. Licensing, export and purge each require their own adapter, durable mutation rules, endpoint and explicit grants in subsequent cards.

Audit persists the existing bounded metadata projection only. A registered operation's exact action is allowed; caller-supplied action suffixes, bodies, snapshots, headers, secrets and provider errors are excluded. Framework orchestration must run through `IServiceOperationExecutor`; calling an app handler directly is not an authorized management pipeline. A descriptor's receipt version is a contract requirement, not proof that an app has implemented durable idempotency.

## Validation

`RegisteredOperationTests` exercises malformed/duplicate registrations, route and schema/version substitution, immutable grant closure, missing adapters, live credential and human revocation, explicit ceilings, strict payloads and audit canaries. `RegisteredOperationDelegationTests` checks the optional support Identity adapter. Existing discovery tests preserve the three legacy target combinations. Run the package-only Management regression to verify that these contracts work without source project references.
