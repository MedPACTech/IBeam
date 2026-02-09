# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational platform services including identity, authentication, and OTP-based communication.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Development, Test, Local, Prod)
- Identity & authentication services
- OTP (One-Time Password) challenge framework
- Email delivery via Azure Communication Services
- Designed for extensibility across the IBeam platform

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** JSON-based (per environment)
- **Dependency Injection:** Microsoft.Extensions.*
- **Cloud Services:** Azure Communication Services

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
│ └── Contracts/

---

## ⚙️ Configuration

Core configuration is managed via `application.json` / `appsettings.json` files and bound using the
Options pattern.

Example sections include:

- Communications (Email / SMS)
- OTP settings
- Identity & security options

---

## 🚀 Getting Started

1. Install **.NET 10 SDK**
2. Restore dependencies  
   ```bash
   dotnet restore
3. Run the API: dotnet run

🔐 Security Notes
- Secrets (connection strings, keys) should be stored securely
- Do not commit real credentials to source control
- Use environment-specific overrides where possible

📄 License

Proprietary – © IBeam. All rights reserved.