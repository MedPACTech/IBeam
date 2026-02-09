# IBeam Communications Email – Azure Communication Services

**IBeam.Communications.Email.AzureCommunications** is the Azure Communication Services (ACS) Email provider for the **IBeam** ecosystem.  
It implements the IBeam Email abstraction and enables transactional email delivery (such as OTP messages and system notifications) using **Azure Communication Services Email**.

This project is a **provider implementation**, not a standalone application.

---

## ✨ Features

* Azure Communication Services Email integration
* Provider-based email abstraction
* Configuration-driven sender identity
* Designed for transactional messaging (OTP, alerts, notifications)
* Easily swappable with other email providers

---

## 🧱 Tech Stack

* **.NET:** 10.0
* **Framework:** .NET Class Library
* **Email Provider:** Azure Communication Services (Email)
* **Configuration:** `IOptions` / `IOptionsMonitor`
* **DI:** Microsoft.Extensions.DependencyInjection

---

## 📁 Project Structure


 