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
- **Configuration:** `appsettings.json` per environment
- **Dependency Injection & Options Pattern:** Microsoft.Extensions.*

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
│ └── Otp/
└── Infrastructure/


---

## 🔐 OTP Authentication

The IBeam API includes a flexible OTP framework that supports:

- Secure OTP generation
- Pluggable delivery mechanisms (Email, SMS, etc.)
- Configurable expiration, retry limits, and throttling
- Persistent challenge storage

---

## 🚀 Getting Started

1. Ensure **.NET 10 SDK** is installed
2. Configure environment-specific settings in `appsettings.{Environment}.json`
3. Run the application:

```bash
dotnet run

📄 Configuration

Core configuration is managed through application.json and environment overrides.
Sensitive values (connection strings, secrets) should be stored securely (Key Vault, environment variables).

🛡️ Security Notes
- OTP secrets and challenge data should never be logged
- Always use HTTPS in non-local environments
- Enforce rate limiting on authentication endpoints

📦 Status
This service is under active development and serves as a foundational component of the IBeam platform.