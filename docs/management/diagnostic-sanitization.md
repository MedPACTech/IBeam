# Configurable diagnostic sanitization (IBM-0097)

Errors and logs each have a host-owned setting:

- IBeam:Management:Diagnostics:Errors:Sanitization:Enabled
- IBeam:Management:Diagnostics:Logs:Sanitization:Enabled

Missing, empty, whitespace, null or true keeps sanitization enabled. Only explicit false enables raw fields. Invalid boolean values fail configuration binding. This setting is not accepted from a request query, header, body, claim, app registration or portal preference. Change deployment configuration and restart the target; it is not a per-request switch.

A nonsecret partial example for a testing target is:

```json
{
  "IBeam": {
    "Management": {
      "Diagnostics": {
        "ApplicationKey": "qurvia",
        "Environment": "Testing",
        "Errors": { "Sanitization": { "Enabled": false } },
        "Logs": { "Sanitization": { "Enabled": true } }
      }
    }
  }
}
```

This example omits the separately required source isolation, storage, protection keys and authentication configuration; see [deployment setup](deployment-and-handoff.md). Environment variables use IBeam__Management__Diagnostics__Errors__Sanitization__Enabled and the equivalent Logs name.

## Allowed environments and startup behavior

Raw fields require an explicit non-Production host and registered target deployment. Allowed ASP.NET host environments are Development, Testing and Staging. Allowed management deployment environments are Development, Dev, Testing, Test, Staging, QA and Local (case insensitive). Production/prod, absent/unknown contexts and other names cannot disable sanitization. The target refuses startup for either unsafe module; a sanitized installation remains compatible with existing environment names. Direct readers/projections repeat the guard, including calls outside HTTP. Projection calls recheck current settings; changing deployment context cannot retain an earlier raw projection.

Each opted-in module logs one startup warning identifying only the module. The warning contains no diagnostic contents, API keys or storage values. The Production restriction follows this card's approved policy; it is not a browser authorization grant.

## Response contract and portal behavior

The existing safe fields and summaries remain unchanged. In sanitized mode the unsanitized property is omitted, and raw storage columns are not requested. With sanitization disabled an additional, explicitly named unsanitized object appears in both list and detail:

| Module | Fields inside unsanitized |
| --- | --- |
| Errors | source, traceId, message, exception, path, method |
| Logs | source, level, traceId, message, detail |

These are stored diagnostic text and can contain data captured by the application's logger. No customer entity tables are queried. Missing historical text remains null. The unsanitized object is the machine-readable marker a portal UI must use to distinguish raw diagnostic text from the fixed safe summary. No frontend is supplied by this framework card.

GET /api/management-diagnostics/info reports unsanitizedCapabilities only when an installed module exposes raw text. Values are the corresponding management.diagnostics.errors.read and/or management.diagnostics.logs.read operations. This declaration is a subset of installed capabilities and grants no permissions. It never includes info or unknown operations. Existing sanitized /info responses retain their shape. Production/unknown raw declarations and malformed/duplicate mode capabilities are rejected during connection validation and portal reads.

The portal verifies current target info on each diagnostic request and preserves raw fields only for the advertised operation and registered non-Production deployment. It projects the safe fields again and copies only the typed raw object. Unadvertised raw objects are discarded. Unknown response fields and raw audit/actor/tenant/entity columns are not forwarded. Invalid raw field types, fields larger than the Table string bound (32768 characters each), invalid envelopes or body-size limits fail safely. Client transport and portal orchestration both enforce this behavior, including host-supplied client adapters at the service boundary.

Portal target response limits remain unchanged (256 KiB by default, configurable up to 1 MiB). Use a smaller pageSize for large raw diagnostic entries; the client never silently raises response bounds. Raw output does not add an API that lets the browser choose a URL, key, mode or secret reference.

## Source isolation, tokens, audit and storage

Disabling sanitization relaxes fields only. All live human/machine grants, app/environment/source checks, date/range/page/scan/request/deadline limits and source/level/trace filters remain enforced. Logs.Read and Errors.Read stay independent. Operational logs still require SYSTEM|DAY partitions and Category=System and exclude tenant partitions, EntityChange, SelectRollup and rollups. TaggedShared still rejects untagged, partial, malformed or foreign tags; a raw switch cannot attribute ambiguous history. See [historical/source policy](diagnostic-source-isolation.md).

Diagnostic repositories select Message/Exception/Path/Method for errors, or Message/Detail for logs, only through an explicit service-owned raw query/detail operation. They never select audit JSON, actor, device, IP, user-agent or customer entity columns. Diagnostic storage remains read-only; no runtime provisioning/backfill/mutation was added. Custom repository implementations must honor IncludeUnsanitized on partition queries and implement GetUnsanitizedAsync for raw detail; the default detail method rejects unsupported adapters rather than falling back to broader storage reads. Existing metadata-only adapters and constructor signatures continue to work with the default safe setting.

Target IDs/cursors bind the effective sanitization mode as well as existing source/deployment/query scope. Portal IDs/cursors authenticate that mode in their encrypted state and compare it against freshly verified target info before data retrieval. Switching modes invalidates previously issued links in either direction; retrieve a fresh page. This upgrade also invalidates older tokens without a mode binding. No key rotation is required merely to enable the setting.

Management audit remains metadata-only in both modes: no diagnostic object, raw field, query/response snapshot, arbitrary actor header or exception/provider payload is attached. Storage records' raw properties are ignored during JSON serialization, and their ToString output is redacted. Public raw DTO objects also redact ToString. Standard ASP.NET HTTP body logging is disabled on target and portal diagnostic controllers; host-owned ingress/APM/custom logging must likewise omit diagnostic response bodies and authentication headers. Existing server-generated portal/target audit correlation remains available without copying a diagnostic trace or payload into the audit table.

Tests cover configuration defaults and independent switches, real HTTP list/detail and capability advertisement, Production/unknown refusal, direct projections, request override attempts, target/portal mode-bound replay, malformed mode metadata, raw-field bounds, safe success/failure audit, and real private Table reads with explicit columns/read-only SAS/unchanged ETags. The generated-package smoke host uses two testing targets with opposite raw module modes and verifies the current portal preserves those modes while audit remains free of payload canaries.
