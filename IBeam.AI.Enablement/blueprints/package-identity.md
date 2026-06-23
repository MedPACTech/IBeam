# Blueprint: Package Identity (`IBeam.Identity*`)

## Objective

Guide AI to implement identity features using IBeam contracts, services, and API modules without leaking provider details.

## Compose These Packages

1. `IBeam.Identity`
2. `IBeam.Identity.Services`
3. `IBeam.Identity.Api`
4. One provider package:
- `IBeam.Identity.Repositories.EntityFramework` or
- `IBeam.Identity.Repositories.AzureTable`

## Rules

1. Put auth/business policy in services.
2. Put role/permission checks in services.
3. Keep controllers thin.
4. Use options for feature toggles and lockout/profile policies.
5. Use API credentials directly for service/agent callers; do not require API-key callers to exchange credentials for JWTs.
6. Keep API credential role/scope names separate from tenant user membership roles; use the API credential role catalog for machine/agent assignment UI.

## Extension Path

1. Add contracts in `IBeam.Identity`.
2. Implement orchestration in `IBeam.Identity.Services`.
3. Add provider store changes in selected repo provider.
4. Expose endpoints in `IBeam.Identity.Api`.

## Related Blueprints

- `component-agent-api-and-mcp.md`: API-key agent access and optional MCP tool surfaces.
