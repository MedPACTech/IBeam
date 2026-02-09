# IBeam Communications – Email Pickup Directory

This project provides an **email delivery implementation using a Pickup Directory** for the IBeam platform.  
It is intended for **local development, testing, and non-production environments**, where emails are written to disk instead of being sent externally.

---

## ✨ Features

- Email delivery via filesystem pickup directory
- Ideal for local development and testing
- No external SMTP or email provider required
- Integrates with the IBeam communications abstraction
- Simple configuration via `application.json`

---

## 🧱 Tech Stack

- **.NET:** 10.0
- **Framework:** ASP.NET Core
- **Email Strategy:** Pickup Directory
- **Configuration:** JSON (Options Pattern)

---

## 📁 Project Structure

IBeam.Communications.Email.PickupDirectory/
├── Application/
│ ├── EmailPickupDirectoryService.cs
│ └── Interfaces/
├── Configuration/
│ └── EmailPickupDirectoryOptions.cs
├── Extensions/
│ └── ServiceCollectionExtensions.cs
├── application.json
└── README.md


---

## ⚙️ Configuration

All email behavior is configured through `application.json`.

Key settings include:
- Pickup directory path
- Default sender email address
- Default sender display name

---

## 🚀 Usage

1. Set the pickup directory path in `application.json`
2. Ensure the directory exists and is writable
3. Register the pickup directory provider via DI
4. Run the application
5. Emails will be written as `.eml` files to the pickup directory

---

## 🧪 Recommended Use Cases

- Local development
- Automated tests
- QA and staging environments
- Debugging email templates and workflows

> ⚠️ This provider is not intended for production use.

---

## 🧩 IBeam Ecosystem

This module is one of several interchangeable email providers within the IBeam platform  
(e.g., Pickup Directory, SMTP, Azure Communications).
