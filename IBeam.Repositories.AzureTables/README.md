# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational identity, authentication, and platform services used across IBeam applications, including OTP-based authentication and communications infrastructure.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Local, Development, Test, Prod)
- Identity & authentication services
- OTP (One-Time Password) challenge framework
- Pluggable communications (Email, SMS, etc.)
- Designed for extensibility across the IBeam platform

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** application.json + environment overrides
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
├── Identity/
│ ├── Otp/
│ ├── Authentication/
│ └── Authorization/
└── Infrastructure/


---

## ⚙️ Configuration

All application configuration is stored in `application.json` and overridden per environment as needed.

Key configuration areas include:

- Communications (Email, SMS)
- OTP behavior and policies
- Identity and security settings

---

## 🚀 Getting Started

1. Ensure **.NET 10 SDK** is installed
2. Configure `application.Development.json`
3. Run the API:

```bash
dotnet run

The API will start using the configured environment settings.

🔐 Security Notes
Secrets should never be committed to source control
Use environment variables or secure secret stores for production
OTP codes and challenges are time-bound and single-use

📄 License

Copyright © IBeam
All rights reserved.
