# IBeam.Communications.Email.PickupDirectory

`IBeam.Communications.Email.PickupDirectory` implements `IEmailService` by writing `.eml` email files to a local pickup directory. It is intended for local development, integration tests, QA inspection, and environments where you want to verify generated email without sending it to real recipients.

## When To Use This

- You want to inspect email output without delivering it.
- You need a simple local/test email provider.
- You want automated tests to verify generated `.eml` files.
- You are developing templates and want fast feedback.

Do not treat this package as a production email delivery provider.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Email provider | `PickupDirectoryEmailService` | Writes `EmailMessage` as mail files through `SmtpClient` pickup-directory delivery. |
| Provider options | `PickupDirectoryEmailOptions` | Holds directory path and default sender settings. |
| DI registration | `AddIBeamPickupDirectoryEmail(IConfiguration)` | Registers pickup-directory options and `IEmailService`. |
| Validation | Startup option validation plus message validation | Catches missing directory/sender settings and invalid messages. |
| Error translation | `EmailProviderException`, `EmailConfigurationException`, `EmailValidationException` | Surfaces filesystem and validation failures through IBeam exception types. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This provider belongs behind the service layer. It should not contain business decisions. Consuming services decide when email should be sent, and this provider writes the message to disk for inspection.

## Quick Start

```csharp
using IBeam.Communications.Abstractions;
using IBeam.Communications.Email.PickupDirectory;

builder.Services.AddIBeamCommunications(builder.Configuration);
builder.Services.AddIBeamPickupDirectoryEmail(builder.Configuration);
```

Configuration:

```json
{
  "IBeam": {
    "Communications": {
      "Email": {
        "FromAddress": "noreply@example.test",
        "FromName": "Local Test App",
        "PickupDirectory": {
          "DirectoryPath": "artifacts/email",
          "DefaultFromAddress": "noreply@example.test",
          "DefaultFromDisplayName": "Local Test App"
        }
      }
    }
  }
}
```

Send email:

```csharp
public sealed class LocalEmailPreviewService
{
    private readonly IEmailService _email;

    public LocalEmailPreviewService(IEmailService email)
    {
        _email = email;
    }

    public Task WritePreviewAsync(string to, CancellationToken ct = default)
    {
        var message = new EmailMessage
        {
            Subject = "Preview",
            HtmlBody = "<p>This message will be written as an .eml file.</p>"
        };
        message.To.Add(to);

        return _email.SendAsync(message, ct: ct);
    }
}
```

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `IBeam:Communications:Email:PickupDirectory:DirectoryPath` | required | Directory where `.eml` files are written. Created automatically when possible. |
| `IBeam:Communications:Email:PickupDirectory:DefaultFromAddress` | required | Provider-level default sender address. |
| `IBeam:Communications:Email:PickupDirectory:DefaultFromDisplayName` | `null` | Provider-level default sender display name. |
| `IBeam:Communications:Email:FromAddress` | empty | Shared sender fallback used by IBeam sender resolution. |
| `IBeam:Communications:Email:FromName` | `null` | Shared sender display-name fallback. |

## Service Operations, Auditing, And Permissions

Pickup-directory writes are still provider delivery, not the business operation. Tag the consuming service method that generates the email.

```csharp
[IBeamOperation("notifications.preview.generate")]
public Task GeneratePreviewAsync(Guid tenantId, Guid notificationId, CancellationToken ct = default)
    => _operations.ExecuteAsync(
        this,
        token => GeneratePreviewCoreAsync(tenantId, notificationId, token),
        new ServiceOperationExecutionOptions
        {
            TenantId = tenantId,
            EntityId = notificationId
        },
        ct);
```

## Data Storage

This package writes `.eml` files to the configured directory. It does not create database tables.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| Pickup directory | Yes, if missing | Created with `Directory.CreateDirectory`. |
| `.eml` files | Yes | Files are written by `SmtpClient` pickup-directory delivery. |
| Azure Table Storage tables | No | No table schema is owned by this package. |
| Email outbox/history | No | Add an application repository if durable tracking is needed. |

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| Email provider | `IEmailService` | Replace pickup directory with SMTP, SendGrid, ACS, or custom delivery. |
| Directory configuration | `PickupDirectoryEmailOptions` | Point local/test output to a stable inspection folder. |
| Template rendering | `IEmailTemplateRenderer` | Generate `.eml` output from templates for local QA. |

## Package Relationships

| Package | Relationship |
|---|---|
| `IBeam.Communications` | Shared email contracts, models, and sender defaults. |
| `IBeam.Communications.Email.PickupDirectory` | Local/test email provider implementation. |
| `IBeam.Communications.Email.Templating` | Optional template renderer useful with pickup output. |

## Extended Examples And Agent Guidance

- Project agent prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Repository implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Startup fails with `DirectoryPath is required` | Missing pickup directory setting | Configure `IBeam:Communications:Email:PickupDirectory:DirectoryPath`. |
| Files are not written | Directory is wrong or process lacks write access | Check absolute resolved path and process permissions. |
| Sender validation fails | Missing default sender | Configure pickup-directory and/or shared email sender defaults. |
| Test files pile up | Directory is not cleaned between test runs | Clean the configured folder as part of test setup. |

## Version Notes

- Targets `net10.0`.
- Intended for development/test/QA inspection, not production delivery.
- Package version is assigned by the repository release workflow.
