# IBeam.Api

`IBeam.Api` provides reusable API primitives for consistent ASP.NET Core endpoints:

- Standardized success/error response envelopes (`ApiResponse`, `ApiPagedResponse`)
- Global exception middleware (`UseApiExceptionHandling`)
- API configuration/DI helpers (`AddIBeamApi`)
- Reusable base controllers (`ApiControllerBase`, `CrudControllerBase`)

## Quick Start

Register API services:

```csharp
builder.Services.AddIBeamApi(builder.Configuration);
```

Use middleware:

```csharp
app.UseApiExceptionHandling();
```

## Service Boundary and Errors

Controllers should stay thin. They adapt HTTP requests to service calls and shape responses; they should not own business rules, persistence rules, or duplicate service policy checks.

Services own business rules, validation decisions, logging/auditing decisions, and expected error classification. Expected failures should use typed, user-safe exceptions or service results that controllers can return as friendly messages.

Unexpected exceptions and system-level failures should bubble to `ApiExceptionMiddleware`. The middleware returns a generic error response unless detailed errors are enabled, and persists operational details through `IApiErrorSink` when configured. In the Azure Table identity provider, that sink writes to the `SystemErrors` table.

## CrudControllerBase

`CrudControllerBase<TService, TEntity, TKey>` is an opt-in baseline controller with route defaults and operation flags.

- Base route: `api/[controller]`
- `GetById` enabled by default
- Other operations disabled by default (`GetAll`, `GetByIds`, `Post`, `Put`, `Delete`)
- Cursor and offset/page-number paging are disabled by default
- Strongly-typed async service contracts (no `dynamic`)

### Service Contracts

Implement the contracts you need in your service:

- `IGetAllService<TEntity>`
- `IGetAllWithArchivedService<TEntity>`
- `IGetAllCursorPagedService<TEntity>`
- `IGetAllOffsetPagedService<TEntity>`
- `IGetByIdService<TEntity, TKey>`
- `IGetByIdsService<TEntity, TKey>`
- `ICreateService<TEntity>`
- `IUpdateService<TEntity>`
- `IDeleteService<TKey>`

### Paging

`GetAll` supports unpaged, cursor-paged, and offset/page-number-paged execution. Paging is opt-in through controller flags and matching service contracts.

Cursor paging:

```http
GET /api/patients?pageSize=25
GET /api/patients?pageSize=25&continuationToken=opaque-token
```

Requires:

- `AllowGetAllCursorPaged` => `true`
- `IGetAllCursorPagedService<TEntity>`

Returns `ApiCursorPagedResponse<T>` with `pageSize` and `continuationToken`.

Offset/page-number paging:

```http
GET /api/patients?pageNumber=2&pageSize=25
```

Requires:

- `AllowGetAllOffsetPaged` => `true`
- `IGetAllOffsetPagedService<TEntity>`

Returns `ApiOffsetPagedResponse<T>` with `pageNumber`, `pageSize`, and `totalCount`.

Do not combine `pageNumber` and `continuationToken`; the base controller returns `400 Bad Request`.

### Example

```csharp
using IBeam.Api.Abstractions;
using IBeam.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

public sealed record Patient(Guid Id, string Name);

public interface IPatientService :
    IGetByIdService<Patient, Guid>,
    IGetAllService<Patient>,
    ICreateService<Patient>,
    IUpdateService<Patient>,
    IDeleteService<Guid>;

[ApiController]
[Route("api/[controller]")]
public sealed class PatientsController : CrudControllerBase<IPatientService, Patient, Guid>
{
    public PatientsController(IPatientService service) : base(service) { }

    protected override bool AllowGetAll => true;
    protected override bool AllowPost => true;
    protected override bool AllowPut => true;
    protected override bool AllowDelete => true;
}
```

### Created (201) Behavior

For POST, default response is `200 OK` with envelope.  
If you want `201 Created`, override:

- `ReturnCreatedOnPost` => `true`
- `BuildCreatedRouteValues(TEntity createdEntity)` with route values for `GetById`

## Notes

- Unexpected exceptions should bubble to `ApiExceptionMiddleware` for centralized handling.
- If an enabled action is missing the required service contract, an `InvalidOperationException` is thrown to fail fast with clear diagnostics.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

Agents should keep API controllers as gateways. Business logic, permissions, rules, logging, and expected error classification belong in the service layer.
