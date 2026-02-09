# IBeam Identity API

IBeam Identity API is the authentication and identity core for the **IBeam** ecosystem.  
It provides secure identity services including OTP-based authentication, user verification, and extensible identity primitives used across all IBeam applications.

---

## ✨ Features

- ASP.NET Core Web API
- OTP (One-Time Password) generation and validation
- Pluggable OTP delivery (Email, SMS, etc.)
- Multi-environment configuration support
- Options pattern for clean configuration
- Designed for modular reuse across IBeam services

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** application.json + environment overrides
- **Dependency Injection:** Microsoft.Extensions.DependencyInjection
- **Options Pattern:** Microsoft.Extensions.Options

---

## 📁 Project Structure

IBeam.Identity/
├── Controllers/
├── Core/
│ ├── Entities/
│ ├── Options/
│ └── Interfaces/
├── Services/
│ ├── Otp/
│ └── Identity/
├── Infrastructure/
│ ├── Stores/
│ └── Senders/
├── Program.cs
├── application.json
└── README.md


---

## ⚙️ Configuration

The application uses `application.json` as the base configuration file, with optional environment-specific overrides:

- `application.Development.json`
- `application.Test.json`
- `application.Prod.json`

Configuration is bound using the **Options pattern**.

---

## 🔐 OTP Flow (High-Level)

1. Client requests OTP challenge
2. Server generates OTP code
3. OTP is stored securely with expiration
4. OTP is delivered via configured sender (Email/SMS)
5. Client submits OTP for validation
6. Server verifies and completes authentication

---

## 🚀 Getting Started

```bash
dotnet restore
dotnet build
dotnet run
