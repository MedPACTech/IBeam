# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational identity, authentication, and platform services used across IBeam applications, including OTP-based authentication and communications.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Development, Test, Local, Prod)
- Identity & authentication services
- OTP (One-Time Password) challenge framework
- Pluggable communication providers (Email, SMS)
- Designed for extensibility across the IBeam platform

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** JSON-based configuration
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
├── Core/
│ ├── Entities/
│ ├── Options/
│ └── Interfaces/
├── Services/
│ ├── Identity/
│ ├── Otp/
│ └── Communications/
└── Infrastructure/


---

## ⚙️ Configuration

Application configuration is handled through `application.json` and environment-specific overrides.

Key configuration areas include:

- Communications (Email, SMS)
- OTP behavior and expiration
- Identity and authentication settings

---

## 🚀 Getting Started

1. Ensure you have **.NET 10 SDK** installed
2. Restore dependencies:
   ```bash
   dotnet restore
3. Run the API: dotnet run
4. The API will be available at:
https://localhost:5001

🔐 OTP Flow (High-Level)

1) Client requests OTP challenge
2) OTP code is generated and stored
3) Code is delivered via configured communication channel
4) Client submits OTP for verification
5) Challenge is validated and resolved

📌 Notes
- Secrets should be stored securely (Key Vault, environment variables)
- application.json should not contain production secrets
- Designed to support future multi-tenant expansion

📄 License

Proprietary – © IBeam