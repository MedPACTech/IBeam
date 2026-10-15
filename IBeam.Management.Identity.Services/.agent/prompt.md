# IBeam.Management.Identity.Services
Read the root .agent/implementation-guide.md. This is an optional Identity adapter; do not introduce Identity dependencies into Management core/services/API.
Keep rules and trusted bootstrap in services, storage in existing Identity providers, and HTTP in a separate API adapter. Management hosts cannot share an unkeyed Identity container with customer hosts. Never expose bootstrap publicly or capture secrets in audit. Keep storage, authentication/session and unsafe enrollment isolation tests current.
