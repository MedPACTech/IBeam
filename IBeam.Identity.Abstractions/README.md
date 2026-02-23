
# IBeam.Identity.Abstractions

`IBeam.Identity.Abstractions` defines the core contracts, interfaces, and models for authentication and identity management in the IBeam ecosystem. It provides a set of abstractions for user registration, authentication, OTP (One-Time Password) flows, token generation, and multi-tenant management, enabling modular and flexible implementations across different services.

This project does **not** contain implementation logic. Instead, it allows other projects to implement these interfaces for various storage providers, OTP delivery mechanisms, or authentication strategies, ensuring consistency and extensibility throughout the platform.

---

## 🗝️ What Does This Project Provide?

- **Interfaces** for:
  - User registration and authentication (`IIdentityAuthService`, `IIdentityRegistrationService`)
  - User and tenant storage (`IIdentityUserStore`, `ITenantMembershipStore`)
  - OTP challenge and verification (`IOtpService`, `IOtpChallengeStore`)
  - Token creation (`ITokenService`)
  - Claims enrichment and context (`IClaimsEnricher`, `ClaimsEnrichmentContext`)
- **Models** for requests and responses (e.g., `RegisterUserRequest`, `AuthResultResponse`, `IdentityUser`, `OtpChallengeRequest`, `TokenResult`)
- **Options** for configuration (e.g., `IdentityOptions`, `OtpOptions`, `FeatureOptions`)
- **Exceptions** for identity-related error handling
- **Schema** contracts for advanced identity workflows

These abstractions are used by other IBeam projects to implement concrete identity services, repositories, and authentication flows.

---

---


## ✨ Key Features

- Defines the contract for all identity and authentication operations in IBeam
- Supports OTP-based authentication and multi-tenant scenarios
- Enables pluggable implementations for user, tenant, and OTP storage
- Clean separation of concerns for maintainable and testable code
- Used by API, service, and repository projects throughout the IBeam solution

---


## 🧱 Tech Stack

- **.NET:** 10.0
- **Type:** Class Library (Abstractions Only)
- **Usage:** Referenced by IBeam identity, API, and service projects

---


## 📁 Project Structure (Abbreviated)

IBeam.Identity.Abstractions/
├── Interfaces/           # Core service and storage interfaces
├── Models/               # DTOs for requests, responses, and entities
├── Options/              # Configuration option classes
├── Exceptions/           # Custom exception types
├── Schema/               # Identity schema contracts
├── application.json      # Example configuration
└── README.md


---


## ⚙️ Usage

Reference this project in your implementation or API projects to access the identity contracts. Implement the interfaces as needed for your storage, authentication, or OTP delivery requirements.

Configuration options are provided as POCOs for use with the .NET Options pattern.

---


## 🔐 Example: OTP Authentication Flow

1. Client requests an OTP challenge (see `IOtpService`)
2. Implementation generates and stores OTP securely
3. OTP is delivered via pluggable sender (Email/SMS/etc.)
4. Client submits OTP for verification
5. Implementation verifies OTP and returns authentication result

---

## 🚀 Getting Started

```bash
dotnet restore
dotnet build
dotnet run
