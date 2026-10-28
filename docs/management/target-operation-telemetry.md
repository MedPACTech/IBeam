# Safe target operation telemetry

IBM-0143 instruments the supported registered sender, compiled native Identity and
diagnostic transports, and connection discovery. Subscribe to `DiagnosticListener`
named `IBeam.Management.TargetOperations`; its `target.operation.completed` event
contains only `ManagementTargetCallTelemetry` from `IBeam.Management.Operations`.
The same named `Meter` exposes `management.target.calls` and
`management.target.duration` (milliseconds).

Completion records contain UTC time, duration clamped to one day, channel/kind,
canonical operation, optional app/tenant/resource/original-operation GUIDs, correlation,
fixed outcome/failure classes, optional HTTP status and whether a mutation was sent.
They have no URL, route/query/filter, credential reference, human profile, header,
request/response object, exception message/stack, OTP, key, assertion or token. Unknown
caller operation strings become `unregistered`, never a metric dimension or free text.
Metric dimensions are only bounded canonical operation/channel/outcome names; scoped
GUIDs appear only in completion records, not high-cardinality metric dimensions.

Correlation uses the server-owned audit context's validated 32-character lowercase
hex value when available. Invalid values become fresh GUIDs, not copied text. Native
Identity can resolve an audit-aware constructor without removing its original
constructor. Discovery before app binding has no trustworthy app/tenant ID, so these
fields are null. No ambient Activity baggage or parent tags are copied and no new
outgoing trace/header propagation is enabled.

Registered sender completion includes preflight validation/access failures, typed
response validation and safe read-only receipt recovery. Outcomes are `success`,
`rejected`, `timeout`, `network-failure`, `cancelled`, `unknown` or `unavailable`.
An ambiguous sent mutation stays `unknown`, with its original operation ID; timeout
and network classes remain separate even when public exceptions are deliberately
opaque. Caller cancellation differs from the internal attempt deadline. Validated
export/purge `unknown` results are marked explicitly. A sent flag is evidence of
possible commitment, not proof that work completed or was rolled back.

Legacy Identity/diagnostic records describe individual transport/envelope calls;
their subsequent DTO/business validation and lifecycle completion remain separate
service/audit concerns. A successful discovery or accepted processing receipt does
not mean a tenant export/purge, invitation acceptance or other business job completed.
No request/response payload is inspected by the telemetry emitter.

Registered dispatch and portal sends also use the descriptor's explicit allowlisted
audit action with verified scoped IDs. Existing generic dispatch auditing remains
for rejected/invalid boundary attempts. The Management audit sink reconstructs only
metadata, including the verified delegated actor and correlation, without entity
snapshots. Existing configured durable-audit failure policy is unchanged; audit
completion does not itself grant access or certify business completion.

Tracing is observational: listener, enablement and meter callback exceptions are
isolated independently. They cannot grant authority, change a return/exception,
retry a request or repeat a destructive effect. Observers must be nonblocking;
applications forwarding records to external systems should use their own bounded
queue/drop policy. Framework completion records do not implement an unbounded
in-memory history. Tracer failures may lose telemetry; they do not trigger retries.

Diagnostic sanitization controls only authorized diagnostic DTO fields. Even an
explicitly allowed Development/Testing unsanitized response keeps messages, details,
paths and provider text out of this telemetry and Management audit. There is no
configuration, browser claim or request toggle to weaken this redaction.

The focused tests cover a real signed custom route, native Identity/diagnostics and
discovery, all fixed outcome classes, original write correlation/recovery, metric
dimensions, malformed correlations, canaries, ambient baggage, and throwing observers.
No MPTPortal or consumer production telemetry configuration is changed by this module.
