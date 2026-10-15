# IBeam.Management.Api

Read the repository .agent/implementation-guide.md before changes.

Keep controllers thin and use IBeam response helpers. Require explicit management authentication schemes. Do not implement business permissions, secret validation or repository queries in controllers.

Do not leak secrets or raw customer payloads into DTOs, audit snapshots, exception messages or logs. Preserve API -> Services -> Repository boundaries. Build focused tests for authentication substitution, scope isolation, denied access and unsafe diagnostic content. Keep README registration and scope accurate. The separate IBeam.Admin.API host and its rename are deferred.
