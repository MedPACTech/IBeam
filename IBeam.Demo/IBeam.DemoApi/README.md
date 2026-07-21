# IBeam.DemoApi

`IBeam.DemoApi` is a sample ASP.NET Core API host showing how an application composes IBeam services, authentication, authorization, OpenAPI, and controllers.

## When To Use This

- You want a reference API composition root for IBeam packages.
- You want to see where service packages are registered.
- You need a small app for manual testing, smoke tests, or demos.

## What This Project Contains

| Area | File/Type | Purpose |
|---|---|---|
| Startup/composition | `Program.cs` | Registers controllers, demo services, JWT auth, authorization, and OpenAPI/Scalar. |
| Controllers | `Controllers/*` | Thin HTTP endpoints for auth, identity health, current-user, and secure route examples. |
| Configuration | `appsettings*.json` | Demo JWT and host configuration. |
| HTTP examples | `IBeam.DemoApi.http` | Sample requests for local testing. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This project is an API host. Controllers should stay thin and call services. Business rules, permissions, logging, auditing, validation, and expected error behavior should be implemented in `IBeam.DemoService` or the IBeam packages it composes.

## Quick Start

```powershell
dotnet run --project IBeam.Demo/IBeam.DemoApi/IBeam.DemoApi.csproj
```

In development, OpenAPI is exposed at:

```text
/openapi/v1.json
```

Scalar API reference is mapped by `MapScalarApiReference()`.

## Service Operations, Auditing, And Permissions

Demo API endpoints should call operation-tagged services. If a demo endpoint grows beyond request binding and response shaping, move that behavior into the service layer and add an operation name such as `[IBeamOperation("demo.ping")]`.

## Data Storage

This project does not own tables or repositories directly. Storage comes from whatever IBeam providers are registered by the demo service layer and configuration.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../../.agent/implementation-guide.md`](../../.agent/implementation-guide.md)
- Demo service README: [`../IBeam.DemoService/README.md`](../IBeam.DemoService/README.md)
- Service logging and audit: [`../../docs/service-logging-and-audit.md`](../../docs/service-logging-and-audit.md)
- Service operation permissions: [`../../docs/service-operation-permissions.md`](../../docs/service-operation-permissions.md)

Agents should use this as a composition example and avoid putting framework behavior directly in `Program.cs` unless it belongs at the host boundary.

## Version Notes

- Targets `net10.0`.
- Demo/reference project, not a reusable package.
