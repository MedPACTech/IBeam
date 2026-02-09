# IBeam Communications – Email (SendGrid)

This project provides the **email delivery layer** for the IBeam ecosystem using **SendGrid**.  
It is designed as a modular communications component that can be reused across IBeam services for transactional and notification-based email delivery.

---

## ✨ Features

- SendGrid-based email delivery
- Strongly-typed configuration via `application.json`
- Support for default sender address and display name
- Designed for dependency injection
- Easily extensible for templates, attachments, and future providers

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Email Provider:** SendGrid
- **Configuration:** JSON-based (`application.json`)
- **Patterns:** Options Pattern, DI-friendly services

---

## 📁 Project Structure

IBeam.Communications.Email.SendGrid/
├── application.json
├── Services/
│ ├── SendGridEmailService.cs
│ └── Interfaces/
│ └── IEmailService.cs
├── Models/
│ └── EmailMessage.cs
└── Extensions/
└── ServiceCollectionExtensions.cs


---

## ⚙️ Configuration

Email configuration is stored in `application.json` and bound using the Options pattern.

Key configuration values include:

- SendGrid API Key
- Default "From" email address
- Default display name

---

## 🚀 Usage

1. Add the project as a reference to your API or worker service
2. Register the email services during startup
3. Inject `IEmailService` where needed
4. Send emails using the provided service abstraction

---

## 🔐 Security Notes

- **Never** commit real API keys to source control
- Store secrets securely using environment variables or a secret manager
- `application.json` values should be overridden per environment

---

## 🧩 Part of the IBeam Platform

This service is a foundational communications component within the broader **IBeam** ecosystem and is intended to remain provider-agnostic where possible.

---

## 📄 License

Internal use only — IBeam platform component.
