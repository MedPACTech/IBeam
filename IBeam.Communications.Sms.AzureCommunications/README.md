# IBeam.Communications.Sms.AzureCommunications

Azure Communication Services SMS provider for `IBeam.Communications`.

## Startup Registration

```csharp
builder.Services.AddIBeamCommunicationsSmsAzure(builder.Configuration);
```

Registers:
- `ISmsService` -> `AzureCommunicationsSmsService`

## Configuration

Section: `IBeam:Communications:Sms:Providers:AzureCommunications`

```json
{
  "IBeam": {
    "Communications": {
      "Sms": {
        "Providers": {
          "AzureCommunications": {
            "ConnectionString": "endpoint=https://...;accesskey=..."
          }
        }
      }
    }
  }
}
```

## Connection String Resolution

`AddIBeamCommunicationsSmsAzure(configuration)` resolves in this order:

1. `IBeam:Communications:Sms:Providers:AzureCommunications:ConnectionString`
2. `IBeam:AzureCommunications`
3. `IBeam:ConnectionString`
4. `ConnectionStrings:AzureCommunications`
5. `ConnectionStrings:IBeam`
6. `ConnectionStrings:DefaultConnection`

## Options

- `ConnectionString`
