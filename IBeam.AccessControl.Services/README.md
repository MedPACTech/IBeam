# IBeam.AccessControl.Services

`IBeam.AccessControl.Services` implements the service-layer access-control behavior for IBeam: resource grant management, resource authorization, permission-to-role mapping, service-operation allow/deny rules, and the bridge into `IServiceOperationExecutor`.

## When To Use This

- You need runtime authorization for service operations such as `pricing.save`.
- You want to grant users, API credentials, or agents access to tenant resources.
- You need configurable role-to-permission mappings.
- You want access-control services without requiring `IBeam.Identity`.
- You want in-memory stores for tests/local hosts, with optional replacement stores for production.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Resource grants | `ResourceAccessService`, `ResourceAccessAuthorizer` | Creates grants and checks resource access by subject/resource/access level. |
| Permission maps | `PermissionRoleMapService`, `PermissionRoleAuthorizer` | Maps permissions to role names/IDs and checks principals against them. |
| Service operations | `ServiceOperationPermissionService`, `ServiceOperationAuthorizer` | Manages and evaluates allow/deny rules for service operation names. |
| Rule providers | `ConfigServiceOperationPermissionRuleProvider`, `StoreServiceOperationPermissionRuleProvider` | Loads operation rules from configuration and optional stores. |
| In-memory stores | `InMemoryResourceAccessStore`, `InMemoryPermissionRoleMapStore`, `InMemoryServiceOperationPermissionStore` | Development/test storage implementations. |
| DI registration | `AddIBeamAccessControlServices`, `AddIBeamServiceOperationAuthorization`, `AddIBeamServiceOperationPermissionManagement` | Registers access-control services and optional dynamic management. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This is the service layer. It owns access-control rules and authorization decisions. API projects should call these services rather than implementing access logic in controllers. Repository packages should persist grants/rules only and not decide whether access is allowed.

## Quick Start

```csharp
using IBeam.AccessControl.Services;

builder.Services.AddIBeamAccessControlServices(builder.Configuration);
```

Enable service-operation authorization:

```json
{
  "IBeam": {
    "Services": {
      "Authorization": {
        "Enabled": true,
        "DefaultMode": "require-permission",
        "Rules": [
          {
            "Pattern": "transactions.*",
            "Effect": "allow",
            "RoleNames": [ "Accounting" ]
          }
        ],
        "EmergencyOverrides": [
          {
            "Pattern": "referralcodes.delete",
            "Effect": "deny",
            "RoleNames": [ "Accounting" ],
            "Priority": 1000
          }
        ]
      }
    }
  }
}
```

Dynamic permission management is optional:

```csharp
builder.Services.AddIBeamServiceOperationPermissionManagement();
```

## Common Usage

Grant resource access:

```csharp
await resourceAccess.GrantAccessAsync(
    tenantId,
    new GrantResourceAccessRequest
    {
        ResourceType = "project",
        ResourceId = "platform",
        Subject = new AccessSubject(AccessSubjectTypes.User, userId.ToString("D")),
        AccessLevel = ResourceAccessLevels.Edit
    },
    createdByUserId,
    ct);
```

Map a permission to a role:

```csharp
await permissionMaps.UpsertByPermissionNameAsync(
    tenantId,
    "pricing.save",
    new UpsertPermissionRoleMapRequest
    {
        RoleNames = [ "Accounting" ]
    },
    ct);
```

Add an operation rule:

```csharp
await operationPermissions.UpsertRuleAsync(
    tenantId,
    new UpsertServiceOperationPermissionRequest
    {
        Pattern = "sales.*",
        Effect = ServiceOperationPermissionEffects.Deny,
        RoleNames = [ "Accounting" ],
        Priority = 100
    },
    updatedByUserId,
    ct);
```

## Service Operations, Auditing, And Permissions

The management services in this package are tagged with `IBeamOperation` and routed through `IServiceOperationExecutor`.

| Service | Operation Examples |
|---|---|
| `ResourceAccessService` | `accesscontrol.resourceaccess.list`, `accesscontrol.resourceaccess.grant`, `accesscontrol.resourceaccess.update`, `accesscontrol.resourceaccess.revoke` |
| `PermissionRoleMapService` | `accesscontrol.permissionroles.list`, `accesscontrol.permissionroles.upsert.name`, `accesscontrol.permissionroles.delete.id` |
| `ServiceOperationPermissionService` | `accesscontrol.serviceoperations.list`, `accesscontrol.serviceoperations.upsert`, `accesscontrol.serviceoperations.disable`, `accesscontrol.serviceoperations.delete` |

Consuming services should use operation names that describe business actions:

```csharp
[IBeamOperation("purchases.archive")]
public Task ArchiveAsync(Guid tenantId, Guid purchaseId, CancellationToken ct = default)
    => _operations.ExecuteAsync(
        this,
        token => ArchiveCoreAsync(tenantId, purchaseId, token),
        new ServiceOperationExecutionOptions
        {
            TenantId = tenantId,
            EntityId = purchaseId
        },
        ct);
```

## Configuration

| Section | Purpose |
|---|---|
| `IBeam:AccessControl` | Resource access-level ranks and claim settings. |
| `IBeam:Services:Authorization` | Service-operation allow/deny rules, emergency overrides, and default mode. |
| `IBeam:Services:Audit` | Service audit behavior from `IBeam.Services`. |

## Data Storage

The default stores are in-memory. They are useful for local development and tests but should be replaced for production.

| Store | Default Implementation | Production Replacement |
|---|---|---|
| `IResourceAccessStore` | `InMemoryResourceAccessStore` | Azure Table, EF, SQL, or app-specific store. |
| `IPermissionRoleMapStore` | `InMemoryPermissionRoleMapStore` | Azure Table, EF, SQL, or app-specific store. |
| `IServiceOperationPermissionStore` | `InMemoryServiceOperationPermissionStore` | Azure Table, EF, SQL, or app-specific store. |

For Azure Table storage and schema, see [`../IBeam.AccessControl.Repositories.AzureTable/README.md`](../IBeam.AccessControl.Repositories.AzureTable/README.md).

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| Resource hierarchy | `IResourceAccessHierarchyResolver` | Allow grants on parent resources to imply child-resource access. |
| Operation rule provider | `IServiceOperationPermissionRuleProvider` | Load rules from config, store, scripts, or a custom source. |
| Principal/role claims | `ClaimsPrincipal` consumed by authorizers | Integrate with IBeam.Identity or bring-your-own-auth. |
| Stores | access-control store interfaces | Persist dynamic grants/rules outside memory. |

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Roles, permissions, and grants: [`../docs/roles-permissions-and-grants.md`](../docs/roles-permissions-and-grants.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Dynamic rules disappear after restart | In-memory store is being used | Register a persistent store after `AddIBeamAccessControlServices`. |
| Emergency deny is not applied | Authorization options not enabled or rule pattern mismatch | Set `Enabled=true` and verify the operation name. |
| API credential behaves like user | Missing subject type claims/rules | Include subject type in claims and rules. |
| Audit does not show access-control management calls | Service auditing disabled | Configure `IBeam:Services:Audit` and ensure the sink is registered. |

## Version Notes

- Targets `net10.0`.
- Uses `IBeam.Services` operation/audit infrastructure.
- Package version is assigned by the repository release workflow.
