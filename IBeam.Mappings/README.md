# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational identity, authentication, and platform services used across IBeam applications.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Development, Test, Local, Prod)
- Identity & authentication services
- OTP (One-Time Password) challenge and delivery framework
- Modular, extensible architecture for the IBeam platform

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** appsettings per environment
- **Dependency Injection & Options Pattern**
- **Azure Communication Services** (Email)

---

## 📁 Project Structure

IBeam.API/
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Test.json
├── appsettings.Prod.json
├── Program.cs
├── Controllers/
├── Services/
├── Core/
└── Infrastructure/


---

## 🔐 Authentication & OTP

IBeam includes a pluggable OTP framework supporting:
- OTP challenge creation
- Secure hashing & expiration
- Multiple delivery mechanisms (Email, SMS-ready)
- Store-backed validation

---

## 🚀 Getting Started

1. Clone the repository
2. Configure `appsettings.json`
3. Run the API:

```bash
dotnet run

🛠 Environment Configuration

Each environment uses its own configuration file:
- appsettings.Development.json
- appsettings.Test.json
- appsettings.Prod.json

Sensitive values should be injected via:
- User Secrets (local)
- Environment Variables
- Azure App Configuration / Key Vault

📄 License

Proprietary – IBeam Platform
© IBeam Technologies
