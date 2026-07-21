# IBeam.AccessControl

`IBeam.AccessControl` contains the provider-neutral contracts and models for tenant-scoped access grants, permission-to-role mappings, and service-operation authorization. It is intentionally independent from `IBeam.Identity` so teams can use IBeam access control with their own authentication system.

## When To Use This

- You need to describe access to tenant resources such as projects, products, workspaces, modules, or records.
- You want roles to map to stable permission names or IDs.
- You want service operations like `pricing.save` or `coupons.delete` to be authorized by configurable rules.
- You need user, API credential, and agent subjects to share one access model.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Resource subjects | `AccessSubject`, `AccessSubjectTypes` | Identifies who receives access, such as a user, API credential, or agent. |
| Resource grants | `ResourceAccessGrantRecord`, `GrantResourceAccessRequest`, `CheckResourceAccessRequest` | Models tenant-scoped grants to a resource type/id at an access level. |
| Permission maps | `PermissionRoleMapRecord`, `PermissionGrantSet`, `UpsertPermissionRoleMapRequest` | Maps permission names/IDs to role names and role IDs. |
| Service-operation rules | `ServiceOperationPermissionRule`, `ServiceOperationAuthorizationOptions` | Allows or denies operation-name patterns such as `pricing.*`. |
| Interfaces | `IResourceAccessStore`, `IResourceAccessService`, `IPermissionRoleMapStore`, `IServiceOperationAuthorizer`, and related contracts | Defines storage and service boundaries without choosing an implementation. |
| Options | `AccessControlOptions` | Configures access-level rank comparison and JWT claim emission limits. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is the model/contract layer. Authorization decisions happen in `IBeam.AccessControl.Services`. Optional HTTP endpoints live in `IBeam.AccessControl.Api`. Optional Azure Table persistence lives in `IBeam.AccessControl.Repositories.AzureTable`.

## Quick Start

Most apps should install this package through a service or API package. If you are building your own service layer, consume the contracts directly:

```csharp
public sealed class ProjectAccessPolicy
{
    private readonly IResourceAccessAuthorizer _authorizer;

    public ProjectAccessPolicy(IResourceAccessAuthorizer authorizer)
    {
        _authorizer = authorizer;
    }

    public async Task RequireProjectEditAsync(Guid tenantId, string projectId, string userId, CancellationToken ct)
    {
        var result = await _authorizer.AuthorizeAsync(
            tenantId,
            "project",
            projectId,
            new AccessSubject(AccessSubjectTypes.User, userId),
            ResourceAccessLevels.Edit,
            ct);

        if (!result.Allowed)
            throw new UnauthorizedAccessException(result.Reason);
    }
}
```

## Configuration

`AccessControlOptions` is bound from `IBeam:AccessControl`.

```json
{
  "IBeam": {
    "AccessControl": {
      "EmitResourceAccessClaim": true,
      "MaxResourceAccessClaimsInJwt": 200,
      "AccessLevelRanks": {
        "view": 10,
        "edit": 20,
        "delete": 30,
        "manage": 40,
        "admin": 50,
        "owner": 60,
        "*": 2147483647
      }
    }
  }
}
```

| Setting | Default | Purpose |
|---|---:|---|
| `EmitResourceAccessClaim` | `true` | Allows callers to include compact resource-access claims when building JWTs. |
| `MaxResourceAccessClaimsInJwt` | `200` | Caps how many resource claims should be emitted. |
| `AccessLevelRanks` | built-in ranks | Compares access levels so higher grants imply lower access. |

## Service Operations, Auditing, And Permissions

Service operation authorization uses stable operation names created in service-layer code:

```csharp
[IBeamOperation("purchases.archive")]
public Task ArchivePurchaseAsync(Guid tenantId, Guid purchaseId, CancellationToken ct = default)
    => _operations.ExecuteAsync(
        this,
        token => ArchivePurchaseCoreAsync(tenantId, purchaseId, token),
        new ServiceOperationExecutionOptions
        {
            TenantId = tenantId,
            EntityId = purchaseId
        },
        ct);
```

Rules can then allow or deny names and patterns:

```json
{
  "IBeam": {
    "Services": {
      "Authorization": {
        "Enabled": true,
        "DefaultMode": "require-permission",
        "Rules": [
          {
            "Pattern": "pricing.*",
            "Effect": "allow",
            "RoleNames": [ "Accounting" ]
          },
          {
            "Pattern": "coupons.delete",
            "Effect": "deny",
            "RoleNames": [ "Accounting" ],
            "Priority": 100
          }
        ]
      }
    }
  }
}
```

## Data Storage

This core package does not create tables. It defines store interfaces only.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| Resource grants | No | Use `IBeam.AccessControl.Services` in-memory stores or a repository package. |
| Permission-role maps | No | Implement `IPermissionRoleMapStore` or use Azure Table storage. |
| Service-operation permission rules | No | Implement `IServiceOperationPermissionStore` or use Azure Table storage. |

For Azure Table details, see [`../IBeam.AccessControl.Repositories.AzureTable/README.md`](../IBeam.AccessControl.Repositories.AzureTable/README.md).

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| Resource access storage | `IResourceAccessStore` | Persist grants in Azure Table, EF, SQL, or another store. |
| Resource hierarchy | `IResourceAccessHierarchyResolver` | Let product-level grants imply project-level access. |
| Permission map storage | `IPermissionRoleMapStore` | Store role grants by permission name or ID. |
| Operation rule providers | `IServiceOperationPermissionRuleProvider` | Load rules from config, database, scripts, or another source. |
| Operation authorization | `IServiceOperationAuthorizer` | Plug service operation rules into `IServiceOperationExecutor`. |

## Package Relationships

| Package | Relationship |
|---|---|
| `IBeam.AccessControl` | Core models and interfaces. |
| `IBeam.AccessControl.Services` | Implements access checks, authorization rules, and in-memory stores. |
| `IBeam.AccessControl.Api` | Optional HTTP endpoints for dynamic management. |
| `IBeam.AccessControl.Repositories.AzureTable` | Optional Azure Table persistence. |
| `IBeam.Services` | Provides `[IBeamOperation]` and `IServiceOperationExecutor`. |

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Roles, permissions, and grants: [`../docs/roles-permissions-and-grants.md`](../docs/roles-permissions-and-grants.md)
- Core extraction review: [`../docs/core-access-control-extraction-review.md`](../docs/core-access-control-extraction-review.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

Agents should read the AI prompt before adding permissions, grants, or service operation authorization behavior.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Operation authorization always denies | No matching rule and default mode requires permission | Add a config/store rule or change `DefaultMode` deliberately. |
| Role names work but role IDs do not | Claims or mappings are missing role IDs | Include `rid` claims and populate `RoleIds`. |
| Resource access does not cascade | No hierarchy resolver registered | Implement `IResourceAccessHierarchyResolver`. |
| Using your own auth system feels blocked | Identity is not required | Map your principal/roles/tenant into the access-control abstractions. |

## Version Notes

- Targets `net10.0`.
- Designed to support bring-your-own-auth.
- Package version is assigned by the repository release workflow.
