# IBeam.Management.Services

Read the repository .agent/implementation-guide.md before changes.

Services own validation, management authorization, business rules and audit; custom operations must run through IServiceOperationExecutor. Never allow customer roles to bypass management context.

Do not leak secrets or raw customer payloads into DTOs, audit snapshots, exception messages or logs. Preserve API -> Services -> Repository boundaries. Build focused tests for authentication substitution, scope isolation, denied access and unsafe diagnostic content. Keep README registration and scope accurate. The separate IBeam.Admin.API host and its rename are deferred.

System-error reads use explicit dedicated/shared source trust and live Errors.Read checks before every provider query. Preserve UTC range/page/scan/request/time bounds and encrypted source/deployment/query-bound state with fixed expiry. Internal error metadata is not a public DTO. Never audit tokens, query/entity/return snapshots or untrusted Source/TraceId; safe public projection is a separate reader/API step. System-log reads follow the same contract under Logs.Read with their own key and frozen exact filters; never project log message/detail text.

Applications require explicit human management.applications.admin and server-owned organization scope. Preserve immutable app/environment identity, conditional activation, reference-only credentials and write-only keys. Outbound probes must keep exact approved HTTPS origins, public DNS validation, IP-pinned sockets and normal TLS; never enable redirects, proxies, cookies or trace/header/body secret capture. Read-only environment bindings must never be writable from HTTP. Keep timeout/cancellation and failed-rotation tests current.

App-assignment operations require live management organization/user/app state and explicit access-admin plus per-capability delegation ceilings. Never authorize from a stale JWT grant or role alone. Diagnostic RequireAsync has no administrator bypass or cache; compare-and-swap protects all assignment changes. Keep Identity dependencies behind live-state ports.

Management audit uses IManagementAuditRepository through a dedicated metadata-only sink/executor for Management assemblies; preserve the host's customer-service audit pipeline. Persist only allowlisted action/result/time and trusted subject/org/app/correlation IDs, never generic snapshots or raw transaction/request/error text. Keep retention/failure policy and non-transactional recovery explicit. Portal/selector services coordinate entity-service ports with current user/app/grant/connection checks; authorize before resolving secrets, recheck around data reads, and bind tokens to the person and connection. Do not weaken outbound DNS/IP/TLS/approved-origin policy to run local smoke hosts; the sample uses a fixture-only public-port adapter.
