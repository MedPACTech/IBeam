# IBeam Extension Patterns

## Pattern 1: Add a New Domain Module

1. Create contracts package section (interfaces/models/options/events).
2. Create services package implementation.
3. Create one or more repository provider implementations.
4. Add API controller endpoints and DI composition extension.

## Pattern 2: Add New Authentication Behavior

1. Extend options with feature flag.
2. Add service orchestration method.
3. Add/extend store interface if new persistence needed.
4. Implement provider-specific store changes in Azure Table + EF where applicable.
5. Emit lifecycle events and include idempotency metadata.

## Pattern 3: Add Multi-Tenant Business Feature (Scheduling, Billing, etc.)

1. Persist tenant ownership in all records.
2. Query by tenant boundary first.
3. Add permission mapping for feature actions.
4. Keep tenant policy checks in services, not only controllers.

## Versioning Guidance

1. Prefer additive changes to contracts.
2. Avoid breaking controller payloads unless versioned route introduced.
3. Document config additions and defaults.
