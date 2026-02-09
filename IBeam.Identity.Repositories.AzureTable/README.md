# IBeam Identity API

IBeam Identity API is a core service within the **IBeam** ecosystem, responsible for identity management, authentication, and OTP-based verification. It is built on **ASP.NET Core (.NET 10)** and designed to be secure, extensible, and cloud-ready.

---

## ✨ Features

- ASP.NET Core Web API
- OTP (One-Time Password) generation and validation
- Pluggable OTP delivery (Email, SMS, etc.)
- Azure Table Storage-backed repositories
- Multi-environment configuration support
- Options pattern–based configuration
- Clean architecture with clear separation of concerns

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core Web API
- **Storage:** Azure Table Storage
- **Configuration:** application.json + environment overrides
- **Dependency Injection:** Microsoft.Extensions.DependencyInjection
- **Options Pattern:** Microsoft.Extensions.Options

---

## 📁 Project Structure

IBeam.Identity/
├── IBeam.Identity.API/
│ ├── Controllers/
│ ├── Program.cs
│ └── application.json
│
├── IBeam.Identity.Core/
│ ├── Entities/
│ ├── Options/
│ ├── Otp/
│ │ ├── Contracts/
│ │ └── Interfaces/
│
├── IBeam.Identity.Services/
│ ├── OtpService.cs
│ └── Senders/
│
├── IBeam.Identity.Repositories.AzureTable/
│ ├── Stores/
│ └── Tables/
│
└── README.md

---

## ⚙️ Configuration

Configuration is driven by `application.json` and environment-specific overrides.

Key configuration areas include:

- OTP behavior and security
- Email delivery via Azure Communication Services
- Storage connection settings

---

## 🔐 OTP Flow Overview

1. Client requests an OTP challenge
2. Server generates a cryptographically secure code
3. OTP is persisted with expiration metadata
4. Code is delivered via configured sender
5. Client submits code for verification
6. Challenge is validated and consumed

---

## 🚀 Getting Started

1. Configure `application.json`
2. Restore dependencies:
   ```bash
   dotnet restore
3. Run the API: dotnet run

🧩 Extensibility

Add new OTP senders by implementing IOtpSender

Swap persistence layers by implementing IOtpChallengeStore

Extend identity flows without impacting consumers

📄 License

Proprietary – IBeam Platform
© IBeam Technologies