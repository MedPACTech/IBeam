# IBeam.Identity.Repositories.EntityFramework

Entity Framework identity repository provider for IBeam.

## Narrative Introduction

This package offers EF-based Identity store wiring and tenant membership persistence for teams that prefer relational storage. It centralizes provider selection and DbContext setup behind one registration method so hosts can swap persistence approaches without changing auth orchestration code.

## Features and Components

- DI extension:
  - `AddIBeamIdentityEntityFrameworkStores(IServiceCollection, IConfiguration, string configSectionPath = "IdentityEf")`
- `IBeamIdentityDbContext` registration
- ASP.NET Core Identity EF store wiring
- tenant membership store implementation

## Dependencies

- Internal packages:
  - `IBeam.Identity.Services`
  - `IBeam.Identity`
- External packages:
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore.Sqlite`
  - `Microsoft.EntityFrameworkCore.SqlServer`
  - `Npgsql.EntityFrameworkCore.PostgreSQL`
  - `System.IdentityModel.Tokens.Jwt`
  - `Microsoft.AspNetCore.App` framework reference

## Provider Status

- Supported now: `Sqlite`
- Not yet active in extension wiring: `SqlServer`, `Postgres`

## Auth Identifier Parity Guidance

The shared identity services expect provider implementations to resolve users by auth identifier without scanning all users. Azure Table uses an `AuthIdentifiers` table for this.

An EF provider should use an equivalent relational table with a unique key on `(IdentifierType, NormalizedIdentifier)`:

```sql
create table AuthIdentifiers (
    IdentifierType nvarchar(32) not null,
    NormalizedIdentifier nvarchar(256) not null,
    UserId uniqueidentifier not null,
    BoundAtUtc datetimeoffset not null,
    constraint PK_AuthIdentifiers primary key (IdentifierType, NormalizedIdentifier)
);
```

Expected behavior:

- `FindByEmailAsync` resolves `email + normalized email` to `UserId`.
- `FindByPhoneAsync` resolves `sms + normalized phone` to `UserId`.
- `UpdateEmailAsync` and `UpdatePhoneAsync` move the identifier binding to the same `UserId`.
- Email/password linking and phone linking should never create a second user for an already-authenticated person.

## Connection String Cascade

EF identity store registration resolves connection string with fallback precedence:

1. `{configSectionPath}:ConnectionString` (default section path is `IdentityEf`)
2. `IBeam:Identity:EntityFramework:ConnectionString`
3. `IBeam:Repositories:EntityFramework:ConnectionString`
4. `IBeam:Repositories:ConnectionString`
5. `IBeam:ConnectionString`
6. `ConnectionStrings:IdentityEf`
7. `ConnectionStrings:IdentityEntityFramework`
8. `ConnectionStrings:IBeam`
9. `ConnectionStrings:DefaultConnection`

This aligns EF identity provider behavior with the broader IBeam repository fallback pattern.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Identity contracts: [`../IBeam.Identity/README.md`](../IBeam.Identity/README.md)
- Azure Table schema inventory for parity checks: [`../docs/identity-azure-table-schema-inventory.md`](../docs/identity-azure-table-schema-inventory.md)
- Roles, permissions, and grants: [`../docs/roles-permissions-and-grants.md`](../docs/roles-permissions-and-grants.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)

Agents should use the Azure Table schema inventory as a parity checklist when extending the EF provider, while preserving EF-native relational design.
