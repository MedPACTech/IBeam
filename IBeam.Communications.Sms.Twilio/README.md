# IBeam.Communications.Sms.Twilio

`IBeam.Communications.Sms.Twilio` is reserved for a future Twilio-backed `ISmsService` provider. The current project is scaffolded only and does not yet contain a production SMS implementation.

## When To Use This

Do not use this package for production SMS delivery yet.

Use this project when:

- You are implementing the Twilio provider for IBeam.
- You need a package placeholder to preserve naming and package structure.
- You are reviewing provider boundaries before adding real Twilio delivery code.

For production SMS today, use `IBeam.Communications.Sms.AzureCommunications` or provide a custom `ISmsService` implementation.

## Current Contents

| Area | Type(s) | Purpose |
|---|---|---|
| Placeholder | `Class1` | Empty scaffold type. |
| Package shell | `IBeam.Communications.Sms.Twilio.csproj` | Reserves the package name and target framework. |

## Intended Architecture Fit

When implemented, this package should follow the same IBeam communications provider pattern:

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

Expected boundaries:

- Domain services call `ISmsService`.
- This package implements Twilio SMS transport only.
- Controllers do not call Twilio SDKs directly.
- Repositories do not send SMS.
- Business rules, permissions, and audit operation names stay in consuming services.

## Intended Public Surface

A future complete implementation should likely include:

| Area | Expected Type(s) | Purpose |
|---|---|---|
| SMS provider | `TwilioSmsService : ISmsService` | Sends `SmsMessage` through Twilio. |
| Provider options | `TwilioSmsOptions` | Holds account SID, auth token/API key, messaging service SID or from number. |
| DI registration | `AddIBeamTwilioSms(IConfiguration)` | Registers Twilio options and `ISmsService`. |
| Validation | options/message validation | Fails fast on missing credentials or sender configuration. |
| Error translation | `SmsProviderException`, `SmsConfigurationException` | Converts Twilio errors to IBeam exceptions. |

## Proposed Configuration

This configuration does not exist yet. It is a proposed shape for future implementation:

```json
{
  "IBeam": {
    "Communications": {
      "Sms": {
        "FromPhoneNumber": "+16145551212",
        "Twilio": {
          "AccountSid": "<twilio-account-sid>",
          "AuthToken": "<secret>",
          "MessagingServiceSid": "<optional-messaging-service-sid>",
          "DefaultFromPhoneNumber": "+16145551212"
        }
      }
    }
  }
}
```

## Proposed Usage

This API does not exist yet. It shows the expected provider pattern:

```csharp
using IBeam.Communications.Abstractions;
using IBeam.Communications.Sms.Twilio;

builder.Services.AddIBeamCommunications(builder.Configuration);
builder.Services.AddIBeamTwilioSms(builder.Configuration);
```

Consuming services should continue to depend on `ISmsService`:

```csharp
public sealed class ReminderSmsService
{
    private readonly ISmsService _sms;

    public ReminderSmsService(ISmsService sms)
    {
        _sms = sms;
    }

    public Task SendReminderAsync(string phoneNumber, CancellationToken ct = default)
    {
        var message = new SmsMessage { Body = "Reminder: your appointment is tomorrow." };
        message.To.Add(phoneNumber);

        return _sms.SendAsync(message, ct: ct);
    }
}
```

## Service Operations, Auditing, And Permissions

When Twilio support is implemented, keep audit and permission boundaries in the consuming service method, not in the Twilio transport.

```csharp
[IBeamOperation("appointments.reminder.send")]
public Task SendReminderAsync(Guid tenantId, Guid appointmentId, CancellationToken ct = default)
    => _operations.ExecuteAsync(
        this,
        token => SendReminderCoreAsync(tenantId, appointmentId, token),
        new ServiceOperationExecutionOptions
        {
            TenantId = tenantId,
            EntityId = appointmentId
        },
        ct);
```

## Data Storage

This package currently creates no stores. A future Twilio provider should also avoid owning application storage.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| Azure Table Storage tables | No | No schema is owned by this package. |
| SMS outbox/history | No | Add a consuming-service repository if durable retry/history is required. |
| Twilio delivery records | No | Twilio owns provider-side records. |

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| SMS delivery | `ISmsService` | Implement Twilio or replace with another provider. |
| Provider options | future `TwilioSmsOptions` | Configure credentials and sender behavior. |
| Retry/outbox | consuming service/repository | Add durable retry and status tracking outside the provider. |

## Package Relationships

| Package | Relationship |
|---|---|
| `IBeam.Communications` | Owns `ISmsService`, `SmsMessage`, `SmsOptions`, validation, and exceptions. |
| `IBeam.Communications.Sms.Twilio` | Reserved Twilio SMS provider package. |
| `IBeam.Communications.Sms.AzureCommunications` | Current implemented ACS SMS provider. |

## Extended Examples And Agent Guidance

- Project agent prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Repository implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

Agents implementing this package should first replace the placeholder with a real `ISmsService` implementation, add options validation, add DI registration, and add provider tests.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| `ISmsService` is not registered by this package | Twilio provider is not implemented yet | Use Azure SMS or register a custom `ISmsService`. |
| No Twilio options exist | Package is scaffold-only | Add `TwilioSmsOptions` as part of implementation. |
| No provider tests exist | Package is scaffold-only | Add unit tests and opt-in integration tests when implementation begins. |

## Version Notes

- Targets `net10.0`.
- Current status: scaffold/reserved package, not production-ready.
- Package version is assigned by the repository release workflow.
