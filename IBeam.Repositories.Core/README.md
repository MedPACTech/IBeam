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
- **Configuration:** appsettings / application.json per environment
- **Dependency Injection & Options Pattern:** Microsoft.Extensions.*

---

## 📁 Project Structure

IBeam.API/
├── application.json
├── application.Development.json
├── application.Test.json
├── application.Prod.json
├── Program.cs
├── Controllers/
├── Core/
│ ├── Entities/
│ ├── Options/
│ └── Interfaces/
├── Services/
│ ├── Identity/
│ └── Otp/
└── Infrastructure/


---

## 🔐 Authentication & OTP

IBeam supports OTP-based authentication using pluggable delivery mechanisms (email, SMS, etc.).

Key components:
- `IOtpService`
- `IOtpChallengeStore`
- `IOtpSender`
- `OtpOptions` (configured via application.json)

---

## ⚙️ Configuration

Application settings are defined in `application.json` and overridden per environment using:
- `application.Development.json`
- `application.Test.json`
- `application.Prod.json`

Sensitive values (connection strings, secrets, keys) should be stored securely using environment variables or a secret manager.

---

## 🚀 Getting Started

1. Ensure **.NET 10 SDK** is installed
2. Restore dependencies:
   ```bash
   dotnet restore
3. Run the API: dotnet run
4. Access Swagger (if enabled): https://localhost:{port}/swagger

📌 Notes

This API is intended to be the foundational identity and platform service for the IBeam ecosystem.
Designed to evolve with additional authentication methods, tenant support, and platform services.
