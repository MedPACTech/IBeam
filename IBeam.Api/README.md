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

## CrudControllerBase

`CrudControllerBase<TService, TEntity, TKey>` is an opt-in baseline controller with route defaults and operation flags.

- Base route: `api/[controller]`
- `GetById` enabled by default
- Other operations disabled by default (`GetAll`, `GetByIds`, `Post`, `Put`, `Delete`)
- Strongly-typed async service contracts (no `dynamic`)

### Service Contracts

Implement the contracts you need in your service:

- `IGetAllService<TEntity>`
- `IGetAllWithArchivedService<TEntity>`
- `IGetByIdService<TEntity, TKey>`
- `IGetByIdsService<TEntity, TKey>`
- `ICreateService<TEntity>`
- `IUpdateService<TEntity>`
- `IDeleteService<TKey>`

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

- Exceptions should bubble to `ApiExceptionMiddleware` for centralized handling.
- If an enabled action is missing the required service contract, an `InvalidOperationException` is thrown to fail fast with clear diagnostics.
