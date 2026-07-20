# IBeam.AccessControl.Repositories.AzureTable

`IBeam.AccessControl.Repositories.AzureTable` provides Azure Table Storage implementations for IBeam access-control resource grants, permission-role maps, and service-operation permission rules.

## When To Use This

- You need persistent access-control rules outside in-memory stores.
- You want simple Azure Table persistence for tenant-scoped grants and rules.
- You want permission maps and service-operation rules to survive app restarts.
- You are not using a custom EF/SQL/application-specific access-control store.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Resource grants store | `AzureTableResourceAccessStore` | Persists `IResourceAccessStore` records. |
| Permission maps store | `AzureTablePermissionRoleMapStore` | Persists permission-to-role mappings by name or ID. |
| Service-operation rules store | `AzureTableServiceOperationPermissionStore` | Persists operation allow/deny rules. |
| Options | `AzureTableAccessControlOptions` | Configures connection string, table prefix, table names, and table creation. |
| DI registration | `AddIBeamAccessControlAzureTableStores(IConfiguration)` | Replaces in-memory access-control stores with Azure Table stores. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is a repository layer. It persists access-control data only. It does not evaluate permissions, enforce emergency overrides, or make business decisions.

## Quick Start

```csharp
using IBeam.AccessControl.Repositories.AzureTable;
using IBeam.AccessControl.Services;

builder.Services.AddIBeamAccessControlServices(builder.Configuration);
builder.Services.AddIBeamAccessControlAzureTableStores(builder.Configuration);
```

Register this package after `AddIBeamAccessControlServices` so the Azure Table stores replace the default in-memory stores.

Configuration:

```json
{
  "IBeam": {
    "AccessControl": {
      "AzureTable": {
        "StorageConnectionString": "<connection-string>",
        "TablePrefix": "IBeam",
        "ResourceAccessGrantsTableName": "AccessGrants",
        "PermissionRoleMapsTableName": "PermissionRoleMaps",
        "ServiceOperationPermissionsTableName": "ServiceOperationPermissions",
        "CreateTablesIfNotExists": true
      }
    }
  }
}
```

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `StorageConnectionString` | required | Azure Storage account connection string. |
| `TablePrefix` | empty | Prefix applied to all table names. Example: `IBeam`. |
| `ResourceAccessGrantsTableName` | `AccessGrants` | Logical table name for resource grants. |
| `PermissionRoleMapsTableName` | `PermissionRoleMaps` | Logical table name for permission-role maps. |
| `ServiceOperationPermissionsTableName` | `ServiceOperationPermissions` | Logical table name for service-operation allow/deny rules. |
| `CreateTablesIfNotExists` | `true` | Creates tables automatically on first use. Disable when schema is managed externally. |

With the default `IBeam` prefix, physical tables are:

| Logical Purpose | Physical Table |
|---|---|
| Resource grants | `IBeamAccessGrants` |
| Permission-role maps | `IBeamPermissionRoleMaps` |
| Service-operation rules | `IBeamServiceOperationPermissions` |

## Azure Table Schema

### `IBeamAccessGrants`

Purpose: stores tenant-scoped access grants to resources.

| Field | Purpose |
|---|---|
| `PartitionKey` | `TEN|{tenantId:D}`. Queries list grants by tenant. |
| `RowKey` | `AGR|{grantId:D}`. Looks up one grant inside a tenant partition. |
| `GrantId` | Stable grant identifier. |
| `TenantId` | Tenant that owns the grant. |
| `ResourceType` | Resource category, such as `project` or `product`. |
| `ResourceId` | Resource identifier, including `*` for wildcard grants. |
| `SubjectType` | Subject kind, such as `user`, `api-credential`, or `agent`. |
| `SubjectId` | User ID, credential ID, agent key, or external subject key. |
| `AccessLevel` | Granted level such as `view`, `edit`, `manage`, or `owner`. |
| `Status` | Grant status, typically `active`, `disabled`, or `revoked`. |
| `CreatedUtc` | Creation timestamp. |
| `CreatedByUserId` | Optional administrator/user who created the grant. |
| `UpdatedUtc` | Optional update timestamp. |
| `ExpiresUtc` | Optional expiration timestamp. |
| `MetadataJson` | JSON metadata for application-specific labels/context. |

### `IBeamPermissionRoleMaps`

Purpose: stores which roles grant a permission name or permission ID.

| Field | Purpose |
|---|---|
| `PartitionKey` | `TEN|{tenantId:D}`. Queries mappings by tenant. |
| `RowKey` | `NAM|{normalizedPermissionName}` for name maps or `ID|{permissionId:D}` for ID maps. |
| `TenantId` | Tenant that owns the map. |
| `PermissionName` | Stable permission key such as `pricing.save`. |
| `PermissionId` | Optional stable permission GUID. |
| `RoleNamesCsv` | Role-name grants for compatibility/display/claim scenarios. |
| `RoleIdsCsv` | Role-ID grants; preferred for stable authorization. |
| `Status` | Mapping status, typically `active` or `disabled`. |
| `UpdatedUtc` | Last update timestamp. |

### `IBeamServiceOperationPermissions`

Purpose: stores allow/deny rules for service operation names and patterns.

| Field | Purpose |
|---|---|
| `PartitionKey` | `TEN|{tenantId:D}`. Queries rules by tenant. |
| `RowKey` | `SOP|{ruleId:D}`. Looks up one service-operation rule. |
| `RuleId` | Stable rule identifier. |
| `TenantId` | Tenant that owns the rule. |
| `Pattern` | Operation pattern, such as `pricing.*` or `coupons.delete`. |
| `Effect` | `allow` or `deny`. |
| `SubjectTypesCsv` | Optional subject filters such as `user` or `agent`. |
| `RoleNamesCsv` | Role names affected by the rule. |
| `RoleIdsCsv` | Role IDs affected by the rule. |
| `Priority` | Higher-priority rules win when matching overlaps. |
| `Source` | Rule source, typically `store`. |
| `Status` | Rule status, typically `active` or `disabled`. |
| `CreatedUtc` | Creation timestamp. |
| `UpdatedUtc` | Last update timestamp. |
| `UpdatedByUserId` | Optional user/admin who last changed the rule. |

## Service Operations, Auditing, And Permissions

Repository methods are not service-operation boundaries. The management services that call these stores are tagged in `IBeam.AccessControl.Services`. Keep authorization and audit behavior in the service layer.

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| Resource grant storage | `IResourceAccessStore` | Use another storage backend. |
| Permission map storage | `IPermissionRoleMapStore` | Use another storage backend. |
| Operation rule storage | `IServiceOperationPermissionStore` | Use another storage backend. |
| Table naming | `AzureTableAccessControlOptions` | Match tenant/company naming conventions. |

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Roles, permissions, and grants: [`../docs/roles-permissions-and-grants.md`](../docs/roles-permissions-and-grants.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Startup fails with missing connection string | `StorageConnectionString` was not configured | Set `IBeam:AccessControl:AzureTable:StorageConnectionString` or a supported fallback. |
| Table creation fails | Account permissions or naming invalid | Verify storage credentials and alphanumeric table names. |
| Rules are not found | Wrong tenant partition or operation pattern | Verify `TenantId`, `PartitionKey`, and `Pattern`. |
| Role IDs do not match | CSV role IDs not populated in mappings/rules | Prefer `RoleIdsCsv` for stable authorization. |

## Version Notes

- Targets `net10.0`.
- Uses `Azure.Data.Tables`.
- Package version is assigned by the repository release workflow.
