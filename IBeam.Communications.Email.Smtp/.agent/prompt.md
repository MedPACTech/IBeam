# IBeam.Communications.Email.Smtp Agent Prompt

You are working inside `IBeam.Communications.Email.Smtp`, the SMTP email provider package for IBeam communications.

Start with the root implementation guide at `.agent/implementation-guide.md`, then apply this package-specific guidance.

## IBeam Architecture Rules

Preserve the core IBeam boundary model:

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

API projects should stay thin. APIs call services, capture service results, and use the IBeam base controller/response/error patterns.

Services are the gatekeepers. Business logic, permissions, audit tags, validation, orchestration, logging, and error translation belong in services. Services should be entity-focused and normally bind to one repository. A service may call another service for lookup data or rule evaluation, but watch for circular references.

Repositories are data-access components for one entity. Repositories should not call APIs, services, or other repositories.

Use stable operation names for service calls when this package adds service-level operations, such as `email.send`, `email.preview`, or `email.template.render`. Align operation names with audit and permission rules when possible.

## Package Purpose

This package is an SMTP provider adapter for the shared `IEmailService` abstraction. It should let an IBeam application send email through an SMTP relay while keeping the calling application tied to IBeam communications abstractions, not SMTP-specific code.

This is not an API package, identity package, repository package, or template rendering package. Do not add controllers, database repositories, identity dependencies, or templating engines here unless the package purpose changes.

## Public Surface

Keep the main public entry points clear and stable:

- `AddIBeamSmtpEmail(this IServiceCollection services, IConfiguration configuration)`
- `SmtpEmailOptions`
- `SmtpEmailService : IEmailService`

Applications should be able to register this provider with:

```csharp
builder.Services.AddIBeamSmtpEmail(builder.Configuration);
```

When an application uses broader communication services, registration should still leave callers depending on `IEmailService`:

```csharp
public sealed class NotificationService
{
    private readonly IEmailService _email;

    public NotificationService(IEmailService email)
    {
        _email = email;
    }

    public Task SendWelcomeEmailAsync(string email, CancellationToken ct)
    {
        return _email.SendAsync(
            new EmailMessage
            {
                To = [email],
                Subject = "Welcome",
                TextBody = "Your account is ready."
            },
            ct: ct);
    }
}
```

## Configuration

SMTP settings live under:

```text
IBeam:Communications:Email:Smtp
```

The package also consumes shared email defaults through `EmailOptions` from the communications abstractions when resolving sender information.

Expected configuration shape:

```json
{
  "IBeam": {
    "Communications": {
      "Email": {
        "Smtp": {
          "Host": "smtp.yourdomain.com",
          "Port": 587,
          "EnableSsl": true,
          "UseDefaultCredentials": false,
          "Username": "smtp-username",
          "Password": "smtp-password",
          "DefaultFromAddress": "DoNotReply@yourdomain.com",
          "DefaultFromDisplayName": "IBeam Notifications"
        }
      }
    }
  }
}
```

When adding settings:

- Add the property to `SmtpEmailOptions`.
- Bind it in the existing options registration if needed.
- Add startup validation for settings required to send.
- Update `README.md` and `application.json`.
- Avoid duplicating shared sender/default behavior already handled by `EmailOptions`.

## Implementation Rules

Keep `SmtpEmailService` stateless and singleton-safe. It may hold options, but it must not hold per-message state.

Create and dispose `MailMessage` and `SmtpClient` per send unless the provider is redesigned with a tested connection-management strategy.

Validate outbound messages through `EmailMessageValidator.Validate(message)`.

Resolve sender data through `SenderResolution.ResolveEmailFrom(...)` so package behavior remains consistent with other email providers.

Use IBeam communications exceptions:

- Invalid input should surface as `EmailValidationException`.
- Missing or invalid provider configuration should surface as `EmailConfigurationException` or startup options validation.
- SMTP transport failures should be wrapped in `EmailProviderException` with the provider name `SmtpEmailService`.

Honor `CancellationToken` before and after SMTP calls. `SmtpClient.SendMailAsync` does not accept a cancellation token in the current implementation, so do not pretend the in-flight SMTP call can be cancelled unless the implementation changes.

Do not put audit, access-control decisions, or API response shaping in this provider. Those belong in the calling service/API layer.

## Dependencies

This package should stay narrow:

- It may reference `IBeam.Communications`.
- It may use Microsoft configuration/options abstractions.
- It should not reference IBeam Identity, API projects, repositories, Azure Table packages, EF packages, or application-specific projects.

If a feature needs templates, use or extend the shared communications templating abstractions instead of building a provider-specific template engine here.

## Testing Guidance

Unit tests should live in the matching test project, such as `IBeam.Tests.Communications.Email.Smtp`.

Prefer tests that verify:

- Options validation fails for missing host, invalid port, or missing default sender.
- Message validation is honored.
- Text and HTML body behavior is correct.
- Sender resolution follows shared `EmailOptions` rules.
- SMTP failures are wrapped as `EmailProviderException`.

Avoid tests that require a real SMTP server by default. If integration tests are added, make them opt-in through explicit configuration so normal builds remain reliable.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep this prompt useful for agents consuming the package from source or from the package artifact.
