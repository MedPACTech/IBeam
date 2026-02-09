# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational identity, authentication, and platform services (including OTP-based authentication) used across IBeam applications.

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
- **Dependency Injection:** Microsoft.Extensions.DependencyInjection
- **Options Pattern:** Microsoft.Extensions.Options

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
│ ├── Entities/
│ ├── Options/
│ └── Interfaces/
└── Infrastructure/


---

## ⚙️ Configuration

The API uses layered configuration via `appsettings.json` and environment-specific overrides.

Key configuration areas include:

- Identity & authentication
- OTP challenge settings
- Email and notification services
- External integrations

---

## ▶️ Running the API

```bash
dotnet restore
dotnet build
dotnet run

The API will start using the environment specified by ASPNETCORE_ENVIRONMENT.

🔐 Security Notes
OTP codes are generated cryptographically
OTP challenges are time-bound and single-use
Sensitive configuration values should be stored securely (Key Vault, user secrets, etc.)

🚀 Future Enhancements
SMS and push-based OTP delivery
Rate limiting and abuse prevention
Expanded identity federation support

📄 License

Proprietary – IBeam Platform