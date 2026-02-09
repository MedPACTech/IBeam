# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational identity, authentication, and platform services used across IBeam applications, including OTP-based authentication and extensible communication services.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Local, Development, Test, Prod)
- Identity & authentication services
- OTP (One-Time Password) challenge and delivery framework
- Email communications via Azure Communication Services
- Designed for extensibility across the IBeam platform

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** JSON-based configuration with environment overrides
- **Dependency Injection:** Microsoft.Extensions.DependencyInjection
- **Options Pattern:** Microsoft.Extensions.Options

---

## 📁 Project Structure

IBeam.API/
├── application.json
├── application.Development.json
├── application.Test.json
├── application.Prod.json
├── Program.cs
├── Controllers/
├── Services/
├── Core/
│ ├── Entities/
│ ├── Options/
│ └── Interfaces/
└── Infrastructure/


---

## ⚙️ Configuration

The application uses `application.json` as the base configuration file, with environment-specific overrides.

Key configuration sections include:
- Communications (Email, SMS, OTP delivery)
- Identity and authentication settings
- Platform-level options

---

## 🚀 Running the API

```bash
dotnet restore
dotnet build
dotnet run

Set the environment as needed:

ASPNETCORE_ENVIRONMENT=Development

🔐 Security Notes
- Secrets (connection strings, keys) should never be committed to source control
- Use Azure Key Vault or environment variables for sensitive values in production

🧩 Extensibility
IBeam API is designed to be modular and extensible:
- Add new communication providers
- Extend OTP strategies
- Plug into additional IBeam platform services

📄 License

Proprietary – IBeam Platform
© IBeam Technologies