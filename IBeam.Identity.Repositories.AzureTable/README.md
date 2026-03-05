# IBeam.Identity.Repositories.AzureTable

`IBeam.Identity.Repositories.AzureTable` provides Azure Table Storage implementations for Identity store contracts.

## What this project does

- Wires ElCamino ASP.NET Identity Azure Table stores.
- Implements custom stores for:
  - `IIdentityUserStore`
  - `ITenantMembershipStore`
  - `ITenantProvisioningService`
  - `IOtpChallengeStore`
  - `IExternalLoginStore`
  - `IAuthSessionStore`
- Ensures schema/tables on startup via hosted service.

## Service registration

In host/API:

```csharp
builder.Services.AddIBeamIdentityAzureTable(builder.Configuration);
```

This binds options, registers stores, and enables automatic table creation at startup.

## Required configuration

Section: `IBeam:Identity:AzureTable`

Storage connection string precedence:

1. `IBeam:Identity:AzureTable:StorageConnectionString`
2. `IBeam:ConnectionString`
3. `ConnectionStrings:IBeam`
4. `ConnectionStrings:IdentityAzureTable`
5. `ConnectionStrings:AzureTables`
6. `ConnectionStrings:AzureTable`
7. `ConnectionStrings:AzureStorage`

- `StorageConnectionString`
- `TablePrefix`
- `IndexTableName`
- `UserTableName`
- `RoleTableName`
- `TenantsTableName`
- `TenantUsersTableName`
- `UserTenantsTableName`
- `OtpChallengesTableName`
- `ExternalLoginsTableName`
- `AuthSessionsTableName`

Example:

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

## Runtime behavior

On startup, schema manager creates missing tables (including custom tables above) and writes schema version row in `{TablePrefix}Schema`.

## Build

```bash
dotnet restore
dotnet build
```
