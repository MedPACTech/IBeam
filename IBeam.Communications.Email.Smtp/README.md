# IBeam.Communications.Email.Smtp

`IBeam.Communications.Email.Smtp` implements `IEmailService` using the standard SMTP protocol. It is useful when a consuming API already has access to an SMTP relay, enterprise mail server, local dev relay, or provider-neutral SMTP endpoint.

## When To Use This

- Your organization already provides an SMTP relay.
- You want a simple standards-based email provider.
- You want application services to stay on IBeam `IEmailService`.
- You need a provider that can work without SendGrid or Azure Communication Services.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Email provider | `SmtpEmailService` | Sends `EmailMessage` with `System.Net.Mail.SmtpClient`. |
| Provider options | `SmtpEmailOptions` | Holds host, port, SSL, credentials, and default sender settings. |
| DI registration | `AddIBeamSmtpEmail(IConfiguration)` | Registers SMTP options and `IEmailService`. |
| Validation | Startup option validation plus message validation | Catches missing host, invalid port, and missing sender configuration. |
| Error translation | `EmailProviderException`, `EmailValidationException` | Surfaces provider and validation failures through IBeam exception types. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package belongs behind the service layer. A domain service decides when an email should be sent and calls `IEmailService`; this package only delivers that message through SMTP.

## Quick Start

```csharp
using IBeam.Communications.Abstractions;
using IBeam.Communications.Email.Smtp;

builder.Services.AddIBeamCommunications(builder.Configuration);
builder.Services.AddIBeamSmtpEmail(builder.Configuration);
```

Configuration:

```json
{
  "IBeam": {
    "Communications": {
      "Email": {
        "FromAddress": "noreply@example.com",
        "FromName": "Example App",
        "Smtp": {
          "Host": "smtp.example.com",
          "Port": 587,
          "EnableSsl": true,
          "UseDefaultCredentials": false,
          "Username": "smtp-user",
          "Password": "<secret>",
          "DefaultFromAddress": "noreply@example.com",
          "DefaultFromDisplayName": "Example App"
        }
      }
    }
  }
}
```

Send email:

```csharp
public sealed class AlertEmailService
{
    private readonly IEmailService _email;

    public AlertEmailService(IEmailService email)
    {
        _email = email;
    }

    public Task SendAlertAsync(string to, CancellationToken ct = default)
    {
        var message = new EmailMessage
        {
            Subject = "Action needed",
            TextBody = "Please review the latest alert."
        };
        message.To.Add(to);

        return _email.SendAsync(message, ct: ct);
    }
}
```

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `IBeam:Communications:Email:Smtp:Host` | required | SMTP host or relay name. |
| `IBeam:Communications:Email:Smtp:Port` | `587` | SMTP TCP port. Must be between 1 and 65535. |
| `IBeam:Communications:Email:Smtp:EnableSsl` | `true` | Enables SSL/TLS for the SMTP connection. |
| `IBeam:Communications:Email:Smtp:UseDefaultCredentials` | `false` | Uses machine/domain credentials instead of explicit username/password. |
| `IBeam:Communications:Email:Smtp:Username` | `null` | Optional SMTP username. |
| `IBeam:Communications:Email:Smtp:Password` | `null` | Optional SMTP password. Store securely. |
| `IBeam:Communications:Email:Smtp:DefaultFromAddress` | `noreply@localhost` | Provider-level default sender address. |
| `IBeam:Communications:Email:Smtp:DefaultFromDisplayName` | `null` | Provider-level default sender display name. |

Shared fallback sender settings are also bound from `IBeam:Communications:Email`.

## Service Operations, Auditing, And Permissions

SMTP delivery is not the business operation. Tag the service method that decides to send the message.

```csharp
[IBeamOperation("orders.confirmation.send")]
public Task SendConfirmationAsync(Guid tenantId, Guid orderId, CancellationToken ct = default)
    => _operations.ExecuteAsync(
        this,
        token => SendConfirmationCoreAsync(tenantId, orderId, token),
        new ServiceOperationExecutionOptions
        {
            TenantId = tenantId,
            EntityId = orderId
        },
        ct);
```

Use `ILogger<T>` or a consuming outbox service if you need SMTP diagnostics, retries, or delivery tracking.

## Data Storage

This package does not create database tables or durable stores.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| Azure Table Storage tables | No | No schema is owned by this provider. |
| SMTP queue/history | No | The SMTP relay/server may own its own logs. |
| Email outbox | No | Add a consuming-service repository if durable retries are needed. |

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| Email provider | `IEmailService` | Replace SMTP with SendGrid, ACS, pickup directory, or custom delivery. |
| Sender defaults | `EmailOptions`, `SmtpEmailOptions` | Set default sender values globally or provider-specific values. |
| Retry/outbox | Consuming service/repository | Add durable retry and tracking outside this transport. |

## Package Relationships

| Package | Relationship |
|---|---|
| `IBeam.Communications` | Shared email contracts and options. |
| `IBeam.Communications.Email.Smtp` | SMTP provider implementation. |
| `IBeam.Communications.Email.Templating` | Optional template rendering before SMTP delivery. |

## Extended Examples And Agent Guidance

- Project agent prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Repository implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Startup fails with missing host | `Host` is not configured | Set `IBeam:Communications:Email:Smtp:Host`. |
| Authentication fails | Wrong username/password or relay policy | Verify credentials and relay rules. |
| TLS/SSL connection fails | Relay does not support the configured SSL setting | Adjust `EnableSsl` or relay port. |
| Messages send from unexpected address | Sender fallback order is different than expected | Check message sender, call-level `EmailOptions`, shared email defaults, and provider defaults. |
| No audit row is written | Provider sends are not business operations | Wrap the consuming service method with `IServiceOperationExecutor`. |

## Version Notes

- Targets `net10.0`.
- Uses `System.Net.Mail.SmtpClient` and creates a new client per send.
- Package version is assigned by the repository release workflow.
