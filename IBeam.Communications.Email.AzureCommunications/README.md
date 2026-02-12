# IBeam.Communications.Email.AzureCommunications

`IBeam.Communications.Email.AzureCommunications` provides an **Azure Communication Services (ACS) Email** implementation for the IBeam communications framework. It plugs into the IBeam email abstraction layer and enables secure, configurable email delivery using Azure’s managed email infrastructure.

---

## ✨ Features

- Azure Communication Services Email integration
- Implements IBeam email sender abstractions
- Strongly-typed configuration via Options pattern
- Environment-safe configuration (no hardcoded secrets)
- Designed for extensibility and testability
- Production-ready for regulated environments

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Azure Service:** Azure Communication Services – Email
- **Configuration:** Microsoft.Extensions.Options
- **DI:** Microsoft.Extensions.DependencyInjection

---

## 📦 Package Purpose

This package is a **concrete email provider** for the IBeam platform.

It is responsible only for:
- Sending emails via Azure Communication Services
- Respecting IBeam email contracts
- Reading provider-specific configuration

It does **not**:
- Define business rules
- Decide when emails are sent
- Handle OTP or workflow logic directly

---

## 📁 Project Structure

IBeam.Communications.Email.AzureCommunications/
├── Options/
│ └── AzureCommunicationsEmailOptions.cs
├── Senders/
│ └── AzureCommunicationsEmailSender.cs
├── Extensions/
│ └── ServiceCollectionExtensions.cs
└── IBeam.Communications.Email.AzureCommunications.csproj


---

## ⚙️ Configuration

Add the following section to your application configuration:

```json
{
  "IBeam": {
    "Communications": {
      "Email": {
        "AzureCommunications": {
          "ConnectionString": "endpoint=https://...;accesskey=...",
          "DefaultFromAddress": "DoNotReply@yourdomain.com",
          "DefaultFromDisplayName": "IBeam Notifications"
        }
      }
    }
  }
}

public sealed class AzureCommunicationsEmailOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string DefaultFromAddress { get; init; } = string.Empty;
    public string DefaultFromDisplayName { get; init; } = string.Empty;
}

🔌 Dependency Injection

Register the provider during application startup:

builder.Services.AddAzureCommunicationsEmail(
    builder.Configuration);


This will:
- Bind configuration
- Register the Azure email sender
- Wire the sender into the IBeam email abstraction

📤 Email Sending

The Azure Communications sender implements the IBeam email sender contract and is invoked automatically by higher-level services (e.g., OTP delivery, notifications).

Consumers should depend on IBeam email abstractions, not this package directly.

🔐 Security Notes
- Store the ACS connection string in secure configuration (Key Vault, environment variables, etc.)
- Do not commit secrets to source control
- Supports rotation via IOptionsMonitor

🚀 Usage in IBeam
This package is used by:

- Identity & OTP delivery
- System notifications
- Platform messaging workflows

It is designed to be swappable with other providers (SMTP, SendGrid, SES) without changing calling code.

🧭 Design Philosophy
- Provider-specific logic lives in provider packages
- Configuration is explicit and strongly typed
- Abstractions live upstream
- No hidden magic, no static dependencies

📄 License

Copyright © MedPAC Technologies, LLC
Part of the IBeam Platform