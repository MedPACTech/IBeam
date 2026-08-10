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

Each aggregate type is stored in its own table with stable partition and row keys plus a JSON payload. Purchases have hashed correlation, provider-event, and license-key indexes; checkout attempts are retained separately. Claim-token hashes and provider-binding history are durable, while raw claim tokens and provider webhook bodies are never stored.

Claim issuance and provider-binding switches use same-partition Azure Table transactions. Claim consumption and provider migration use ETags to reject stale concurrent writes. Expired, unclaimed purchases can be removed in bounded batches through `IBillingPurchaseStore.DeleteExpiredPurchasesAsync`.
