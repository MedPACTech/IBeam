# IBeam.Commerce.Repositories.AzureTable

Azure Table Storage store providers for the IBeam licensing, billing, and credits stack.

```csharp
using IBeam.Commerce.Repositories.AzureTable;

builder.Services.AddIBeamLicensingServices(builder.Configuration);
builder.Services.AddIBeamBillingServices(builder.Configuration);
builder.Services.AddIBeamCreditServices(builder.Configuration);
builder.Services.AddIBeamCommerceAzureTableStores(builder.Configuration);
```

## Configuration

```json
{
  "IBeam": {
    "Commerce": {
      "AzureTable": {
        "StorageConnectionString": "UseDevelopmentStorage=true",
        "TablePrefix": "IBeamDev",
        "CreateTablesIfNotExists": true
      }
    }
  }
}
```

The connection string can also be supplied through the common IBeam Azure Tables connection-string cascade used by other IBeam Azure Table packages.

## Storage Strategy

Each aggregate type is stored in its own table with a stable tenant partition key and a JSON payload. Credit ledger writes use Azure Table `AddEntity` so duplicate ledger ids are treated as idempotent append attempts instead of overwriting existing usage. Reservations and billing/license records use replace upserts for normal lifecycle updates.
