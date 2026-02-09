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
│ └── Contracts/


---

## 🚀 Getting Started

1. Ensure **.NET 10 SDK** is installed
2. Restore dependencies: dotnet restore
3. Run the API: dotnet run
 

The API will start using the environment-specific configuration defined in `appsettings.{Environment}.json`.

---

## 🔐 Authentication & OTP

IBeam supports OTP-based authentication using a pluggable challenge and sender model.  
OTP configuration is managed via the `IBeam:Otp` section in `appsettings.json`.

---

## 🧩 Extensibility

The API is designed as a foundation layer for the IBeam platform and can be extended with:

- Additional identity providers
- Messaging channels (SMS, Email, Push)
- Tenant-aware services
- Platform-specific modules


