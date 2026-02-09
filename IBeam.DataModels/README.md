# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational identity, authentication, and platform services (including OTP-based authentication) used across IBeam applications.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Development, Test, Local, Prod)
- Identity & authentication services
- OTP (One-Time Password) challenge and delivery framework
- Designed for extensibility across the IBeam platform

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** appsettings per environment
- **DI & Options Pattern:** Microsoft.Extensions.*

---

## 📁 Project Structure

IBeam.API/
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Test.json
├── appsettings.Local.json
├── appsettings.Prod.json
├── Program.cs
├── Startup.cs
├── Controllers/
├── Services/
├── Models/
├── Configuration/
└── Middleware/


---

## ⚙️ Configuration

Configuration is environment-specific and managed using `appsettings.{Environment}.json`.

Core configuration sections include:

- Communications (Email, SMS)
- Authentication & Identity
- OTP settings
- Platform-level feature flags

---

## 🚀 Running the API

```bash
dotnet restore
dotnet build
dotnet run

ASPNETCORE_ENVIRONMENT=Development

🔐 Security Notes
No secrets should be committed to source control

Use Azure Key Vault or environment variables for production secrets

OTP and identity services are designed to be policy-driven and extensible

🧩 Extensibility
The IBeam API is intended to act as a platform service, not just a single-purpose API.
New modules (notifications, audit logging, tenant services, etc.) can be added without breaking existing consumers.

📄 License
Proprietary – IBeam Platform
© IBeam