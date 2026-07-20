# IBeam.Communications.Email.AzureCommunications

`IBeam.Communications.Email.AzureCommunications` implements IBeam email delivery through Azure Communication Services Email. It lets application services depend on `IEmailService` while this provider handles Azure-specific connection setup, submission, validation, and provider exception translation.

## When To Use This

- You use Azure Communication Services as your production email provider.
- You want domain services to stay independent of the Azure SDK.
- You want Azure request/provider failures translated into IBeam communication exceptions.
- You want one email provider registration that can be swapped without changing service code.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Email provider | `AzureCommunicationsEmailService` | Sends `EmailMessage` through `Azure.Communication.Email.EmailClient`. |
| Provider options | `AzureCommunicationsEmailOptions` | Holds the ACS connection string. |
| Connection validation | `AzureCommunicationsConnectionStringValidator` | Validates ACS connection string shape before startup completes. |
| DI registration | `AddIBeamAzureCommunicationsEmail(IConfiguration)` | Registers provider options and `IEmailService`. |
| Error translation | `EmailProviderException`, `EmailConfigurationException` | Surfaces friendly provider/configuration failures to consuming services. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is a provider implementation. It should sit behind services that call `IEmailService`. It should not contain business rules, API response logic, tenant authorization, or persistence logic.

## Quick Start

```csharp
using IBeam.Communications.Abstractions;
using IBeam.Communications.Email.AzureCommunications;

builder.Services.AddIBeamCommunications(builder.Configuration);
builder.Services.AddIBeamAzureCommunicationsEmail(builder.Configuration);
```

Configuration:

```json
{
  "IBeam": {
    "Communications": {
      "Email": {
        "FromAddress": "DoNotReply@contoso.com",
        "FromName": "Contoso"
      }
    }
  }
}
```

```json
{
  "IBeam": {
    "Communications": {
      "Email": {
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

Send an email:

```csharp
public sealed class ReceiptService
{
    private readonly IEmailService _email;

    public ReceiptService(IEmailService email)
    {
        _email = email;
    }

    public Task SendReceiptAsync(string to, CancellationToken ct = default)
    {
        var message = new EmailMessage
        {
            Subject = "Your receipt",
            HtmlBody = "<p>Thanks for your purchase.</p>",
            TextBody = "Thanks for your purchase."
        };
        message.To.Add(to);

        return _email.SendAsync(message, ct: ct);
    }
}
```

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `IBeam:Communications:Email:Providers:AzureCommunications:ConnectionString` | required | Azure Communication Services connection string. |
| `IBeam:Communications:Email:FromAddress` | required by shared defaults | Default sender address resolved by shared sender policy. |
| `IBeam:Communications:Email:FromName` | `null` | Optional sender display name for shared defaults. |

The provider connection string must look like:

```text
endpoint=https://<resource>.communication.azure.com/;accesskey=<key>
```

## Service Operations, Auditing, And Permissions

This provider does not own audit or permission decisions. The consuming service that decides to send the email should be tagged and wrapped.

```csharp
[IBeamOperation("billing.receipt.send")]
public Task SendReceiptAsync(Guid tenantId, Guid receiptId, CancellationToken ct = default)
    => _operations.ExecuteAsync(
        this,
        token => SendReceiptCoreAsync(tenantId, receiptId, token),
        new ServiceOperationExecutionOptions
        {
            TenantId = tenantId,
            EntityId = receiptId
        },
        ct);
```

Keep Azure provider code focused on delivery mechanics. Put business decisions, permission rules, and audit boundaries in the service layer that calls `IEmailService`.

## Data Storage

This package does not create tables or durable stores.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| Azure Table Storage tables | No | No schema is owned by this provider. |
| Email outbox | No | Add a consuming-service outbox if durable retries are required. |
| Azure Communication Services resource | No | The ACS resource is provisioned outside this package. |

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| Email provider | `IEmailService` | Swap ACS for SMTP, SendGrid, pickup directory, or a custom provider. |
| Sender defaults | `EmailOptions` | Override sender address/name per message or per call. |
| Provider error handling | Catch `EmailProviderException` in service/API boundary | Add retry, alerting, or user-safe error translation. |

## Package Relationships

| Package | Relationship |
|---|---|
| `IBeam.Communications` | Provides `IEmailService`, `EmailMessage`, `EmailOptions`, validation, and exceptions. |
| `IBeam.Communications.Email.AzureCommunications` | Implements Azure Communication Services email delivery. |
| `IBeam.Communications.Email.Templating` | Optional template rendering before sending through this provider. |

## Extended Examples And Agent Guidance

- Project agent prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Repository implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Startup validation fails | Missing or malformed ACS connection string | Configure `IBeam:Communications:Email:Providers:AzureCommunications:ConnectionString`. |
| ACS rejects sender | Sender address/domain is not configured in ACS | Verify the sender/domain in Azure Communication Services. |
| `EmailProviderException` with 401/403 | Invalid key or resource authorization problem | Rotate the connection string or check ACS access. |
| `EmailProviderException` with 429/5xx | Provider throttling or temporary failure | Retry from the consuming service/outbox if needed. |
| No audit row is written | Provider sends are not business operations | Wrap the consuming service method with `IServiceOperationExecutor`. |

## Version Notes

- Targets `net10.0`.
- Uses `Azure.Communication.Email`.
- Package version is assigned by the repository release workflow.
