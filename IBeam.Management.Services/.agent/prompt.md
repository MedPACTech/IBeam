# IBeam.Management.Services

Read the repository .agent/implementation-guide.md before changes.

Services own validation, management authorization, business rules and audit; custom operations must run through IServiceOperationExecutor. Never allow customer roles to bypass management context.

Do not leak secrets or raw customer payloads into DTOs, audit snapshots, exception messages or logs. Preserve API -> Services -> Repository boundaries. Build focused tests for authentication substitution, scope isolation, denied access and unsafe diagnostic content. Keep README registration and scope accurate. The separate IBeam.Admin.API host and its rename are deferred.
