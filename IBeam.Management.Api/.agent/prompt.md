# IBeam.Management.Api

Read the repository .agent/implementation-guide.md before changes.

Keep controllers thin and use IBeam response helpers. Require explicit management authentication schemes. Do not implement business permissions, secret validation or repository queries in controllers.

Do not leak secrets or raw customer payloads into DTOs, audit snapshots, exception messages or logs. Preserve API -> Services -> Repository boundaries. Build focused tests for authentication substitution, scope isolation, denied access and unsafe diagnostic content. Keep README registration and scope accurate. The separate IBeam.Admin.API host and its rename are deferred.

Application/connection controllers are optional explicit feature registrations. Never expose them through base assembly scanning. Connection DTO keys are write-only, body logging is disabled, responses are no-store, and automatic model-state responses must not echo values. No HTTP endpoint may accept arbitrary target URLs or secret references. Permissions and connection activation remain in services.

IBM-0097 explicitly allows typed diagnostic raw fields only when that module's host-owned Sanitization.Enabled is false and the host/deployment are supported non-Production contexts. The default/blank mode remains sanitized; no browser/request/claim may change it. This is the sole exception to older raw-message prohibition guidance: no audit JSON, actor/device/IP/tenant/entity columns or source/category/grant widening is allowed in either mode. Keep raw fields JSON-hidden in internal records and redacted in ToString; public raw objects must be clearly marked unsanitized. Advertise/verify module modes, bind target/portal tokens to the mode, and never attach payloads to audit or service logs. See docs/management/diagnostic-sanitization.md.

IBM-0106 portal tenant writes are explicit opt-in. Preserve Idempotency-Key and exact If-Match, route all rules through services and return encrypted tracking for uncertain outcomes. Polls require current authority; references are personal/app/connection-bound. No raw request/reference logging or machine/signing key output.
