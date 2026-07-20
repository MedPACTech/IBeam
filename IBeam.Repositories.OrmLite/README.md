# IBeam.Repositories.OrmLite

`IBeam.Repositories.OrmLite` provides a ServiceStack OrmLite repository provider for `IBeam.Repositories`.

```powershell
dotnet add package IBeam.Repositories.OrmLite
```

## When To Use This

- You want IBeam repository abstractions backed by a relational database.
- Your application already uses ServiceStack OrmLite.
- You want service code to stay database-provider-neutral.
- You need SQL-backed persistence but do not want to use a custom repository implementation.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Repository | `OrmLiteRepositoryAsync<T>`, `IOrmLiteRepositoryAsync<T>` | Implements IBeam repository abstractions over OrmLite. |
| Store | `OrmLiteRepositoryStore<T>` | Performs relational persistence. |
| DI | `AddIBeamOrmLiteRepositories()` | Registers OrmLite repository services. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is a repository provider. It should not own service rules, permission checks, audit decisions, or DTO decoration.

## Quick Start

```csharp
using IBeam.Repositories.OrmLite;

builder.Services.AddIBeamOrmLiteRepositories();
```

The host application is responsible for registering the OrmLite connection factory and database provider configuration required by ServiceStack OrmLite.

## Data Storage

This package persists relational rows through OrmLite. It does not create Azure Tables and does not own a built-in schema inventory because table shape follows the entity classes and host OrmLite configuration.

Document app-specific table names and columns in the consuming repository/project README when you introduce domain entities.

## Service Operations, Auditing, And Permissions

Repository calls are not service-operation boundaries. Service-layer methods should be tagged with `[IBeamOperation]` and audited/authorized through IBeam service policies before they call repositories.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)

Agents should keep OrmLite-specific setup in the repository provider or consuming app composition root, not in services.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Repository cannot resolve a database connection | OrmLite connection factory missing | Register the OrmLite provider in the host app. |
| SQL table shape does not match entity | OrmLite mapping/schema mismatch | Align entity attributes and migrations/schema scripts. |
| Business rule leaked into repository | Service boundary was bypassed | Move rule/audit/permission behavior back to the service layer. |

## Version Notes

- Targets `net10.0`.
- Uses ServiceStack OrmLite packages.
- Package version is assigned by the repository release workflow.
