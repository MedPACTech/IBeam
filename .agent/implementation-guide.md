# IBeam Agent Implementation Guide

Use this guide whenever you modify or extend IBeam. It is written for coding agents, automation, and contributors that need to preserve the framework architecture.

## Core Architecture

IBeam keeps a strict one-way flow:

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

The API is a gateway. The service is the business boundary. The repository is the persistence boundary.

## API Rules

- Keep API endpoints thin.
- API controllers and endpoints should accept request DTOs, call one service method, and return the service result.
- Use the IBeam base controller/response helpers where available.
- Do not put business rules, persistence rules, permission decisions, audit decisions, or cross-entity orchestration in the API layer.
- API-level error handling should let expected service errors flow into normal responses.
- Unexpected system errors should bubble to IBeam middleware/global error handling.

## Service Rules

- Put business logic, permissions, rules, logging, audit, validation, error translation, and orchestration in the service layer.
- Services are entity-focused and should primarily bind to one repository.
- A service may call another service when it needs lookup data, rule evaluation, or coordinated behavior owned by another entity.
- Watch for circular service references. If two services need each other, extract the shared rule/orchestration into a separate service or policy component.
- Prefer base service CRUD operations and override hooks before inventing new CRUD plumbing.
- Use IBeam operation names for service actions, for example `pricing.update`, `patients.discharge`, or `transactions.export`.
- Keep permissions and audit action names aligned when possible.

## Repository Rules

- Repositories deal with one entity type.
- Repositories should not call other repositories.
- Repositories should not enforce business permissions, service rules, or API response behavior.
- Repository errors should bubble to services, where they can be translated or wrapped consistently.

## DTO, Model, and Entity Rules

- API DTOs and service models are contract/request/response shapes.
- Entities represent stored data.
- Audit before/after snapshots should use database entities, not decorated outbound DTOs.
- Avoid leaking storage-only details into API DTOs unless they are intentionally part of the public contract.

## Permissions and Audit

- Use `IBeamOperationAttribute` to give service calls stable operation names.
- Use service-operation permissions in `IBeam.AccessControl` for role/subject authorization.
- Keep dynamic permission management optional. Apps may use config, scripts, DB stores, or API endpoints.
- Use IBeam audit sinks for durable entity change history.
- Use `ILogger<T>` for diagnostics and runtime troubleshooting.

## Dependency Direction

- API can reference services/contracts.
- Services can reference repositories/contracts.
- Repositories should not reference services or APIs.
- Core packages should avoid depending on Identity unless the feature truly belongs to Identity.
- Bring-your-own-auth should remain possible anywhere access control only needs claims/roles.

## Before You Code

1. Inspect the project-local `.agent/prompt.md`.
2. Inspect the existing interfaces, base classes, and extension methods.
3. Follow the local package style.
4. Add focused tests for behavior changes.
5. Update docs when public behavior, configuration, or package registration changes.

