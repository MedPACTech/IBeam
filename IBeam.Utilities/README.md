# IBeam.Utilities

`IBeam.Utilities` contains shared primitives used across the IBeam framework.

```powershell
dotnet add package IBeam.Utilities
```

## When To Use This

- You need IBeam's common exception middleware and error helpers.
- You need shared audit-event primitives.
- You want reusable cache wrappers or token helpers used by IBeam packages.
- You are building an IBeam package and need framework-level utility types.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Exceptions | `ExceptionMiddleware` and related types/options | Provides consistent exception handling behavior for ASP.NET Core hosts. |
| Auditing primitives | `AuditEvent`, `AuditAction`, `AuditEventBuilder` | Shared audit-event structures used by higher-level packages. |
| Caching | `DataCache`, `NamespacedMemoryCache` | Small wrappers for shared cache behavior. |
| Settings | `BaseAppSettings`, `IBaseAppSettings` | Common application setting shape. |
| Tokens | `TokenGenerator` | Utility token generation. |
| Validation/roles | `Validations`, `Role` | Small shared helper models. |

## Architecture Fit

Utilities should stay small and cross-cutting. Domain rules belong in services, transport behavior belongs in API packages, and persistence belongs in repositories.

## Code Example

```csharp
app.UseMiddleware<ExceptionMiddleware>();
```

Utility types should usually support higher-level packages rather than becoming a dumping ground for domain behavior.

## Data Storage

This package does not create tables, repositories, containers, or buckets.

## Service Operations, Auditing, And Permissions

This package contains shared primitives but does not own service-operation authorization or audit persistence. Use `IBeam.Services` and `IBeam.Services.Logging` for those runtime patterns.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)

Agents should only add utilities here when multiple packages genuinely share the need.

## Version Notes

- Targets `net10.0`.
- Package version is assigned by the repository release workflow.
