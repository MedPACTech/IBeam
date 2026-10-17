# IBeam.Management

Read the repository .agent/implementation-guide.md before changes.

Keep this package transport independent. No Identity, ASP.NET Core, services or repository references. Management organization, application/environment and customer tenant are distinct scopes.

Do not leak secrets or raw customer payloads into DTOs, audit snapshots, exception messages or logs. Preserve API -> Services -> Repository boundaries. Build focused tests for authentication substitution, scope isolation, denied access and unsafe diagnostic content. Keep README registration and scope accurate. The separate IBeam.Admin.API host and its rename are deferred.

IBM-0097 explicitly allows typed diagnostic raw fields only when that module's host-owned Sanitization.Enabled is false and the host/deployment are supported non-Production contexts. The default/blank mode remains sanitized; no browser/request/claim may change it. This is the sole exception to older raw-message prohibition guidance: no audit JSON, actor/device/IP/tenant/entity columns or source/category/grant widening is allowed in either mode. Keep raw fields JSON-hidden in internal records and redacted in ToString; public raw objects must be clearly marked unsanitized. Advertise/verify module modes, bind target/portal tokens to the mode, and never attach payloads to audit or service logs. See docs/management/diagnostic-sanitization.md.
