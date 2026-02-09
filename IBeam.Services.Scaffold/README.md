# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational identity, authentication, and platform services, including **OTP-based authentication**.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Local, Development, Test, Prod)
- Identity & authentication services
- OTP (One-Time Password) challenge and delivery framework
- Extensible, modular service architecture

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** `application.json` + environment overrides
- **Dependency Injection:** Microsoft.Extensions.*
- **Options Pattern:** Strongly-typed configuration

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
├── Core/
└── Infrastructure/


---

## ⚙️ Configuration

Configuration is driven through `application.json` and environment-specific overrides.

Key configuration areas include:
- Communications (Email, SMS, etc.)
- OTP policies and expiration
- Identity and authentication settings

---

## 🚀 Running the API

```bash
dotnet restore
dotnet build
dotnet run

The API will start using the environment specified by ASPNETCORE_ENVIRONMENT.

🔐 Security Notes
- Secrets should never be committed to source control
- Use environment variables or secure secret stores for production
- OTP codes are time-bound and single-use by design

📌 Status
This project is under active development as part of the IBeam platform.