# IBeam.Services.Logging

Add-on package for IBeam service auditing sinks.

## What it provides

- `LoggerAuditTrailSink` for `ILogger` transport
- `RepositoryAuditTrailSink` for table-style persistence through `IBaseRepositoryAsync<ServiceAuditLogEntry>`
- `HttpContextAuditActorProvider` for actor resolution from authenticated user claims

## Registration

```csharp
builder.Services.AddIBeamLoggerAuditTrail(builder.Configuration);
// or
builder.Services.AddIBeamRepositoryAuditTrail(builder.Configuration);

builder.Services.AddIBeamHttpContextAuditActor();
```

## Audit options

Configuration section: `IBeam:Services:Audit`

- `Enabled` (default `false`)
- `EnableSelectAudits` (default `false`)
- `SelectMode` (`None` or `DailyRollup`)
- `FailOnAuditError` (default `false`)
