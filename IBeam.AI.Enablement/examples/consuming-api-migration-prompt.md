# Consuming API Migration Prompt

Use this prompt with an AI agent that is updating an existing consuming API codebase after adopting the latest IBeam service operation, audit logging, roles, grants, and permission mapping patterns.

```text
You are updating an existing consuming API codebase to align with the latest IBeam architecture changes.

Your goal is to apply IBeam's new service operation, audit logging, roles, grants, and permission mapping patterns without changing existing business behavior.

Important IBeam architecture rules:
- API controllers are gateways only.
- Controllers accept DTOs, call one service, and use the base controller/response pattern.
- Business logic, permissions, rules, auditing, logging, validation, and error handling belong in the service layer.
- Services are entity-focused.
- A service owns one entity/repository relationship.
- Services may call other services for lookup/rule data.
- Repositories only work with one entity and must not call other repositories or services.
- Avoid circular service dependencies.
- Errors should bubble through service results/exceptions and be handled by IBeam/API middleware where appropriate.

First, inspect the installed IBeam package/project agent prompts if available:
- Look for `.agent`, `.ai`, `AGENTS.md`, or similar prompt folders in the repo or NuGet/package content.
- Use those prompts as architectural guidance.
- Prefer the latest IBeam service, audit, and access-control patterns.

Main implementation tasks:

1. Update custom service methods with operation tags.

For every custom service method that performs meaningful business behavior, add:

```csharp
using IBeam.Services.Abstractions;

[IBeamOperation("entity.action")]
public async Task SomeCustomMethodAsync(...)
{
}
```

Use stable, lowercase operation names such as:
- `patients.discharge`
- `pricing.save`
- `purchases.delete`
- `purchases.archive`
- `transactions.export`
- `coupons.delete`

Naming guidance:
- Use `entity.action`.
- Use plural entity names where the API/service already does.
- Keep names stable because they become audit/action identifiers and future permission keys.
- Do not use controller route names unless they match the service operation concept.
- Avoid vague names like `process`, `handle`, or `execute`.

2. Wrap custom service methods with `IServiceOperationExecutor`.

Base CRUD operations should already be handled by IBeam base services. Custom service methods should be wrapped so audit and permission rules can run consistently.

Example:

```csharp
public sealed class PatientService : BaseServiceAsync<Patient, PatientDto>, IPatientService
{
    private readonly IServiceOperationExecutor _operations;

    public PatientService(
        IPatientRepository repository,
        IServiceOperationExecutor operations)
        : base(repository)
    {
        _operations = operations;
    }

    [IBeamOperation("patients.discharge")]
    public Task DischargeAsync(Guid tenantId, Guid patientId, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => DischargeCoreAsync(tenantId, patientId, token),
            new ServiceOperationExecutionOptions
            {
                TenantId = tenantId,
                EntityId = patientId,
                AuditOperation = ServiceAuditOperation.Custom
            },
            ct);

    private async Task DischargeCoreAsync(Guid tenantId, Guid patientId, CancellationToken ct)
    {
        // Existing business logic goes here.
    }
}
```

Important:
- Keep current behavior intact.
- Move existing method body into a private `CoreAsync` method.
- Public method becomes the operation-tagged wrapper.
- Pass `TenantId` whenever available.
- Pass `EntityId` when the method targets a specific entity row.
- Do not wrap internal/private helper methods unless they represent independent audited business operations.

3. Use the correct operation attribute.

For service methods, use:

```csharp
IBeam.Services.Abstractions.IBeamOperationAttribute
```

Do not use the older identity-specific operation attribute for application services:

```csharp
IBeam.Identity.Authorization.IBeamOperationAttribute
```

The older identity attribute is for legacy identity/catalog compatibility. New consuming API services should use the service-layer abstraction.

4. Configure auditing.

Ensure IBeam service auditing/logging is registered and configured.

Expected behavior:
- Adds, updates, deletes, archives, and custom tagged service operations should be auditable.
- Audit records should capture:
  - service name
  - entity name
  - operation/action name
  - tenantId when available
  - entityId when available
  - userId/actor when available
  - before/after JSON when configured
  - timestamp
  - success/failure
  - error info when failures occur

Prefer configuration defaults that audit most write/custom operations, then selectively disable noisy or sensitive operations.

Example configuration shape:

```json
{
  "IBeam": {
    "ServiceAudit": {
      "Enabled": true,
      "DefaultMode": "AuditWrites",
      "CaptureBefore": true,
      "CaptureAfter": true,
      "Services": {
        "PatientService": {
          "Enabled": true,
          "Operations": {
            "patients.discharge": {
              "Enabled": true
            }
          }
        }
      }
    }
  }
}
```

Use the actual configuration model available in the installed IBeam package.

5. Prepare service operations for future permissions.

Operation names should be designed so permissions can later be granted or denied by pattern.

Examples:
- Allow all pricing operations: `pricing.*`
- Allow all transactions operations: `transactions.*`
- Deny sales operations: `sales.*`
- Deny one destructive operation: `coupons.delete`

Do not hard-code authorization decisions inside controllers. Permission decisions should be centralized through IBeam access-control/service-operation authorization.

6. Apply role and grant concepts correctly.

IBeam role/access concepts:
- Tenant roles live in the IBeam role catalog.
- Role assignments should prefer stable role IDs where available.
- Role names may still be used for display, claims, compatibility, or public API claims.
- Permission mappings connect roles to service operation names.
- Resource grants are for access to specific resources/entities.
- API credentials/agents may have different grants than users.

If the consuming API has its own auth system:
- Do not force IBeam.Identity adoption.
- Use IBeam core services/access-control abstractions where possible.
- Map the existing authenticated principal into IBeam's actor/principal/tenant context providers.
- Ensure user/role/tenant/agent identity data is available to the service layer.

7. Keep controllers thin.

Controller example:

```csharp
[HttpPost("{patientId:guid}/discharge")]
public async Task<IActionResult> Discharge(Guid patientId, CancellationToken ct)
{
    await _patientService.DischargeAsync(CurrentTenantId, patientId, ct);
    return Ok();
}
```

Do not place permission checks, audit calls, repository calls, or business rules in the controller.

8. Add tests.

Add or update tests for each migrated service method group:
- Existing behavior still passes.
- Operation wrapper is invoked for custom service methods.
- TenantId and EntityId are passed when available.
- Domain exceptions remain the same type as before.
- Audit failure behavior does not hide original business exceptions.
- Permission-denied tests can be added where access-control is already wired.

Suggested test pattern:
- Use a fake/recording `IServiceOperationExecutor`.
- Assert the method name and options are passed.
- Let the fake executor run the original operation delegate.
- Verify existing business behavior remains unchanged.

9. Do not over-refactor.

Avoid broad rewrites. Keep this migration focused:
- Add operation attributes.
- Wrap custom service methods.
- Add configuration wiring if missing.
- Add tests.
- Preserve public API behavior.
- Preserve DTO/entity/repository boundaries.

Final deliverables:
- List every service method tagged with `IBeamOperation`.
- List every custom method wrapped with `IServiceOperationExecutor`.
- List audit/access-control configuration added or changed.
- List tests added or updated.
- Call out any service methods that could not be safely tagged yet and why.
- Run the relevant build and test commands before finishing.
```
