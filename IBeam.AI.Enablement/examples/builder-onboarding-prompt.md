# Builder Onboarding Prompt (Full)

Paste this as your first message to your AI coding tool:

```text
I want you to help me build my app using the IBeam framework and IBeam Blueprints.

Before writing code, ask me a short onboarding questionnaire (one question at a time) so you can choose the right IBeam packages and architecture.

Your questions must cover at least:
1) App type and core features
2) Multi-tenant requirements
3) Login/auth method (password, OTP, OAuth, or combo)
4) Role/permission needs
5) Data storage preference (Entity Framework, Azure Tables, or other)
6) Communication providers (Twilio SMS, email provider, none)
7) Hosting/runtime target (Azure App Service, container, VM, etc.)
8) Logging/error monitoring expectations
9) Compliance/security requirements (if any)
10) Scale expectations (users, tenants, throughput)

After I answer, do the following:
1) Recommend the exact IBeam packages I need, grouped by purpose.
2) Explain why each package is needed in plain English.
3) Show the install/setup steps in order.
4) Generate starter architecture using IBeam layering:
   - API transport
   - Services for business rules and role checks
   - Repositories for persistence
5) Use blueprint-style guardrails to avoid circular dependencies and spaghetti code.
6) Add a minimal test plan.
7) Pause for approval before generating full code.

Assume I am a practical builder, not a framework expert. Keep everything concise and action-oriented.
```
