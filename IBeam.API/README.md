# IBeam.Api

`IBeam.Api` is now a lightweight ASP.NET Core helper class library for API hosts.

It provides:
- `ApiControllerBase` for consistent response shapes
- global request/host exception handling
- configuration + DI composition helpers to keep `Program.cs` small

## Included building blocks

- `IBeam.Api.Controllers.ApiControllerBase`
- `IBeam.Api.Middleware.ApiExceptionMiddleware`
- `IBeam.Api.Infrastructure.GlobalErrorHandler`
- `IBeam.Api.Infrastructure.*DependencyInjection` helpers
- `IBeam.Api.Models.ApiResponse<T>`, `ApiPagedResponse<T>`, `ApiError`

## Typical usage from a host API

```csharp
using IBeam.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIBeamApi(builder.Configuration, api =>
{
    api.ConfigureOptions(cfg =>
    {
        cfg.BindOptions<JwtSettings>("JwtSettings");
        cfg.BindOptions<MyFeatureOptions>("MyFeature");
    });

    api.AddServices(
        services => services.AddScoped<IOrderService, OrderService>()
    );

    api.AddRepositories(
        (services, config) => services.AddScoped<IOrderRepository, OrderRepository>()
    );

    api.AddExternalClients(
        services => services.AddHttpClient<IMyClient, MyClient>()
    );
});

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseIBeamApi();
app.MapControllers();

app.Run();
```

## Base controller example

```csharp
using IBeam.Api.Controllers;

[Route("api/[controller]")]
public sealed class OrdersController : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id)
    {
        var dto = new { Id = id, Name = "Sample" };
        return OkResponse(dto);
    }
}
```

## Optional persistent error logging

Implement `IApiErrorSink` in your host API and register it in DI:

```csharp
services.AddScoped<IApiErrorSink, MyApiErrorSink>();
```

The middleware and global handler will call it when available.
