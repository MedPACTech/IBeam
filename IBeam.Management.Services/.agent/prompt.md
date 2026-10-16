# IBeam.Management.Services

Read the repository .agent/implementation-guide.md before changes.

Services own validation, management authorization, business rules and audit; custom operations must run through IServiceOperationExecutor. Never allow customer roles to bypass management context.

Do not leak secrets or raw customer payloads into DTOs, audit snapshots, exception messages or logs. Preserve API -> Services -> Repository boundaries. Build focused tests for authentication substitution, scope isolation, denied access and unsafe diagnostic content. Keep README registration and scope accurate. The separate IBeam.Admin.API host and its rename are deferred.

System-error reads use explicit dedicated/shared source trust and live Errors.Read checks before every provider query. Preserve UTC range/page/scan/request/time bounds and encrypted source/deployment/query-bound state with fixed expiry. Internal error metadata is not a public DTO. Never audit tokens, query/entity/return snapshots or untrusted Source/TraceId; safe public projection is a separate reader/API step.
