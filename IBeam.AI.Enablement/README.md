# IBeam.AI.Enablement

AI enablement kit for building applications on IBeam with consistent architecture.

## Purpose

This project gives AI coding agents (Codex, Copilot, etc.) the context they need to:

1. Understand IBeam architecture.
2. Generate code that follows IBeam layering and abstractions.
3. Extend IBeam safely without bypassing framework patterns.

## Contents

- `AGENTS.md`: default instruction contract for AI agents.
- `docs/architecture.md`: package/layer responsibilities.
- `docs/extension-patterns.md`: extension workflow and guardrails.
- `docs/anti-patterns.md`: patterns that create drift or complexity.
- `blueprints/package-identity.md`: blueprint for `IBeam.Identity*` usage.
- `blueprints/package-repositories.md`: blueprint for repo/provider composition.
- `blueprints/package-communications.md`: blueprint for Twilio/email/SMS composition.
- `blueprints/component-multi-tenant-app.md`: blueprint for multi-tenant app composition.
- `catalogs/ibeam-capabilities.json`: machine-readable capability map.
- `catalogs/architecture-rules.json`: machine-readable architecture rules.
- `examples/prompt-seeds.md`: prompt templates for AI tools.

## Recommended usage flow

1. Point AI tool to this folder and load `AGENTS.md`.
2. Select package/component blueprint(s) in `blueprints/`.
3. Generate implementation in target app/repo.
4. Validate output against `catalogs/architecture-rules.json`.

## Scope

This project documents and constrains architecture behavior. Runtime implementation remains in:

- `IBeam.Identity*`
- `IBeam.Repositories*`
- `IBeam.Services*`
- `IBeam.Api*`
- `IBeam.Communications*`
