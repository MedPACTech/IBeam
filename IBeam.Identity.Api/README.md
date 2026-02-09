# IBeam Identity API

IBeam Identity API is the core identity and authentication service for the **IBeam** ecosystem.  
It is built on **ASP.NET Core (.NET 10)** and provides secure, extensible identity capabilities including OTP-based authentication and communications support.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Local, Development, Test, Prod)
- Identity & authentication services
- One-Time Password (OTP) generation and validation
- Pluggable OTP delivery (Email, SMS, etc.)
- Designed for extensibility across the IBeam platform

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** application.json + environment overrides
- **Dependency Injection:** Microsoft.Extensions.*
- **Options Pattern:** Strongly typed configuration

---

## 📁 Project Structure

IBeam.Identity.Api/
├── application.json
├── application.Development.json
├── application.Test.json
├── application.Prod.json
├── Program.cs
├── Controllers/
├── Services/
│ └── Otp/
├── Core/
│ ├── Entities/
│ ├── Options/
│ └── Interfaces/
└── Infrastructure/


---

## ⚙️ Configuration

The API uses a layered configuration approach:

- `application.json` – base settings
- `application.{Environment}.json` – environment-specific overrides

Configuration is bound using the **Options pattern**.

---

## 🔐 OTP Flow (High-Level)

1. Client requests OTP challenge
2. API generates secure OTP code
3. OTP is stored with expiration
4. OTP is delivered via configured sender (email/SMS)
5. Client submits OTP for verification
6. API validates and completes authentication

---

## 🚀 Running the API

```bash
dotnet restore
dotnet build
dotnet run
