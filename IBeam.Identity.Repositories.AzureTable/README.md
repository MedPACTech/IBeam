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

PartitionKey = AUTH|SMS|16145551212
RowKey       = USER
UserId       = {same-guid}
```

Why this matters:

- Email OTP, SMS OTP, and email/password can all resolve to the same `UserId`.
- Adding or changing email/SMS does not require moving the user row.
- Authorization remains fast because tenant membership is still loaded by `USR|{userId}`.
- SMS auth no longer needs to scan the user table by `PhoneNumber`.

The schema bootstrap creates `AuthIdentifiers` with the other custom identity tables. New create/update flows maintain bindings automatically.

## Table Set

- `AspNetUsers`, `AspNetRoles`, `AspNetIndex`: ElCamino identity tables.
- `AuthIdentifiers`: email/SMS auth lookup bindings to `UserId`.
- `Tenants`, `TenantUsers`, `UserTenants`, `TenantRoles`: tenant and role membership.
- `PermissionRoleMaps`: tenant permission-to-role bindings.
- `OtpChallenges`: OTP challenge state.
- `ExternalLogins`: OAuth provider-user links.
- `AuthSessions`, `AuthAttempts`: session and lockout state.
- `SystemLogs`, `SystemErrors`, `Schema`: operational records.

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
