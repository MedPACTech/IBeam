# IBeam.Identity.Repositories.AzureTable

Azure Table Storage provider for IBeam identity store contracts.

## Narrative Introduction

This package connects identity orchestration to Azure Table persistence. It wires ASP.NET Core Identity + ElCamino Azure Table stores, registers IBeam store abstractions, and includes schema initialization so hosts can start with minimal persistence setup.

## Features and Components

- DI extension:
  - `AddIBeamIdentityAzureTable(IConfiguration)`
- Azure Table option binding and validation (`AzureTableIdentityOptions`)
- Identity store registrations for:
  - users
  - auth identifier lookup bindings
  - tenants and memberships
  - tenant roles and user-role assignments
  - permission role-mapping store (`IPermissionAccessStore`)
  - OTP challenges
  - external logins
  - auth sessions
- schema management services:
  - `IIdentitySchemaManager`
  - hosted schema bootstrap

## Dependencies

- Internal packages:
  - `IBeam.Identity.Services`
- External packages:
  - `ElCamino.AspNetCore.Identity.AzureTable`
  - `System.IdentityModel.Tokens.Jwt`
  - `Microsoft.AspNetCore.App` framework reference

## Configuration

Primary section:
- `IBeam:Identity:AzureTable`

Includes connection-string fallback resolution across `IBeam:*` and `ConnectionStrings:*` keys.

## Auth Identifier Index

The Azure Table provider keeps the canonical user in ElCamino's `AspNetUsers` table, but auth lookup is routed through a provider-owned `AuthIdentifiers` table.

Key shape:

```text
PartitionKey = AUTH|EMAIL|ADAM@TEST.COM
RowKey       = USER
UserId       = {guid}

PartitionKey = AUTH|SMS|+16145551212
RowKey       = USER
UserId       = {same-guid}
```

Why this matters:

- Email OTP, SMS OTP, and email/password can all resolve to the same `UserId`.
- Adding or changing email/SMS does not require moving the user row.
- Authorization remains fast because tenant membership is still loaded by `USR|{userId}`.
- SMS auth no longer needs to scan the user table by `PhoneNumber`.
- SMS identifiers are normalized before lookup or binding. For default US phone handling,
  `6142649686`, `16142649686`, `+16142649686`, and `(614) 264-9686` all bind to
  the same E.164 identifier, `+16142649686`.

The schema bootstrap creates `AuthIdentifiers` with the other custom identity tables. New create/update flows maintain bindings automatically.

## Tenant Membership + Role Bootstrap

Use `ITenantRoleService.EnsureTenantMembershipAndGrantRolesAsync(...)` when an app needs to bootstrap a tenant membership and assign roles in one call.

The Azure Table provider will:

- ensure the tenant row exists
- ensure default tenant roles exist
- ensure requested role names exist
- ensure `TenantUsers` membership exists
- ensure `UserTenants` reverse membership exists
- grant requested role IDs and role names

`GrantRolesAsync(...)` remains strict and still requires an existing tenant membership.

Code example:

```csharp
await tenantRoleService.EnsureTenantMembershipAndGrantRolesAsync(
    new TenantMembershipRoleBootstrapRequest(
        TenantId: configuredTenantId,
        UserId: userId,
        TenantName: "Wellderly",
        RoleNames: new[] { "Member" },
        SetAsDefault: true),
    ct);
```

The provider also implements `ITenantProvisioningService.EnsureUserTenantMembershipAsync(...)`. IBeam auth services use that method when `IBeam:Identity:TenantProvisioning:Mode` is `UseDefaultTenant` and `AutoLinkUserToDefaultTenant` is enabled.

For Azure Table storage, that path ensures:

- `Tenants` has the configured tenant row.
- `TenantUsers` has the tenant-to-user membership row.
- `UserTenants` has the user-to-tenant reverse row.
- `Roles` has any requested role names.

It does not create a random tenant id.

## Tenant Provisioning Configuration

Workspace-per-user behavior remains the default:

```json
{
  "IBeam": {
    "Identity": {
      "TenantProvisioning": {
        "Mode": "AutoCreateTenantForNewUser"
      }
    }
  }
}
```

Single-tenant deployments can pin auth flows to an existing/configured tenant:

```json
{
  "IBeam": {
    "Identity": {
      "TenantProvisioning": {
        "Mode": "UseDefaultTenant",
        "DefaultTenantId": "225925cc-995e-4584-a63b-4f2cb4f38f6f",
        "AutoLinkUserToDefaultTenant": true,
        "AutoLinkRoleNames": [ "Member" ]
      },
      "AzureTable": {
        "StorageConnectionString": "UseDevelopmentStorage=true",
        "TablePrefix": "WellderlyTest"
      }
    }
  }
}
```

Use `RequireExistingTenant` when auth should fail instead of mutating tenant tables:

```json
{
  "IBeam": {
    "Identity": {
      "TenantProvisioning": {
        "Mode": "RequireExistingTenant",
        "DefaultTenantId": "225925cc-995e-4584-a63b-4f2cb4f38f6f"
      }
    }
  }
}
```

In `RequireExistingTenant`, OTP/password/OAuth auth flows do not create `Tenants`, `TenantUsers`, `UserTenants`, or `Roles` records for missing memberships.

## Table Set

- `AspNetUsers`, `AspNetRoles`, `AspNetIndex`: ElCamino identity tables.
- `AuthIdentifiers`: email/SMS auth lookup bindings to `UserId`.
- `Tenants`, `TenantUsers`, `UserTenants`, `Roles`: tenant and role membership.
- `PermissionRoleMaps`: tenant permission-to-role bindings.
- `OtpChallenges`: OTP challenge state.
- `ExternalLogins`: OAuth provider-user links.
- `AuthSessions`, `AuthAttempts`: session and lockout state.
- `SystemLogs`, `SystemErrors`, `Schema`: operational records.

## Table Naming and Prefix Defaults

Physical table names are `{TablePrefix}{BaseTableName}`.

If `IBeam:Identity:AzureTable:TablePrefix` is not set, the Azure Table identity provider uses an empty prefix and creates unprefixed tables such as `AspNetUsers`, `AuthIdentifiers`, `SystemLogs`, `SystemErrors`, and `Schema`.

When `TablePrefix` is set, operational tables are also prefixed:

- `{Prefix}SystemLogs`
- `{Prefix}SystemErrors`
- `{Prefix}Schema`

Tables named `{Prefix}IdentitySystemLogs` and `{Prefix}IdentitySystemErrors` are not IBeam Azure Table identity defaults unless an app explicitly configures the base table names that way.

IBeam does not derive table prefixes from environment names. Names such as `WellderlyTest` only become prefixes when explicitly configured in `IBeam:Identity:AzureTable:TablePrefix`.

## Connection String Cascade

Identity AzureTable provider resolves connection string with fallback precedence:

1. `IBeam:Identity:AzureTable:StorageConnectionString`
2. `IBeam:AzureTables`
3. `IBeam:Repositories:ConnectionString`
4. `IBeam:ConnectionString`
5. `ConnectionStrings:AzureTables`
6. `ConnectionStrings:AzureStorage`
7. `ConnectionStrings:IBeam`
8. `ConnectionStrings:DefaultConnection`
9. `ConnectionStrings:IdentityAzureTable`
