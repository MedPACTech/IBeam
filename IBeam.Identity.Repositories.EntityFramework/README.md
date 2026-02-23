# IBeam.Identity.Repositories.EntityFramework

This project provides the **Entity Framework Core repository implementations** for the IBeam Identity platform. It is responsible for persistence and data access related to identity, authentication, and OTP challenges.

---

## ✨ Features

- Entity Framework Core–based repositories
- SQL-backed persistence for Identity & OTP workflows
- Clean separation via repository interfaces
- Designed for DI and testability
- Part of the larger **IBeam Identity** ecosystem

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **ORM:** Entity Framework Core
- **Database:** SQL Server (configurable)
- **Configuration:** `application.json` / environment overrides

---

## 📁 Project Structure

IBeam.Identity.Repositories.EntityFramework/
├── DbContexts/
│ └── IdentityDbContext.cs
├── Configurations/
│ └── Entity configurations (Fluent API)
├── Repositories/
│ └── Repository implementations
├── Migrations/
├── application.json
└── IBeam.Identity.Repositories.EntityFramework.csproj


---

## ⚙️ Configuration

Database and infrastructure settings are configured via `application.json` and environment-specific overrides.

Example:
- Connection strings
- EF Core provider options
- Retry and timeout behavior

---

## 🔌 Usage

Register the repositories and DbContext in your API or service layer:

```csharp
services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("IdentityDb")));

services.AddScoped<IOtpChallengeStore, OtpChallengeRepository>();
🔐 Security Notes

Secrets should never be committed to source control

Use environment variables or secure secret stores for production

Supports managed identity–based authentication where applicable

🧩 Related Projects

IBeam.Identity.Services

IBeam.Identity.Services

IBeam.API

📄 License

Proprietary – IBeam Platform
© IBeam. All rights reserved.