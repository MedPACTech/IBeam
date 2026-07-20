# IBeam.AI.Enablement

`IBeam.AI.Enablement` is the documentation and prompt kit for helping AI coding agents build IBeam applications correctly.

## Purpose

This project gives AI coding agents the context they need to:

1. Understand IBeam architecture.
2. Generate code that follows IBeam layering and abstractions.
3. Extend IBeam safely without bypassing framework patterns.
4. Apply newer IBeam service-operation, permission, logging, audit, Identity, and agent-tool patterns to consuming applications.

## Builder Quick Start

1. Read [`docs/builder-quickstart.md`](docs/builder-quickstart.md).
2. Paste [`examples/builder-onboarding-prompt.md`](examples/builder-onboarding-prompt.md) into your AI tool.
3. Use [`examples/builder-onboarding-prompt-quick.md`](examples/builder-onboarding-prompt-quick.md) for a shorter version.
4. For existing APIs that need the newer permission/audit patterns, use [`examples/consuming-api-migration-prompt.md`](examples/consuming-api-migration-prompt.md).

## Core Architecture Reminder

```text
API <-- DTO object --> Service <-- Entity --> Repository
```

- API projects are gateways. They bind requests, call services, and shape responses.
- Services own business rules, permissions, logging, auditing, validation, and cross-service orchestration.
- Repositories persist one entity type and should not call other repositories or services.
- Custom service methods should use operation names like `[IBeamOperation("patients.discharge")]`.
- Agent tools and MCP handlers should call services instead of repositories.

## Contents

| Path | Purpose |
|---|---|
| [`AGENTS.md`](AGENTS.md) | Default instruction contract for AI agents. |
| [`docs/builder-quickstart.md`](docs/builder-quickstart.md) | Step-by-step guide for practical builders. |
| [`docs/architecture.md`](docs/architecture.md) | Package/layer responsibilities. |
| [`docs/extension-patterns.md`](docs/extension-patterns.md) | Extension workflow and guardrails. |
| [`docs/anti-patterns.md`](docs/anti-patterns.md) | Patterns that create drift or complexity. |
| [`blueprints/package-identity.md`](blueprints/package-identity.md) | Blueprint for `IBeam.Identity*` usage. |
| [`blueprints/package-repositories.md`](blueprints/package-repositories.md) | Blueprint for repository/provider composition. |
| [`blueprints/package-communications.md`](blueprints/package-communications.md) | Blueprint for email/SMS composition. |
| [`blueprints/component-multi-tenant-app.md`](blueprints/component-multi-tenant-app.md) | Blueprint for multi-tenant application composition. |
| [`blueprints/component-agent-api-and-mcp.md`](blueprints/component-agent-api-and-mcp.md) | Blueprint for API-key agent access and MCP tools. |
| [`catalogs/ibeam-capabilities.json`](catalogs/ibeam-capabilities.json) | Machine-readable capability map. |
| [`catalogs/architecture-rules.json`](catalogs/architecture-rules.json) | Machine-readable architecture rules. |
| [`examples/prompt-seeds.md`](examples/prompt-seeds.md) | Prompt templates for AI tools. |
| [`examples/builder-onboarding-prompt.md`](examples/builder-onboarding-prompt.md) | Full onboarding prompt. |
| [`examples/builder-onboarding-prompt-quick.md`](examples/builder-onboarding-prompt-quick.md) | Short onboarding prompt. |
| [`examples/consuming-api-migration-prompt.md`](examples/consuming-api-migration-prompt.md) | Migration prompt for existing consuming APIs. |

## Recommended Usage Flow

1. Point the AI tool to this folder and load [`AGENTS.md`](AGENTS.md).
2. Select package/component blueprint(s) in [`blueprints/`](blueprints/).
3. Load package-local `.agent/prompt.md` files from the IBeam packages being used.
4. Generate implementation in the target app/repository.
5. Validate output against [`catalogs/architecture-rules.json`](catalogs/architecture-rules.json).

## Package-Local Prompts

Runtime package projects include `.agent/prompt.md` files. Consuming agents should read the root implementation guide first, then package-local prompts for the packages they are installing.

Examples:

- [`../IBeam.Services/.agent/prompt.md`](../IBeam.Services/.agent/prompt.md)
- [`../IBeam.Identity/.agent/prompt.md`](../IBeam.Identity/.agent/prompt.md)
- [`../IBeam.AccessControl/.agent/prompt.md`](../IBeam.AccessControl/.agent/prompt.md)
- [`../IBeam.Ai.Api/.agent/prompt.md`](../IBeam.Ai.Api/.agent/prompt.md)

## Scope

This project documents and constrains architecture behavior. Runtime implementation remains in:

- `IBeam.Api*`
- `IBeam.Services*`
- `IBeam.Repositories*`
- `IBeam.Identity*`
- `IBeam.AccessControl*`
- `IBeam.Licensing*`
- `IBeam.Ai*`
- `IBeam.Communications*`
- `IBeam.Storage*`
- `IBeam.Utilities`

## Version Notes

- Documentation-only project.
- Included to help teams and agents consume IBeam consistently.
