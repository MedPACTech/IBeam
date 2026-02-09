# IBeam Identity Services

IBeam Identity Services is a core backend component of the **IBeam** ecosystem.  
It provides identity-related capabilities including authentication workflows, OTP (One-Time Password) generation, challenge storage, and delivery mechanisms.

This service is designed to be secure, extensible, and environment-agnostic, supporting multiple delivery channels and configuration profiles.

---

## ✨ Features

- ASP.NET Core Web API
- OTP challenge generation and validation
- Pluggable OTP senders (Email, SMS, etc.)
- Secure OTP hashing and expiration handling
- Multi-environment configuration support
- Options Pattern–based configuration
- Clean separation of contracts, services, and infrastructure

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Configuration:** application.json + environment overrides
- **Dependency Injection:** Microsoft.Extensions.DependencyInjection
- **Options Pattern:** Microsoft.Extensions.Options

---

## 📁 Project Structure

IBeam.Identity.Services/
├── Controllers/
├── Core/
│ ├── Entities/
│ ├── Options/
│ └── Otp/
│ ├── Contracts/
│ └── Interfaces/
├── Services/
│ └── Otp/
├── Infrastructure/
├── application.json
├── Program.cs
└── Startup.cs


---

## ⚙️ Configuration

All configuration is managed through `application.json` and environment-specific overrides.

Key configuration sections include:

- OTP behavior (length, expiration, retry limits)
- Communications (email/SMS providers)
- Security and hashing settings

---

## 🔐 OTP Flow (High-Level)

1. Client requests an OTP challenge
2. Service generates a secure OTP code
3. OTP is hashed and stored with expiration metadata
4. OTP is delivered via configured sender
5. Client submits OTP for validation
6. Challenge is validated and consumed

---

## 🚀 Getting Started

1. Restore dependencies  
   ```bash
   dotnet restore
2. Run the service : dotnet run
3. Configure delivery providers in application.json

🧩 Extensibility

Add new OTP delivery channels by implementing IOtpSender

Swap storage mechanisms via IOtpChallengeStore

Extend identity workflows without modifying core logic

📄 License

Proprietary – IBeam Platform
© IBeam