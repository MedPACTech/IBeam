# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**. It provides foundational identity, authentication, and platform services used across IBeam applications, including OTP-based authentication and extensible communication services.

---

## ✨ Features

- ASP.NET Core Web API
- Multi-environment configuration (Local, Development, Test, Prod)
- Identity & authentication services
- OTP (One-Time Password) challenge framework
- Pluggable notification and communication providers
- Clean architecture with strong separation of concerns

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
├── Core/
│ ├── Entities/
│ ├── Options/
│ └── Contracts/
├── Infrastructure/
└── README.md


---

## ⚙️ Configuration

All configuration is centralized in `application.json` and overridden per environment using standard ASP.NET Core configuration precedence.

Key configuration areas include:

- OTP behavior and policies
- Communication providers (email, SMS, etc.)
- Identity and security settings

---

## 🚀 Running the API

```bash
dotnet restore
dotnet build
dotnet run

The API will start using the environment specified by ASPNETCORE_ENVIRONMENT.

🔐 Security Notes
Secrets should never be committed to source control
Use environment variables or secure secret stores for production
OTP codes are time-bound and hashed at rest

🧩 Extensibility

IBeam is designed as a modular ecosystem. New providers, identity mechanisms, and services can be added with minimal friction using interfaces and dependency injection.

📄 License

Internal use only. © IBeam