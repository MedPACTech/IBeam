# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational identity, authentication, and platform services (including OTP-based authentication) used across IBeam applications.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Local, Development, Test, Production)
- Identity & authentication services
- OTP (One-Time Password) challenge and delivery framework
- Pluggable communication providers (Email, SMS, etc.)
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
├── Controllers/
├── Core/
│ ├── Entities/
│ ├── Options/
│ ├── Contracts/
├── Services/
│ ├── Identity/
│ ├── Otp/
│ ├── Communications/
├── Infrastructure/
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Test.json
├── appsettings.Prod.json
├── Program.cs
└── Startup.cs


---

## 🔐 OTP Flow Overview

1. Client requests an OTP challenge
2. Server generates a secure OTP
3. OTP is stored with expiration and retry limits
4. OTP is delivered via configured communication provider
5. Client submits OTP for verification

---

## 🚀 Getting Started

### Prerequisites

- .NET SDK 10.0+
- Visual Studio 2022+ or VS Code

### Run Locally

```bash
dotnet restore
dotnet build
dotnet run

The API will start using the Local or Development configuration by default.

⚙️ Configuration

All application configuration is defined in appsettings.json and overridden per environment as needed.

- Key configuration sections include:
- IBeam
- Identity
- Otp
- Communications

See application.json for a reference configuration structure.

🧩 Extensibility
IBeam API is designed to support:
- Multiple OTP delivery mechanisms
- Additional identity providers
- Future platform services within the IBeam ecosystem

📄 License
Internal use only — proprietary to the IBeam platform.