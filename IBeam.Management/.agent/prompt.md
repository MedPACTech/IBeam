# IBeam.Management

Read the repository .agent/implementation-guide.md before changes.

Keep this package transport independent. No Identity, ASP.NET Core, services or repository references. Management organization, application/environment and customer tenant are distinct scopes.

Do not leak secrets or raw customer payloads into DTOs, audit snapshots, exception messages or logs. Preserve API -> Services -> Repository boundaries. Build focused tests for authentication substitution, scope isolation, denied access and unsafe diagnostic content. Keep README registration and scope accurate. The separate IBeam.Admin.API host and its rename are deferred.
