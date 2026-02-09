# IBeam API

IBeam API is the core backend service for the **IBeam** ecosystem, built on **ASP.NET Core (.NET 10)**. It provides foundational identity, authentication, and platform services (including OTP-based authentication) used across IBeam applications.

---

## ✨ Features

* ASP.NET Core Web API
* Multi-environment configuration (Development, Test, Local, Prod)
* Identity & authentication services
* OTP (One-Time Password) challenge and delivery framework
* Designed for extensibility across the IBeam platform

---

## 🧱 Tech Stack

* **.NET:** 10.0
* **Framework:** ASP.NET Core Web API
* **Configuration:** appsettings per environment
* **DI & Options Pattern:** Microsoft.Extensions.*

---

## 📁 Project Structure

```
IBeam.API/
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Test.json
├── appsettings.Prod.json
├── Program.cs
├── Startup.cs
├── Controllers/
├── Services/
│   └── Otp/
├── Core/
│   └── Identity, Options, Entities
└── Infrastructure/
```

> Note: `bin/` and build artifacts are included in the repo zip but are not required for source control.

---

## ⚙️ Configuration

The API uses environment-specific configuration files:

* `appsettings.json` – base configuration
* `appsettings.Development.json`
* `appsettings.Test.json`
* `appsettings.Prod.json`
* `appsettings.local.json` (developer overrides, not for source control)

Typical configuration sections include:

* Connection strings
* Identity & authentication settings
* OTP options (code length, expiration, retry limits)
* Logging

---

## ▶️ Running the API

### Prerequisites

* .NET SDK 10.0+
* Visual Studio 2022+ or VS Code

### Run locally

```bash
dotnet restore
dotnet build
dotnet run --project IBeam.API
```

Or via Visual Studio:

1. Open `IBeam.API.csproj`
2. Set **IBeam.API** as startup project
3. Run using the desired environment profile

### Environment Selection

```bash
set ASPNETCORE_ENVIRONMENT=Development
```

or

```bash
$env:ASPNETCORE_ENVIRONMENT="Development"
```

---

## 🔐 OTP Services

IBeam includes a pluggable OTP framework:

* `IOtpService` – challenge creation and validation
* `IOtpSender` – SMS, Email, or custom delivery
* `IOtpChallengeStore` – persistence abstraction
* `OtpOptions` – configurable OTP behavior

This design allows:

* Multiple OTP channels
* Configurable code length & expiration
* Secure hashing and validation

---

## 🧪 Testing

* Unit tests can be added using xUnit or NUnit
* Environment-specific config allows isolated test setups

---

## 🚀 Deployment

* Supports containerization (Docker-ready)
* Designed for cloud hosting (Azure App Service, Containers, AKS)
* Use `appsettings.Prod.json` or environment variables for secrets

---

## 📦 Package Metadata

* **Package ID:** IBeam.API
* **Version:** 1.0.0
* **Company:** MedPAC Technologies
* **Repository:** [https://github.com/MedPACTech/IBeam](https://github.com/MedPACTech/IBeam)

---

## 🛡️ Security Notes

* Do **not** commit secrets or production keys
* Use environment variables or secure vaults
* OTP codes are never stored in plaintext

---

## 🤝 Contributing

1. Create a feature branch
2. Commit with clear messages
3. Submit a pull request

---

## 📄 License

Proprietary © MedPAC Technologies. All rights reserved.

---

If you want, I can tailor this README for **open-source**, **internal dev teams**, or **compliance / audit reviewers**, or generate a `README.md` file ready to drop into the repo.
