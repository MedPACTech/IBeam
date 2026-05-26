# IBeam.Identity

`IBeam.Identity` is the contract package for the IBeam identity domain.

## Narrative Introduction

This package provides the shared language for identity workflows across API, services, and repository implementations. It contains interfaces, request/response models, options, and event contracts so higher-level packages can evolve independently behind stable abstractions.

## Identity Architecture (Simple View)

IBeam identity is intentionally layered:

1. API Layer (`IBeam.Identity.Api`)
- HTTP endpoints, auth middleware, request validation, response mapping.
- Controllers include OTP/password/OAuth/token/session/tenant-role APIs.

2. Contract Layer (`IBeam.Identity`)
- Interfaces, models, options, events, authorization attributes.
- No storage or provider-specific logic.

3. Service Layer (`IBeam.Identity.Services`)
- Core auth orchestration: OTP, password, OAuth, token issuing, tenant selection.
- Uses only contracts (`IIdentityUserStore`, `IOtpChallengeStore`, etc.).

4. Repository Layer (provider implementations)
- Azure Table provider (`IBeam.Identity.Repositories.AzureTable`) currently ships complete implementations.
- Entity Framework provider (`IBeam.Identity.Repositories.EntityFramework`) exists for EF-based identity paths.

5. Communications Layer
- Email/SMS abstractions and providers used by OTP/registration flows.

## Features and Components

- auth service contracts:
  - `IIdentityAuthService`
  - `IIdentityOtpAuthService`
  - `IIdentityOAuthAuthService`
  - `ITokenService`
- store contracts:
  - `IIdentityUserStore`, `IOtpChallengeStore`, `IExternalLoginStore`
  - `ITenantMembershipStore`, `ITenantProvisioningService`, `IAuthSessionStore`
  - `ITenantRoleStore` for tenant-scoped role CRUD and assignment
  - `IPermissionAccessStore` for tenant permission-to-role mappings
- service contracts:
  - `ITenantRoleService`
  - `IRoleAccessAuthorizer`
  - `IPermissionAccessAuthorizer`
  - `IPermissionCatalogProvider`
- options models (`JwtOptions`, `OtpOptions`, `OAuthOptions`, `FeatureOptions`, etc.)
- lifecycle event contracts and default no-op implementations
- role access attributes (service-safe, no MVC dependency):
  - `[RoleAccess("owner", "billing")]`
  - `[RoleAccessId("3f7a4b4f-8fc5-49bb-b6fe-1f4a9b43a3e9")]`
  - `[AllowAllRoleAccess]`
- dynamic permission attributes:
  - `[PermissionAccess("SavePatient")]`
  - `[PermissionAccessId("6c76f166-b130-4c80-bf7e-99d38ea1a75f")]`

## Models vs Entities

1. Models
- Defined in `IBeam.Identity.Models`.
- Used by API and services (requests/responses/domain contracts).
- Examples: `AuthResultResponse`, `RegisterUserRequest`, `TenantInfo`.

2. Entities
- Provider-specific persistence shapes (e.g., Azure Table entities).
- Include storage keys (`PartitionKey`, `RowKey`) and persistence metadata.
- Examples in Azure Table provider: `TenantEntity`, `UserTenantEntity`, `OtpChallengeEntity`.

## Azure Table Schema (Current Provider)

### ElCamino identity tables
- `AspNetUsers`: base user identities.
- `AspNetRoles`: role definitions.
- `AspNetIndex`: identity lookup/index support.

### IBeam custom identity tables
- `Tenants`: tenant master records.
- `TenantUsers`: tenant-to-user membership index.
- `UserTenants`: user-to-tenant membership index.
- `TenantRoles`: tenant-scoped roles.
- `PermissionRoleMaps`: tenant permission mapping to role names/ids.
- `OtpChallenges`: OTP lifecycle records (destination, hash, attempts, expiry, consume state).
- `ExternalLogins`: OAuth provider-user links.
- `AuthSessions`: refresh/session tracking and revocation.
- `Schema`: schema version marker for bootstrap.

## Table Naming and Prefixing

For Azure Table identity provider, physical table name is:

`{TablePrefix}{BaseTableName}`

Examples:
- `TablePrefix = "IBeam"` + `AspNetUsers` => `IBeamAspNetUsers`
- `TablePrefix = "Acme"` + `TenantUsers` => `AcmeTenantUsers`

This applies to both ElCamino and custom IBeam identity tables.

## Connection String Resolution Cascade

### Current implemented behavior

Azure Table providers currently resolve connection strings with fallback precedence.

1. Identity AzureTable provider (`IBeam.Identity.Repositories.AzureTable`)
- 1) `IBeam:Identity:AzureTable:StorageConnectionString`
- 2) `IBeam:AzureTables`
- 3) `IBeam:Repositories:ConnectionString`
- 4) `IBeam:ConnectionString`
- 5) `ConnectionStrings:AzureTables`
- 6) `ConnectionStrings:AzureStorage`
- 7) `ConnectionStrings:IBeam`
- 8) `ConnectionStrings:DefaultConnection`
- 9) `ConnectionStrings:IdentityAzureTable`

2. Generic AzureTables repository provider (`IBeam.Repositories.AzureTables`)
- 1) `IBeam:Repositories:AzureTables:ConnectionString`
- 2) `IBeam:AzureTables`
- 3) `IBeam:Repositories:ConnectionString`
- 4) `IBeam:ConnectionString`
- 5) `ConnectionStrings:AzureTables`
- 6) `ConnectionStrings:AzureStorage`
- 7) `ConnectionStrings:IBeam`
- 8) `ConnectionStrings:DefaultConnection`

3. Identity EntityFramework provider (`IBeam.Identity.Repositories.EntityFramework`)
- 1) `{configSectionPath}:ConnectionString` (default `IdentityEf`)
- 2) `IBeam:Identity:EntityFramework:ConnectionString`
- 3) `IBeam:Repositories:EntityFramework:ConnectionString`
- 4) `IBeam:Repositories:ConnectionString`
- 5) `IBeam:ConnectionString`
- 6) `ConnectionStrings:IdentityEf`
- 7) `ConnectionStrings:IdentityEntityFramework`
- 8) `ConnectionStrings:IBeam`
- 9) `ConnectionStrings:DefaultConnection`

## Configuration Models Exposed

- `IBeam:Identity:Jwt`
- `IBeam:Identity:Otp`
- `IBeam:Identity:OAuth`
- `IBeam:Identity:Features`
- `IBeam:Identity:Events`
- `IBeam:Identity:EmailTemplates`
- `IBeam:Identity:PermissionAccess`
- `IBeam:Identity:RoleManagement`

## Examples

### 1) API composition in a host app

```csharp
builder.Services.AddIBeamIdentityApi(builder.Configuration);
builder.Services.AddIBeamIdentityApiControllers();
```

### 2) Azure Table identity configuration with prefix and scoped connection

```json
{
  "IBeam": {
    "Identity": {
      "AzureTable": {
        "StorageConnectionString": "UseDevelopmentStorage=true",
        "TablePrefix": "Acme",
        "UserTableName": "AspNetUsers",
        "RoleTableName": "AspNetRoles",
        "IndexTableName": "AspNetIndex"
      }
    }
  }
}
```

### 3) Fallback-only configuration (top-level IBeam connection)

```json
{
  "IBeam": {
    "ConnectionString": "UseDevelopmentStorage=true"
  }
}
```

With AzureTable providers, this can be used when deeper scoped keys are not supplied.

### 4) Service role access example

```csharp
[RoleAccess("SavePatient")]
public sealed class PatientService
{
    private readonly IRoleAccessAuthorizer _roleAccess;

    public PatientService(IRoleAccessAuthorizer roleAccess)
    {
        _roleAccess = roleAccess;
    }

    public Task SavePatientAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        _roleAccess.EnsureAuthorizedForCurrentMethod(user, this);
        return Task.CompletedTask;
    }
}
```
