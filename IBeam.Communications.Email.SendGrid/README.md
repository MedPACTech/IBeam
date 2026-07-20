# IBeam.Communications.Email.SendGrid

`IBeam.Communications.Email.SendGrid` implements `IEmailService` using SendGrid. It lets application and domain services send email through IBeam abstractions without taking a direct dependency on SendGrid SDK types.

## When To Use This

- You use SendGrid for production or staging email.
- You want provider reliability while keeping business services provider-neutral.
- You need SendGrid sandbox mode for staging environments.
- You want SendGrid failures translated into IBeam communication exceptions.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Email provider | `SendGridEmailService` | Sends `EmailMessage` through SendGrid. |
| Provider options | `SendGridEmailOptions` | Holds API key, default sender, display name, and sandbox mode. |
| Address mapping | `SendGridAddressMapper` | Converts IBeam email addresses to SendGrid SDK addresses. |
| DI registration | `AddIBeamSendGridEmail(IConfiguration)` | Registers provider options and `IEmailService`. |
| Error translation | `EmailProviderException` | Wraps provider failures in an IBeam exception shape. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is a provider boundary. Domain services call `IEmailService`; this package performs SendGrid delivery. Controllers should not call `SendGridClient`, and repositories should not send email.

## Quick Start

```csharp
using IBeam.Communications.Abstractions;
using IBeam.Communications.Email.SendGrid;

builder.Services.AddIBeamCommunications(builder.Configuration);
builder.Services.AddIBeamSendGridEmail(builder.Configuration);
```

Configuration:

```json
{
  "IBeam": {
    "Communications": {
      "Email": {
        "FromAddress": "noreply@example.com",
        "FromName": "Example App",
        "SendGrid": {
          "ApiKey": "<sendgrid-api-key>",
          "DefaultFromAddress": "noreply@example.com",
          "DefaultFromDisplayName": "Example App",
          "SandboxMode": false
        }
      }
    }
  }
}
```

Send email:

```csharp
public sealed class InvitationService
{
    private readonly IEmailService _email;

    public InvitationService(IEmailService email)
    {
        _email = email;
    }

    public Task SendInviteAsync(string recipient, CancellationToken ct = default)
    {
        var message = new EmailMessage
        {
            Subject = "You are invited",
            HtmlBody = "<p>Open the app to accept your invite.</p>",
            TextBody = "Open the app to accept your invite."
        };
        message.To.Add(recipient);

        return _email.SendAsync(message, ct: ct);
    }
}
```

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `IBeam:Communications:Email:SendGrid:ApiKey` | required | SendGrid API key. Store this securely. |
| `IBeam:Communications:Email:SendGrid:DefaultFromAddress` | required | Provider-level default sender address. |
| `IBeam:Communications:Email:SendGrid:DefaultFromDisplayName` | `null` | Provider-level default sender display name. |
| `IBeam:Communications:Email:SendGrid:SandboxMode` | `false` | Sends to SendGrid sandbox mode so messages are accepted but not delivered. |
| `IBeam:Communications:Email:FromAddress` | empty | Shared sender fallback used by IBeam sender resolution. |
| `IBeam:Communications:Email:FromName` | `null` | Shared sender display-name fallback. |

## Service Operations, Auditing, And Permissions

The SendGrid provider is not the audit boundary. Tag and wrap the consuming service operation.

```csharp
[IBeamOperation("accounts.invite")]
public Task InviteAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default)
    => _operations.ExecuteAsync(
        this,
        token => InviteCoreAsync(tenantId, inviteId, token),
        new ServiceOperationExecutionOptions
        {
            TenantId = tenantId,
            EntityId = inviteId
        },
        ct);
```

Operation names such as `accounts.invite` can later be used by IBeam access-control rules and audit queries.

## Data Storage

This package does not create database tables, Azure Table Storage tables, or local durable stores.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| Email outbox | No | Add an application-specific outbox if retries/history are required. |
| Provider delivery records | No | SendGrid owns provider-side event/activity records. |
| Azure Table Storage tables | No | No schema is owned by this package. |

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| Delivery provider | `IEmailService` | Replace SendGrid with SMTP, ACS, pickup directory, or a custom provider. |
| Template rendering | `IEmailTemplateRenderer` | Render content before it reaches SendGrid. |
| Retry/outbox | Consuming service/repository | Add durable retry behavior outside the provider. |

## Package Relationships

| Package | Relationship |
|---|---|
| `IBeam.Communications` | Shared email abstractions and message models. |
| `IBeam.Communications.Email.SendGrid` | SendGrid email provider implementation. |
| `IBeam.Communications.Email.Templating` | Optional template renderer that sends through `IEmailService`. |

## Extended Examples And Agent Guidance

- Project agent prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Repository implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Startup fails with `ApiKey is required` | Missing SendGrid API key | Configure `IBeam:Communications:Email:SendGrid:ApiKey`. |
| Startup fails with `DefaultFromAddress is required` | Missing provider sender | Configure `DefaultFromAddress` and verify the sender/domain in SendGrid. |
| SendGrid returns 4xx | Invalid request, sender, recipient, or API authorization | Check provider error details and SendGrid account configuration. |
| Messages accepted but not delivered | `SandboxMode` is enabled | Set `SandboxMode` to `false` outside staging/test. |
| No audit row is written | Provider sends are not business operations | Wrap the consuming service method with `IServiceOperationExecutor`. |

## Version Notes

- Targets `net10.0`.
- Uses `SendGrid` and `SendGrid.Extensions.DependencyInjection`.
- Package version is assigned by the repository release workflow.
