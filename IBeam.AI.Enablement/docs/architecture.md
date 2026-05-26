# IBeam Architecture Map

## Core Packages

1. `IBeam.Api`
- API infrastructure, middleware integration points, error abstractions.

2. `IBeam.Identity`
- Contracts for identity domain (interfaces, models, options, events).

3. `IBeam.Identity.Services`
- Identity business logic and orchestration.
- Owns auth rules, lockout policy, profile extension behavior, role/permission decision points.

4. `IBeam.Identity.Repositories.AzureTable`
- Azure Table identity provider implementation (ElCamino-based user store + custom stores).

5. `IBeam.Identity.Repositories.EntityFramework`
- Entity Framework identity provider implementation.

6. `IBeam.Repositories*`
- Generic repository abstractions and provider implementations.

7. `IBeam.Communications*`
- Email/SMS provider abstractions and concrete providers (Twilio, ACS, etc.).

## Architecture Principles

1. Business rules and role checks live in services.
2. Repositories are persistence adapters, not business orchestrators.
3. One service is primarily responsible for one entity/aggregate boundary.
4. Services may collaborate, but dependency cycles are prohibited.
5. Error and logging fallbacks should exist when hosts do not customize sinks.

## Multi-Tenant Principle

Identity is global; authorization is tenant-scoped.
- User identity can span tenants.
- Access decisions are tenant-aware and service-enforced.
