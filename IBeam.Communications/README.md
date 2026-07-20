# IBeam.Communications

`IBeam.Communications` is the provider-neutral communications foundation for IBeam. It defines the email, SMS, template, option, validation, and exception contracts that application services use without depending directly on SMTP, SendGrid, Azure Communication Services, Twilio, or another delivery provider.

## When To Use This

- You want application services to send email or SMS through stable IBeam interfaces.
- You want to swap communication providers without rewriting business services.
- You need shared message models, default sender options, and validation behavior.
- You want templated email support that can be paired with any `IEmailService` provider.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Email contract | `IEmailService` | Sends `EmailMessage` instances through the registered provider. |
| SMS contract | `ISmsService` | Sends `SmsMessage` instances through the registered provider. |
| Templated email | `ITemplatedEmailService`, `IEmailTemplateRenderer` | Renders named templates and sends them through `IEmailService`. |
| Message models | `EmailMessage`, `EmailAddress`, `SmsMessage`, `RenderedEmailTemplate` | Provider-neutral message and rendered-template shapes. |
| Options | `EmailOptions`, `SmsOptions`, `EmailTemplateOptions` | Shared sender defaults and template location settings. |
| Validation | `EmailMessageValidator`, `SmsMessageValidator`, option validators | Checks required message fields before provider delivery. |
| Exceptions | `EmailValidationException`, `EmailConfigurationException`, `EmailProviderException`, `SmsValidationException`, `SmsConfigurationException`, `SmsProviderException`, template exceptions | Gives consuming services predictable exception types. |
| DI registration | `AddIBeamCommunications(IConfiguration)` | Registers shared communication options and file-template services. |

## Architecture Fit

IBeam keeps application code on this path:

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package belongs at the abstraction/model layer. Controllers should not call provider SDKs directly. Domain services should decide when a communication should be sent, then call `IEmailService`, `ISmsService`, or `ITemplatedEmailService`.

Provider packages implement the transport boundary. Repositories are not involved unless a consuming service stores its own notification/audit entity.

## Quick Start

Register shared communications first, then register one concrete email/SMS provider package.

```csharp
using IBeam.Communications.Abstractions;
using IBeam.Communications.Email.Smtp;

builder.Services.AddIBeamCommunications(builder.Configuration);
builder.Services.AddIBeamSmtpEmail(builder.Configuration);
```

Send email from a service:

```csharp
public sealed class WelcomeMessageService
{
    private readonly IEmailService _email;

    public WelcomeMessageService(IEmailService email)
    {
        _email = email;
    }

    public Task SendWelcomeAsync(string emailAddress, CancellationToken ct = default)
    {
        var message = new EmailMessage
        {
            Subject = "Welcome",
            TextBody = "Your account is ready."
        };
        message.To.Add(emailAddress);

        return _email.SendAsync(message, ct: ct);
    }
}
```

Send SMS from a service:

```csharp
public sealed class SmsNotificationService
{
    private readonly ISmsService _sms;

    public SmsNotificationService(ISmsService sms)
    {
        _sms = sms;
    }

    public Task SendCodeAsync(string phoneNumber, string code, CancellationToken ct = default)
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

Shared configuration sections:

```json
{
  "IBeam": {
    "Communications": {
      "Email": {
        "FromAddress": "noreply@example.com",
        "FromName": "Example App"
      },
      "Sms": {
        "FromPhoneNumber": "+16145551212",
        "DefaultToUs": true
      }
    },
    "EmailTemplating": {
      "BasePath": "Templates/Email",
      "HtmlExtension": ".html",
      "TextExtension": ".txt"
    }
  }
}
```

| Setting | Default | Purpose |
|---|---:|---|
| `IBeam:Communications:Email:FromAddress` | empty | Default email sender address used when the message/provider does not provide one. |
| `IBeam:Communications:Email:FromName` | `null` | Optional default email sender display name. |
| `IBeam:Communications:Sms:FromPhoneNumber` | empty | Default SMS sender phone number. E.164 format is recommended. |
| `IBeam:Communications:Sms:DefaultToUs` | `true` | Shared SMS normalization hint used by communication policies. |
| `IBeam:EmailTemplating:BasePath` | `null` | Directory containing template files. |
| `IBeam:EmailTemplating:HtmlExtension` | `.html` | File extension for HTML templates. |
| `IBeam:EmailTemplating:TextExtension` | `.txt` | File extension for text templates. |

## Service Operations, Auditing, And Permissions

This package does not define business service operations directly. The consuming service that decides to send a message should own the audit and permission operation name.

```csharp
using IBeam.Services.Abstractions;

[IBeamOperation("patients.discharge")]
public Task DischargeAsync(Guid tenantId, Guid patientId, CancellationToken ct = default)
    => _operations.ExecuteAsync(
        this,
        token => DischargeCoreAsync(tenantId, patientId, token),
        new ServiceOperationExecutionOptions
        {
            TenantId = tenantId,
            EntityId = patientId
        },
        ct);

private async Task DischargeCoreAsync(Guid tenantId, Guid patientId, CancellationToken ct)
{
    // Business rules and data changes happen here.
    await _email.SendAsync(BuildDischargeEmail(patientId), ct: ct);
}
```

Keep communication providers focused on delivery. Do not put patient, billing, identity, or workflow permission rules inside provider implementations.

## Data Storage

This package does not create database tables, Azure Table Storage tables, queues, containers, or durable stores. If a consuming app needs message history, notification outbox rows, retries, or delivery receipts, that should be modeled in the consuming service and repository package.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| Azure Table Storage tables | No | No table schema is owned by this package. |
| Email/SMS outbox | No | Add an application-specific outbox if durable delivery is required. |
| Template files | No | Reads files from the configured template directory when templating is enabled. |

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| Email delivery | `IEmailService` | Use SMTP, SendGrid, Azure Communication Services, pickup directory, or a custom provider. |
| SMS delivery | `ISmsService` | Use Azure Communication Services, Twilio, or a custom provider. |
| Template rendering | `IEmailTemplateRenderer` | Replace simple file formatting with Razor, Liquid, Handlebars, database templates, or a hosted renderer. |
| Templated email orchestration | `ITemplatedEmailService` | Add application-specific template selection, localization, or tracking. |

## Package Relationships

| Package | Relationship |
|---|---|
| `IBeam.Communications` | Shared abstractions, models, options, validators, template interfaces. |
| `IBeam.Communications.Email.Templating` | File-based template renderer and templated email orchestration. |
| `IBeam.Communications.Email.Smtp` | SMTP `IEmailService` provider. |
| `IBeam.Communications.Email.SendGrid` | SendGrid `IEmailService` provider. |
| `IBeam.Communications.Email.AzureCommunications` | Azure Communication Services email provider. |
| `IBeam.Communications.Email.PickupDirectory` | Local/test `.eml` pickup-directory provider. |
| `IBeam.Communications.Sms.AzureCommunications` | Azure Communication Services SMS provider. |
| `IBeam.Communications.Sms.Twilio` | Reserved Twilio SMS provider package; currently scaffolded. |

## Extended Examples And Agent Guidance

- Project agent prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Repository implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)
- Builder onboarding prompt: [`../IBeam.AI.Enablement/examples/builder-onboarding-prompt.md`](../IBeam.AI.Enablement/examples/builder-onboarding-prompt.md)

Agents should read the project prompt and root implementation guide before changing provider boundaries or adding service operation/audit behavior.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| `IEmailService` cannot be resolved | No email provider package registered | Register one provider, such as `AddIBeamSmtpEmail`, `AddIBeamSendGridEmail`, `AddIBeamAzureCommunicationsEmail`, or `AddIBeamPickupDirectoryEmail`. |
| `ISmsService` cannot be resolved | No SMS provider package registered | Register an SMS provider such as `AddIBeamCommunicationsSmsAzure`. |
| Options validation fails on startup | Missing default sender settings | Configure `IBeam:Communications:Email` or `IBeam:Communications:Sms`. |
| Provider sends from the wrong address | Message-level or call-level options override defaults | Check `EmailMessage.FromAddress`, call-level `EmailOptions`, shared `EmailOptions`, and provider defaults. |
| No audit row is written for a send | The communication provider is not the business operation | Wrap the consuming service method with `IServiceOperationExecutor` and tag it with `[IBeamOperation]`. |

## Version Notes

- Targets `net10.0`.
- Packages are versioned by the repository release workflow.
- Provider implementations should remain replaceable behind these abstractions.
