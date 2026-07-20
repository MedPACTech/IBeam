# IBeam.AccessControl.Repositories.AzureTable

Optional Azure Table Storage repositories for IBeam AccessControl.

Register it when resource grants, permission maps, and service-operation permission rules should be persisted outside configuration:

```csharp
builder.Services.AddIBeamAccessControlAzureTableStores(builder.Configuration);
```

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

With the default `IBeam` prefix, the physical tables are:

- `IBeamAccessGrants`
- `IBeamPermissionRoleMaps`
- `IBeamServiceOperationPermissions`

`CreateTablesIfNotExists` defaults to `true`. Set it to `false` only when the AccessControl tables are created by external schema management.

The stores registered by `AddIBeamAccessControlAzureTableStores(...)` are:

- `IResourceAccessStore`
- `IPermissionRoleMapStore`
- `IServiceOperationPermissionStore`

