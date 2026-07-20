# IBeam.Communications.Email.Templating

`IBeam.Communications.Email.Templating` adds file-based email template rendering and a templated email service on top of `IBeam.Communications`. It lets application services send named templates while staying on the provider-neutral `IEmailService` boundary.

## When To Use This

- You want email bodies to live in template files instead of service code.
- You want the same template flow to work with SMTP, SendGrid, Azure Communication Services, or pickup-directory email.
- You need simple token replacement for small templates.
- You want an easy renderer to replace later with Razor, Liquid, Handlebars, or a database-backed template store.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Template renderer | `FileEmailTemplateRenderer` | Reads `.html` and `.txt` template files from a configured base path. |
| Templated email service | `TemplatedEmailService` | Renders a template and sends the resulting email through `IEmailService`. |
| Registration | `AddIBeamEmailTemplatingFromFiles(IConfiguration)` | Registers the file renderer and templated email service. |
| Shared contracts | `IEmailTemplateRenderer`, `ITemplatedEmailService`, `EmailTemplateOptions` | Provided by `IBeam.Communications` and implemented here. |

## Architecture Fit

This package is a service helper, not an API or repository package.

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

The consuming domain service decides why an email is sent. This package only handles how a named template becomes an email body and how that body is forwarded to the registered email provider.

## Quick Start

Register base communications, an email provider, and file templating:

```csharp
using IBeam.Communications.Abstractions;
using IBeam.Communications.Email.SendGrid;
using IBeam.Communications.Email.Templating;

builder.Services.AddIBeamCommunications(builder.Configuration);
builder.Services.AddIBeamSendGridEmail(builder.Configuration);
builder.Services.AddIBeamEmailTemplatingFromFiles(builder.Configuration);
```

Configuration:

```json
{
  "IBeam": {
    "EmailTemplating": {
      "BasePath": "Templates/Email",
      "HtmlExtension": ".html",
      "TextExtension": ".txt"
    }
  }
}
```

Create template files:

```text
Templates/Email/welcome.html
Templates/Email/welcome.txt
```

Use the templated service:

```csharp
public sealed class WelcomeEmailService
{
    private readonly ITemplatedEmailService _templates;

    public WelcomeEmailService(ITemplatedEmailService templates)
    {
        _templates = templates;
    }

    public Task SendWelcomeAsync(string email, string displayName, CancellationToken ct = default)
        => _templates.SendTemplatedEmailAsync(
            [email],
            "Welcome",
            "welcome",
            displayName,
            ct: ct);
}
```

The file renderer performs simple replacement:

- `object[]` models can fill `{0}`, `{1}`, and so on.
- Any other model fills `{0}` with `model.ToString()`.

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `IBeam:EmailTemplating:BasePath` | required | Directory containing template files. |
| `IBeam:EmailTemplating:HtmlExtension` | `.html` | Extension appended when looking for HTML templates. |
| `IBeam:EmailTemplating:TextExtension` | `.txt` | Extension appended when looking for text templates. |

At least one matching template file must exist for the requested template name. If neither the HTML nor text file exists, the renderer throws `EmailTemplateNotFoundException`.

## Service Operations, Auditing, And Permissions

This package does not own audit or permission decisions. Wrap the consuming service method that sends the templated email.

```csharp
[IBeamOperation("accounts.invite")]
public Task InviteAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    => _operations.ExecuteAsync(
        this,
        token => InviteCoreAsync(tenantId, userId, token),
        new ServiceOperationExecutionOptions
        {
            TenantId = tenantId,
            EntityId = userId
        },
        ct);

private Task InviteCoreAsync(Guid tenantId, Guid userId, CancellationToken ct)
    => _templates.SendTemplatedEmailAsync(
        ["new.user@example.com"],
        "You have been invited",
        "tenant-invite",
        tenantId,
        ct: ct);
```

Do not tag `TemplatedEmailService` as if it were the business operation. Tag the domain operation that caused the email.

## Data Storage

This package does not create database tables or Azure Table Storage schemas.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| Template files | No | The package reads template files from a path owned by the host app. |
| Email history/outbox | No | Add a consuming-service repository if durable tracking is required. |
| Azure Table Storage tables | No | No table schema is owned by this package. |

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| Template rendering | `IEmailTemplateRenderer` | Add a richer rendering engine, localization, database-backed templates, or theme selection. |
| Templated email flow | `ITemplatedEmailService` | Add tracking, tenant-specific template selection, or pre-send policies. |
| Delivery provider | `IEmailService` | Swap SMTP, SendGrid, Azure Communication Services, pickup directory, or a custom provider. |

## Package Relationships

| Package | Relationship |
|---|---|
| `IBeam.Communications` | Owns the templating interfaces and shared options. |
| `IBeam.Communications.Email.Templating` | Implements file-template rendering and render-plus-send orchestration. |
| Email provider packages | Provide the `IEmailService` used after rendering. |

## Extended Examples And Agent Guidance

- Project agent prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Repository implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| `EmailTemplateOptions.BasePath is not configured` | Missing `IBeam:EmailTemplating:BasePath` | Add the base path to configuration. |
| `EmailTemplateNotFoundException` | No `.html` or `.txt` file for the template name | Add the template file or check the configured extensions. |
| Template name rejected | Name contains invalid filename characters or `..` | Use a simple template key such as `welcome` or `tenant-invite`. |
| Render succeeds but send fails | Email provider is missing or misconfigured | Register and configure an `IEmailService` provider. |

## Version Notes

- Targets `net10.0`.
- Uses simple file rendering today; richer renderers should replace `IEmailTemplateRenderer`.
