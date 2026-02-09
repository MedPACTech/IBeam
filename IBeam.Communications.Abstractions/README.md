# IBeam.Communications.Abstractions

Shared abstractions for sending email within the **IBeam** ecosystem.

This library is intentionally **provider-agnostic**: it defines the contracts and transport models that application code depends on, while concrete provider packages (e.g., Azure Communication Services Email, SMTP, SendGrid, etc.) implement the actual delivery.

## Package contents

Namespace: `IBeam.Communications.Email.Abstractions`

### Core contract
- **`IEmailService`**
  - `Task SendAsync(EmailMessage message, EmailSendOptions? options = null, CancellationToken ct = default)`

### Transport models
- **`EmailMessage`**: recipients, subject, optional text/html bodies, optional sender
- **`EmailAddress`**: email address + optional display name
- **`EmailSendOptions`**:
  - `UseDefaultFromIfMissing` (default **true**): allows providers to use configured default sender when `EmailMessage.From` is null
  - `FromOverride`: per-call override that wins over `message.From` and provider defaults

### Validation + errors
- **`EmailDefaults.ValidateMessageForSend(providerName, message)`**
  - Ensures:
    - at least one recipient
    - subject is present
    - at least one body (`TextBody` or `HtmlBody`) is present
  - Throws **`EmailValidationException`** (inherits `EmailServiceException`)

## Typical usage

### 1) Register a concrete provider implementation
This library does not include an implementation. In your application, reference a provider package that implements `IEmailService` and registers it with DI.

Example (pseudo-code):

```csharp
// builder.Services.AddAzureCommunicationsEmail(builder.Configuration);
// builder.Services.AddSingleton<IEmailService, AzureCommunicationsEmailService>();
```

### 2) Send an email

```csharp
using IBeam.Communications.Email.Abstractions;

public sealed class WelcomeEmailSender
{
    private readonly IEmailService _email;

    public WelcomeEmailSender(IEmailService email) => _email = email;

    public Task SendWelcomeAsync(string toEmail, CancellationToken ct)
    {
        var message = new EmailMessage(
            To: new[] { new EmailAddress(toEmail) },
            Subject: "Welcome to IBeam",
            TextBody: "Thanks for signing up!"
        );

        // Provider may supply default 'From' if missing.
        return _email.SendAsync(message, ct: ct);
    }
}
```

### 3) Optional per-call sender override

```csharp
await _email.SendAsync(
    message,
    options: new EmailSendOptions
    {
        FromOverride = new EmailAddress("DoNotReply@yourdomain.com", "IBeam Notifications")
    },
    ct);
```

## Configuration

Provider packages typically bind configuration from the `IBeam:Communications:Email` section.

See `application.json` (included in this repo) for an example configuration based on **Azure Communication Services Email**.

## Target framework
- `net10.0`

## License
Internal / project-specific.
