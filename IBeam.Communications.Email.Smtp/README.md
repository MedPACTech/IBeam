# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational identity, authentication, and platform services (including OTP-based authentication) used across IBeam applications.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Development, Test, Local, Prod)
- Identity & authentication services
- OTP (One-Time Password) challenge and delivery framework
- Extensible communications infrastructure (Email, SMS, etc.)
- Designed for extensibility across the IBeam platform

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** appsettings per environment
- **Dependency Injection:** Microsoft.Extensions.DependencyInjection
- **Configuration & Options:** Microsoft.Extensions.Configuration / Options Pattern

---

## 📁 Project Structure

IBeam.API/
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Test.json
├── appsettings.Prod.json
├── Program.cs
├── Startup.cs
├── Controllers/
├── Services/
├── Models/
├── Configuration/
└── Infrastructure/


---

## ⚙️ Configuration

The application uses layered configuration via `appsettings.json` and environment-specific overrides.

Core configuration sections include:

- **IBeam**
  - Communications
    - Email (SMTP / Azure Communication Services)
  - Authentication
  - OTP

Example configuration can be found in `appsettings.json`.

---

## 🚀 Running the Application

```bash
dotnet restore
dotnet build
dotnet run

🔐 Security Notes

Secrets should never be committed to source control

Use environment variables or secure secret stores (Azure Key Vault, etc.)

OTP and authentication flows are designed to be stateless and auditable

📌 Status

This project is under active development and serves as the foundational API layer for the IBeam platform.