# IBeam.Communications.Email.AzureCommunications

Azure Communication Services Email provider for `IBeam.Communications`.

## Startup Registration

```csharp
builder.Services.AddIBeamAzureCommunicationsEmail(builder.Configuration);
```

Registers:
- `IEmailService` -> `AzureCommunicationsEmailService`

## Configuration

Section: `IBeam:Communications:Email:Providers:AzureCommunications`

```json
{
  "IBeam": {
    "Communications": {
      "Email": {
        "Providers": {
          "AzureCommunications": {
            "ConnectionString": "endpoint=https://...;accesskey=...",
            "DefaultFromAddress": "DoNotReply@yourdomain.com",
            "DefaultFromDisplayName": "IBeam Notifications"
          }
        }
      }
    }
  }
}
```

## Connection String Resolution

`AddIBeamAzureCommunicationsEmail(configuration)` resolves in this order:

1. `IBeam:Communications:Email:Providers:AzureCommunications:ConnectionString`
2. `IBeam:AzureCommunications`
3. `IBeam:ConnectionString`
4. `ConnectionStrings:AzureCommunications`
5. `ConnectionStrings:IBeam`
6. `ConnectionStrings:DefaultConnection`

## Options

- `ConnectionString`
- `DefaultFromAddress`
- `DefaultFromDisplayName`
