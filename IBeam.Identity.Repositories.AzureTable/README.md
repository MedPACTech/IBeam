# IBeam.Identity.Repositories.AzureTable

Azure Table Storage implementation for IBeam Identity store contracts.

## Startup Registration

```csharp
builder.Services.AddIBeamIdentityAzureTable(builder.Configuration);
```

This binds options, validates configuration, registers stores, and enables schema/table initialization.

## Configuration

Section: `IBeam:Identity:AzureTable`

```json
{
  "IBeam": {
    "Identity": {
      "AzureTable": {
        "StorageConnectionString": "UseDevelopmentStorage=true",
        "TablePrefix": "",
        "IndexTableName": "AspNetIndex",
        "UserTableName": "AspNetUsers",
        "RoleTableName": "AspNetRoles",
        "TenantsTableName": "Tenants",
        "TenantUsersTableName": "TenantUsers",
        "UserTenantsTableName": "UserTenants",
        "OtpChallengesTableName": "OtpChallenges",
        "ExternalLoginsTableName": "ExternalLogins",
        "AuthSessionsTableName": "AuthSessions"
      }
    }
  }
}
```

## Connection String Resolution

`AddIBeamIdentityAzureTable(configuration)` resolves storage connection string in this order:

1. `IBeam:Identity:AzureTable:StorageConnectionString`
2. `IBeam:AzureTables`
3. `IBeam:ConnectionString`
4. `ConnectionStrings:AzureTables`
5. `ConnectionStrings:AzureTable`
6. `ConnectionStrings:AzureStorage`
7. `ConnectionStrings:IBeam`
8. `ConnectionStrings:DefaultConnection`
9. `ConnectionStrings:IdentityAzureTable`

## Registered Store Contracts

- `IIdentityUserStore`
- `ITenantMembershipStore`
- `ITenantProvisioningService`
- `IOtpChallengeStore`
- `IExternalLoginStore`
- `IAuthSessionStore`

## Runtime Behavior

At startup, hosted schema initialization ensures missing tables exist and schema version metadata is maintained in the schema table (`{TablePrefix}Schema`).
