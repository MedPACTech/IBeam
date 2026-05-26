# Prompt Seeds for AI Builders

## Seed 0: Builder Onboarding (Full)

Use `examples/builder-onboarding-prompt.md` as your first message.

## Seed 0b: Builder Onboarding (Quick)

Use `examples/builder-onboarding-prompt-quick.md` for a shorter first message.

## Seed 1: Package-First Composition

"Read IBeam.AI.Enablement/AGENTS.md. Build this feature by selecting the appropriate package blueprints under IBeam.AI.Enablement/blueprints. Keep controllers thin and put business/role rules in services."

## Seed 2: Multi-Tenant + Twilio + EF

"Use blueprints/component-multi-tenant-app.md, blueprints/package-identity.md, blueprints/package-communications.md, and blueprints/package-repositories.md. Build a multi-tenant app with Twilio communications and EF identity repositories. Enforce tenant and role rules in services and avoid circular service dependencies."

## Seed 3: Identity Extension

"Extend IBeam.Identity with options-driven behavior. Add contracts first, then services, then provider stores, then API endpoints. Preserve service-layer ownership for business logic and role checks."

## Seed 4: Error/Logging Conformance

"Implement error and logging behavior with framework defaults if no custom sinks are registered. If provider-backed defaults exist, persist to SystemErrors/SystemLogs."

## Seed 5: Architecture Review

"Review this PR against IBeam.AI.Enablement/catalogs/architecture-rules.json and report violations with file-level findings and cycle-risk observations in service dependencies."
