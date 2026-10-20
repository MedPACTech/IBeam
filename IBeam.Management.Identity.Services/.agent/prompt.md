# IBeam.Management.Identity.Services
Read the root .agent/implementation-guide.md. This is an optional Identity adapter; do not introduce Identity dependencies into Management core/services/API.
Keep rules and trusted bootstrap in services, storage in existing Identity providers, and HTTP in a separate API adapter. Management hosts cannot share an unkeyed Identity container with customer hosts. Never expose bootstrap publicly or capture secrets in audit. Keep storage, authentication/session and unsafe enrollment isolation tests current.

Support onboarding reuses management Identity users/memberships/TenantInvites and only grants ManagementSupport. Email invitation tokens/passwords are write-only; no session may be issued before normal MFA login. Atomic redemption markers stay in the existing TenantInvites table. Routine disablement must reject self and any management administrator. Rebuild user/access-admin and delegation permissions from live catalog roles on every request/refresh.

IBM-0109 adds independent customer roles.read. Derive bounded operation/delegation limits from the explicit operation catalog, without auto-granting new names to administrators or reusing customer roles. All app-specific ceilings and live assignment checks remain required.
