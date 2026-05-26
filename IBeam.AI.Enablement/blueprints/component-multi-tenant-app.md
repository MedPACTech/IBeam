# Blueprint: Component Multi-Tenant App Composition

## Objective

Compose a multi-tenant application using IBeam with specific stack requirements, e.g.:

- multi-tenant identity/authorization
- Twilio communications
- Entity Framework persistence

## Required Package Composition

1. Identity:
- `IBeam.Identity`
- `IBeam.Identity.Services`
- `IBeam.Identity.Api`
- `IBeam.Identity.Repositories.EntityFramework`

2. Repositories:
- `IBeam.Repositories`
- EF-backed app repository provider (or app-specific EF repos)

3. Communications:
- `IBeam.Communications`
- `IBeam.Communications.Sms.Twilio`

## Service-Layer Rules

1. Business rules and role checks live in services.
2. Services are mapped primarily 1:1 with their main entity/repository boundary.
3. Services may call other services for workflows, but avoid circular references.
4. If cycle risk appears, create a workflow coordinator service.

## Error/Logging Rules

1. Use framework exception pipeline.
2. If host does not customize sinks, rely on built-in fallbacks.
3. If AzureTable identity provider is enabled, default `SystemErrors`/`SystemLogs` sinks can persist records.

## AI Build Prompt Seed

"Build a multi-tenant app using IBeam with Twilio communications and Entity Framework identity repositories. Keep business rules and role enforcement in services. Use one primary service per entity/repository. Prevent circular service dependencies by introducing coordinator services when needed."
