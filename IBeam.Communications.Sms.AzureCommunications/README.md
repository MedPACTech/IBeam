# IBeam.Communications.Sms.AzureCommunications

`IBeam.Communications.Sms.AzureCommunications` implements `ISmsService` using Azure Communication Services SMS. It keeps SMS delivery behind IBeam abstractions while handling ACS option binding, connection fallback, message validation, and provider exception translation.

## When To Use This

- You use Azure Communication Services for SMS delivery.
- You want application services to depend on `ISmsService`, not the Azure SMS SDK.
- You need provider failures translated into IBeam SMS exception types.
- You want default sender phone numbers configured centrally.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| SMS provider | `AzureCommunicationsSmsService` | Sends `SmsMessage` with `Azure.Communication.Sms.SmsClient`. |
| Provider options | `AzureCommunicationsSmsOptions` | Holds the ACS connection string. |
| Connection validation | `AzureCommunicationsSmsConnectionStringValidator` | Validates ACS connection string shape. |
| DI registration | `AddIBeamCommunicationsSmsAzure(IConfiguration)` | Registers provider options and `ISmsService`. |
| Error translation | `SmsProviderException`, `SmsConfigurationException` | Surfaces provider and configuration failures in IBeam exception types. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is a provider implementation. A domain service decides when an SMS should be sent and calls `ISmsService`; this provider sends the message through Azure Communication Services.

## Quick Start

```csharp
using IBeam.Communications.Abstractions;
using IBeam.Communications.Sms.AzureCommunications;

builder.Services.AddIBeamCommunications(builder.Configuration);
builder.Services.AddIBeamCommunicationsSmsAzure(builder.Configuration);
```

Configuration:

```json
{
  "IBeam": {
    "Communications": {
      "Sms": {
        "FromPhoneNumber": "+16145551212",
        "DefaultToUs": true,
        "Providers": {
          "AzureCommunications": {
            "ConnectionString": "endpoint=https://example.communication.azure.com/;accesskey=..."
          }
        }
      }
    }
  }
}
```

Send an SMS:

```csharp
public sealed class VerificationSmsService
{
    private readonly ISmsService _sms;

    public VerificationSmsService(ISmsService sms)
    {
        _sms = sms;
    }

    public Task SendVerificationCodeAsync(string phoneNumber, string code, CancellationToken ct = default)
    {
        var message = new SmsMessage
        {
            Body = $"Your verification code is {code}."
        };
        message.To.Add(phoneNumber);

        return _sms.SendAsync(message, ct: ct);
    }
}
```

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `IBeam:Communications:Sms:Providers:AzureCommunications:ConnectionString` | preferred | Provider-specific ACS connection string. |
| `IBeam:AzureCommunications` | fallback | Shared ACS connection fallback. |
| `IBeam:ConnectionString` | fallback | Shared IBeam connection fallback. |
| `ConnectionStrings:AzureCommunications` | fallback | Named connection-string fallback. |
| `ConnectionStrings:IBeam` | fallback | Named IBeam connection fallback. |
| `ConnectionStrings:DefaultConnection` | fallback | Default connection-string fallback. |
| `IBeam:Communications:Sms:FromPhoneNumber` | required by shared defaults | Default sender phone number. |
| `IBeam:Communications:Sms:DefaultToUs` | `true` | Shared normalization hint. |

The provider connection string should look like:

```text
endpoint=https://<resource>.communication.azure.com/;accesskey=<key>
```

## Service Operations, Auditing, And Permissions

This provider does not own the business audit or permission boundary. Tag and wrap the consuming service method that decides to send SMS.

```csharp
[IBeamOperation("identity.otp.send")]
public Task SendOtpAsync(Guid tenantId, Guid userId, string phoneNumber, CancellationToken ct = default)
    => _operations.ExecuteAsync(
        this,
        token => SendOtpCoreAsync(tenantId, userId, phoneNumber, token),
        new ServiceOperationExecutionOptions
        {
            TenantId = tenantId,
            EntityId = userId
        },
        ct);
```

Operation names can later be used by IBeam access-control rules and audit queries.

## Data Storage

This package does not create database tables or durable stores.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| Azure Table Storage tables | No | No schema is owned by this package. |
| SMS outbox/history | No | Add a consuming-service repository if durable retry/history is required. |
| Azure Communication Services resource | No | The ACS resource and phone numbers are managed outside this package. |

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| SMS provider | `ISmsService` | Replace ACS with Twilio or a custom SMS provider. |
| Sender defaults | `SmsOptions` | Override sender phone number globally or per send. |
| Retry/outbox | Consuming service/repository | Add durable retry and status tracking outside this provider. |

## Package Relationships

| Package | Relationship |
|---|---|
| `IBeam.Communications` | Shared SMS contract, model, options, validation, and exceptions. |
| `IBeam.Communications.Sms.AzureCommunications` | Azure Communication Services SMS provider implementation. |
| `IBeam.Communications.Sms.Twilio` | Reserved Twilio package; currently scaffolded. |

## Extended Examples And Agent Guidance

- Project agent prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Repository implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Startup validation fails | No valid ACS connection string found | Configure provider-specific connection string or one of the supported fallbacks. |
| SMS sender rejected | ACS phone number is missing, invalid, or not SMS-enabled | Verify `FromPhoneNumber` and ACS phone number provisioning. |
| Recipient validation fails | Recipient is blank or invalid | Use E.164 phone numbers where possible. |
| Provider returns 429/5xx | Throttling or temporary provider issue | Retry from a consuming outbox/service if needed. |
| No audit row is written | Provider sends are not business operations | Wrap the consuming service method with `IServiceOperationExecutor`. |

## Version Notes

- Targets `net10.0`.
- Uses `Azure.Communication.Sms`.
- Package version is assigned by the repository release workflow.
