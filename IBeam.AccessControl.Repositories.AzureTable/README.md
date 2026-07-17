# IBeam.AccessControl.Repositories.AzureTable

Optional Azure Table Storage repository for IBeam service operation permission rules.

Register it when permission rules should be persisted outside configuration:

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
        "ServiceOperationPermissionsTableName": "ServiceOperationPermissions",
        "CreateTablesIfNotExists": true
      }
    }
  }
}
```

With the default `IBeam` prefix, the physical table is `IBeamServiceOperationPermissions`.

`CreateTablesIfNotExists` defaults to `true`. Set it to `false` only when the service operation permissions table is created by external schema management.

