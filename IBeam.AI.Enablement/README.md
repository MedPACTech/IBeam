# IBeam.AI.Enablement

AI enablement kit for building applications on IBeam with consistent architecture.

## Purpose

This project gives AI coding agents (Codex, Copilot, etc.) the context they need to:

1. Understand IBeam architecture.
2. Generate code that follows IBeam layering and abstractions.
3. Extend IBeam safely without bypassing framework patterns.

## Builder Quickstart

If you are a practical builder (not a deep framework engineer), start here:

1. Read `docs/builder-quickstart.md`
2. Paste `examples/builder-onboarding-prompt.md` into your AI tool
3. Or use `examples/builder-onboarding-prompt-quick.md` for a shorter version

## Contents

- `AGENTS.md`: default instruction contract for AI agents.
- `docs/builder-quickstart.md`: step-by-step guide for Codex/Clawbot-style builders.
- `docs/architecture.md`: package/layer responsibilities.
- `docs/extension-patterns.md`: extension workflow and guardrails.
- `docs/anti-patterns.md`: patterns that create drift or complexity.
- `blueprints/package-identity.md`: blueprint for `IBeam.Identity*` usage.
- `blueprints/package-repositories.md`: blueprint for repo/provider composition.
- `blueprints/package-communications.md`: blueprint for Twilio/email/SMS composition.
- `blueprints/component-multi-tenant-app.md`: blueprint for multi-tenant app composition.
- `blueprints/component-agent-api-and-mcp.md`: blueprint for API-key agent access and optional MCP tool surfaces.
- `catalogs/ibeam-capabilities.json`: machine-readable capability map.
- `catalogs/architecture-rules.json`: machine-readable architecture rules.
- `examples/prompt-seeds.md`: prompt templates for AI tools.
- `examples/builder-onboarding-prompt.md`: full onboarding prompt.
- `examples/builder-onboarding-prompt-quick.md`: short onboarding prompt.

## Recommended usage flow

1. Point AI tool to this folder and load `AGENTS.md`.
2. Select package/component blueprint(s) in `blueprints/`.
3. Generate implementation in target app/repo.
4. Validate output against `catalogs/architecture-rules.json`.

## Scope

This project documents and constrains architecture behavior. Runtime implementation remains in:

- `IBeam.Ai`
- `IBeam.Ai.Services`
- `IBeam.Ai.Api`
- `IBeam.Identity*`
- `IBeam.Repositories*`
- `IBeam.Services*`
- `IBeam.Api*`
- `IBeam.Communications*`
