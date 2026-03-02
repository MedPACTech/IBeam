# IBeam Services

IBeam Services is a core backend service within the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational platform capabilities such as identity, authentication, OTP challenges, and communications services.

---

## ✨ Features

- ASP.NET Core Web API (.NET 10)
- Modular, service-oriented architecture
- OTP (One-Time Password) challenge framework
- Pluggable communication providers (Email, SMS, etc.)
- Multi-environment configuration support
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

IBeam.Services/
├── application.json
├── application.Development.json
├── application.Test.json
├── application.Prod.json
├── Program.cs
├── Services/
│ ├── Identity/
│ ├── Otp/
│ └── Communications/
├── Controllers/
├── Core/
│ ├── Entities/
│ ├── Options/
│ └── Contracts/
└── Infrastructure/


---

## 🔐 OTP Services

The OTP subsystem supports:
- Configurable code length and expiration
- Pluggable storage providers
- Multiple delivery mechanisms (Email, SMS, etc.)

---

## 🚀 Running the Service

```bash
dotnet restore
dotnet build
dotnet run

The API will start using the configuration defined in application.json and the active environment.

📌 Notes
- Secrets should be stored securely (Key Vault, User Secrets, etc.)
- application.json should be treated as a template, not a secrets store