# IBeam.Services.Logging

Add-on package for IBeam service auditing sinks.

## What it provides

- `LoggerAuditTrailSink` for `ILogger` transport
- `RepositoryAuditTrailSink` for table-style persistence through `IBaseRepositoryAsync<ServiceAuditLogEntry>`
- `AzureTableSystemLogSink` for durable `IBeamSystemLogs` storage in Azure Table Storage
- `HttpContextAuditActorProvider` for actor resolution from authenticated user claims

## Registration

```csharp
builder.Services.AddIBeamLoggerAuditTrail(builder.Configuration);
// or
builder.Services.AddIBeamRepositoryAuditTrail(builder.Configuration);

builder.Services.AddIBeamHttpContextAuditActor();
```

To persist system logs and service audit transactions to Azure Table Storage:

```csharp
builder.Services.AddIBeamAzureTableSystemLogs(builder.Configuration);
builder.Services.AddIBeamHttpContextAuditActor();
```

Configuration section:

```json
{
  "IBeam": {
    "Logging": {
      "AzureTable": {
        "StorageConnectionString": "<connection-string>",
        "TablePrefix": "IBeam",
        "TableName": "SystemLogs"
      }
    }
  }
}
```

With the default `IBeam` prefix, the physical table is `IBeamSystemLogs`.

## Audit options

Configuration section: `IBeam:Services:Audit`

- `Enabled` (default `false`)
- `EnableSelectAudits` (default `false`)
- `SelectMode` (`None` or `DailyRollup`)
- `FailOnAuditError` (default `false`)
