# IBeam Communications – SMS (Twilio)

The **IBeam.Communications.Sms.Twilio** project provides SMS messaging capabilities for the IBeam platform using **Twilio** as the delivery provider. It is designed to plug into the broader IBeam communications framework and supports environment-based configuration.

---

## ✨ Features

- Twilio-based SMS delivery
- Clean abstraction for messaging providers
- Environment-specific configuration support
- Designed for extensibility within the IBeam ecosystem

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core / Class Library
- **SMS Provider:** Twilio
- **Configuration:** application.json + environment overrides
- **Dependency Injection:** Microsoft.Extensions.*

---

## 📁 Project Structure

IBeam.Communications.Sms.Twilio/
├── Configuration/
│ └── TwilioOptions.cs
├── Services/
│ └── TwilioSmsService.cs
├── Interfaces/
│ └── ISmsSender.cs
├── application.json
├── IBeam.Communications.Sms.Twilio.csproj


---

## ⚙️ Configuration

This project uses an `application.json` file to configure Twilio credentials and defaults.

### Required Settings

- **AccountSid** – Twilio account SID
- **AuthToken** – Twilio auth token
- **FromPhoneNumber** – Default sender phone number

> ⚠️ Secrets should be stored securely using environment variables, Azure Key Vault, or another secure secret provider in production.

---

## 🚀 Usage

1. Register the Twilio SMS service with dependency injection.
2. Configure Twilio credentials in `application.json` or environment-specific overrides.
3. Inject `ISmsSender` wherever SMS delivery is required.

---

## 🔒 Security Notes

- Never commit real Twilio credentials to source control.
- Use secret managers for production deployments.
- Rotate tokens regularly.

---

## 🧩 Part of the IBeam Platform

This project is a modular communications component within the broader **IBeam** ecosystem and is intended to be consumed by APIs, background services, and future messaging workflows.
