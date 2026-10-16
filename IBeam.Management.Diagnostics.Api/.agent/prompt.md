# IBeam.Management.Diagnostics.Api

Read the repository .agent/implementation-guide.md before changes.

Keep controllers thin and use IBeam response helpers. Require explicit management authentication schemes. Do not implement business permissions, secret validation or repository queries in controllers.

Do not leak secrets or raw customer payloads into DTOs, audit snapshots, exception messages or logs. Preserve API -> Services -> Repository boundaries. Build focused tests for authentication substitution, scope isolation, denied access and unsafe diagnostic content. Keep README registration and scope accurate. The separate IBeam.Admin.API host and its rename are deferred.

Persisted management keys use only the dedicated header and reserved diagnostics route namespace. Preserve customer authentication defaults. Do not add fallback parsing for customer headers/Bearer/query strings. Every new diagnostics reader must call IManagementDiagnosticsAccessService.RequireAsync before its repository query. CIDR checks use only a trusted server/proxy-derived remote address. Never log the management key header. Keep BYO-auth registration usable independently.

The error and log controllers are opt-in: preserve NonController plus its explicit feature provider so base installs keep routes absent. Invalid model-state responses must not echo attempted values. Keep detail not-found responses uniform, service error text sanitized and successful/handled diagnostic responses noncacheable. Optional modules register IManagementDiagnosticsCapability additively; never replace the shared info service or mutate another module's routes.
