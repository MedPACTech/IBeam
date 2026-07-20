# Core Access Control Extraction Review

## Why This Exists

IBeam services are intended to be the gatekeeper for application behavior. A service should own the business entity,
coordinate repositories and neighboring services, enforce operation policy, emit audit/logging/error behavior, and
leave repositories focused on persistence.

Authorization should follow that shape. Roles, permission maps, and resource grants are framework-level access-control
concepts; they should not require teams to adopt IBeam Identity authentication.

## What Was Extracted First

The `IBeam.AccessControl` package now contains core permission-map contracts and models:

- `PermissionRoleMapRecord`
- `PermissionRoleMapInfo`
- `PermissionGrantSet`
- `IPermissionRoleMapStore`
- `IPermissionRoleMapService`
- `IPermissionRoleAuthorizer`

The `IBeam.AccessControl.Services` package now provides:

- `InMemoryPermissionRoleMapStore`
- `PermissionRoleMapService`
- `PermissionRoleAuthorizer`

This lets a host with its own authentication issue compatible role claims and evaluate permission maps without using
IBeam Identity login, users, OTP, password auth, or token issuance.

## Bring-Your-Own Auth Contract

A custom auth system can participate by issuing claims:

```csharp
new Claim("tid", tenantId.ToString("D"));
new Claim("sub", userId.ToString("D"));
new Claim("uid", userId.ToString("D"));
new Claim("role", "Administrator");
new Claim("rid", administratorRoleId.ToString("D"));
new Claim("role_id", administratorRoleId.ToString("D"));
```

Then the core authorizer can evaluate:

```csharp
var allowed = await permissionRoleAuthorizer.AuthorizeAsync(
    tenantId,
    principal,
    permissionNames: ["users.manage"],
    permissionIds: [],
    ct);
```

## What Still Lives Under Identity

Identity now consumes AccessControl for permission-map and resource-grant persistence. The remaining Identity-specific access pieces are:

- `IIBeamAccessControlService`
- API credential authentication and credential-specific access context
- Identity API controllers for access catalog, API credentials, and role management

`IIBeamAccessControlService` still lives under Identity because it composes tenant membership claims, Identity permission
catalogs, API credential scope catalogs, access catalog overrides, module definitions, and host rule providers.

## Package Boundary Fixed

`IBeam.AccessControl.Services` no longer depends on `IBeam.Identity`.

The previous `ResourceAccessClaimsEnricher` was Identity-specific because it implemented `IClaimsEnricher`.
That kind of JWT enrichment should live in an Identity integration package, not in the core access-control service
package.

## Breaking Change Direction

Starting with the next internal version, AccessControl is the canonical owner of permission maps, resource grants,
service-operation rules, and their persistence. Identity should consume AccessControl rather than keep parallel models,
stores, and authorization logic.

Because IBeam consumers are currently internal, this cleanup may break previous Identity-owned access-control contracts.
Legacy compatibility should wait until a later public compatibility version.

## Completed Extraction Steps

1. Added a non-Identity Azure Table implementation for core access control:
   - `IBeam.AccessControl.Repositories.AzureTable`
   - `IResourceAccessStore`
   - `IPermissionRoleMapStore`
   - `IServiceOperationPermissionStore`
2. Added standalone AccessControl API endpoints for permission-role map management.

## Next Extraction Steps

1. Move `IIBeamAccessControlService` concepts into `IBeam.AccessControl.Services`, or create a new service equivalent
   that combines permission maps, resource grants, module definitions, and rule providers.
2. Keep Identity-specific pieces as integrations:
   - token claims enrichment
   - API credential authentication
   - user/tenant membership role assignment
3. Replace remaining Identity-owned access-control APIs with thin adapters over AccessControl. Backward-compatible Identity APIs are
   intentionally deferred until a later legacy-support version.

## Core Service Pattern Review

The current base service already supports several gatekeeper behaviors:

- operation flags such as `AllowSave`, `AllowDelete`, `AllowGetAll`
- policy overrides through `ServiceOperationPolicyResolver`
- pre/post operation hooks
- audit transaction and select-rollup support
- repository binding through one `IBaseRepositoryAsync<TEntity>`

Gaps to review:

- Access-control services currently use custom stores directly instead of the generic base service/repository pattern.
- Operation policies are CRUD-oriented; access-control workflows also need semantic operations like `Grant`, `Revoke`,
  `Authorize`, `MapPermission`, and `ResolvePermission`.
- The base service has hooks, but it does not have a first-class authorization hook before repository operations.
- Resource grants and permission maps need repository implementations that can plug into the same logging/error/audit path.
- Claims enrichment should be integration-specific, not part of the core access-control package.

## Proposed Direction

For framework alignment:

1. Model permission maps and resource grants as core entities.
2. Provide repositories for them.
3. Provide services that derive from or mirror `BaseServiceAsync` behavior.
4. Keep authorization decisions in services, not repositories.
5. Let host auth systems supply claims; IBeam should evaluate those claims consistently.
6. Keep Identity as one possible auth integration, not the owner of core access-control concepts.
