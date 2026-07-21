# IBeam.DemoService

`IBeam.DemoService` is the sample service-layer project used by the IBeam demo API.

## When To Use This

- You want a minimal example of where application service registrations belong.
- You need a safe place to demonstrate IBeam service patterns.
- You want to test host composition without changing framework packages.

## What This Project Contains

| Area | File/Type | Purpose |
|---|---|---|
| Service registration | `DependencyInjection.AddDemoService(...)` | Registers demo services and delegates identity composition. |
| Demo service | `IDemoService`, `DemoService` | Minimal service example currently exposing `PingAsync`. |
| Identity composition hook | `Identity/DemoIdentityServiceCollectionExtensions.cs` | Placeholder for demo identity setup. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This project is the service layer for the demo app. If demo behavior becomes more realistic, keep rules, permissions, logging, auditing, validation, and error classification here. Repositories should stay entity-specific and controllers should remain thin.

## Code Example

```csharp
public sealed class DemoService : IDemoService
{
    [IBeamOperation("demo.ping")]
    public Task<string> PingAsync(CancellationToken ct = default)
        => Task.FromResult("DemoService is alive.");
}
```

If `PingAsync` later changes data or performs protected work, wrap it with `IServiceOperationExecutor` so service policy and audit behavior apply consistently.

## Data Storage

This project does not currently define entities, repositories, tables, or storage providers.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../../.agent/implementation-guide.md`](../../.agent/implementation-guide.md)
- Demo API README: [`../IBeam.DemoApi/README.md`](../IBeam.DemoApi/README.md)
- Service logging and audit: [`../../docs/service-logging-and-audit.md`](../../docs/service-logging-and-audit.md)
- Service operation permissions: [`../../docs/service-operation-permissions.md`](../../docs/service-operation-permissions.md)

Agents should use this project to demonstrate clean service patterns before moving ideas into reusable IBeam packages.

## Version Notes

- Targets `net10.0`.
- Demo/reference project, not a reusable package.
