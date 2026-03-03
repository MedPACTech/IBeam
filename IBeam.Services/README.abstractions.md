# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**.  
It provides foundational identity, authentication, and platform services used across IBeam applications, including OTP-based authentication and communications.

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
- **Configuration:** JSON-based (`application.json`)
- **Dependency Injection:** Built-in Microsoft DI
- **Options Pattern:** `Microsoft.Extensions.Options`

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

## 🔐 OTP Authentication Flow

1. Client requests OTP challenge
2. Server generates secure OTP
3. OTP is delivered via configured sender (Email/SMS)
4. Client submits OTP for verification
5. Challenge is validated and completed

---

## 🚀 Getting Started

1. Configure `application.json`
2. Select the environment profile
3. Run the API using:
   ```bash
   dotnet run

📄 License
Proprietary – IBeam Platform