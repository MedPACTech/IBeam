# IBeam Identity Core

IBeam Identity Core is a foundational library for the **IBeam ecosystem**, responsible for identity, authentication, and security-related services. It is designed to be used by IBeam APIs and services, with a strong focus on OTP-based authentication and extensibility.

---

## ✨ Features

- Identity and authentication primitives
- OTP (One-Time Password) challenge generation and validation
- Pluggable OTP delivery mechanisms (email, SMS, etc.)
- Secure cryptographic utilities
- Options-based configuration using `Microsoft.Extensions.Options`
- Clean separation of contracts, entities, and services

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Language:** C#
- **Configuration:** application.json / appsettings.json
- **Patterns:** Options pattern, Dependency Injection
- **Security:** Cryptographically secure OTP generation

---

## 📁 Project Structure

IBeam.Identity.Services/
├── Entities/
│ ├── OtpChallenge.cs
│ └── IdentityUser.cs
├── Options/
│ └── OtpOptions.cs
├── Otp/
│ ├── Contracts/
│ ├── Interfaces/
│ └── Models/
├── Services/
│ └── OtpService.cs
├── Extensions/
├── IBeam.Identity.Services.csproj
└── README.md


---

## ⚙️ Configuration

This library uses strongly-typed options bound from `application.json` (or `appsettings.json`).  
OTP behavior, expiration, and delivery are configurable via the `IBeam:Identity:Otp` section.

---

## 🔐 OTP Flow (High-Level)

1. Client requests an OTP challenge
2. `OtpService` generates a secure code
3. Challenge is persisted via `IOtpChallengeStore`
4. OTP is delivered via a configured `IOtpSender`
5. Client submits OTP for verification
6. Challenge is validated and marked as used/expired

---

## 🚀 Usage

Register services in your API or host application:

```csharp
services.AddIBeamIdentity(configuration);
