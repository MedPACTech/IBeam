# Blueprint: Package Repositories (`IBeam.Repositories*`)

## Objective

Guide AI to compose storage choices safely while preserving service-layer business ownership.

## Compose

1. `IBeam.Repositories` abstractions.
2. Provider packages as needed:
- `IBeam.Repositories.AzureTables`
- `IBeam.Repositories.OrmLite`
- `IBeam.Identity.Repositories.EntityFramework` for identity domain.

## Rules

1. Repositories persist and query only.
2. No business policy in repository methods.
3. Ensure connection-string cascade is documented and honored.
4. Keep provider-specific entities internal to provider packages.

## Service Mapping Guideline

- Prefer one primary service per entity/aggregate repository.
- Cross-entity workflows should use orchestration services/facades.
