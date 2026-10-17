# Diagnostic source isolation and historical data

The management errors/logs surface is deployment-wide operational support. An app assignment authorizes that application/environment's diagnostics across its customer tenants. Existing error rows without customer tenant identity cannot support per-customer-tenant authorization. Do not describe an app grant as a customer-tenant restriction or filter those records by browser-supplied tenant IDs. Customer data tables and business audit history have no route through this surface.

## Choose an attributable source

Configure Errors and Logs independently under IBeam:Management:Diagnostics. Omitted/unspecified source mode fails startup; no automatic fallback to broad historical reads exists.

| Source mode | Required confirmation | Historical rows that can be shown |
| --- | --- | --- |
| DedicatedDeployment | DedicatedSourceConfirmed=true | Truly untagged rows from a table proven to belong exclusively to the configured application/environment, and exactly matching tagged rows. |
| TaggedShared | TrustedTagsConfirmed=true | Only rows with exact, correctly typed ManagementApplicationKey and ManagementEnvironment matching the server's deployment configuration. |

Partial tags, malformed types, blank tags, foreign apps and foreign environments fail closed in either mode. Confirmation is a deployment assertion backed by storage/writer ownership; a boolean does not establish provenance. Never mark a mixed history as dedicated simply to make old rows visible. SourceBinding incorporates actual provider/table identity and is authenticated into every ID and continuation. Moving a reader to another source invalidates its tokens.

Trusted tags must come from the writer's deployment configuration, outside client control. Do not derive them from request headers, URLs, diagnostic messages, customer user claims or an administrator's guess. Readers recheck source/deployment predicates after the provider response. A filter or authenticated token cannot widen an attributed source. Existing writers without such tags require an explicitly dedicated source, a trusted tagging adapter, or an operator-led migration before shared exposure.

## Operational rows and safe projections

System errors use exact ERR|yyyyMMdd partitions and a bounded UTC interval. System logs use exact SYSTEM|DAY|yyyyMMdd partitions plus Category=System and reject rollups and tenant partitions. EntityChange, SelectRollup, tenant business events and entity snapshots remain excluded even when stored in the same physical table or marked with matching deployment tags. List and detail enforce the same rules. Unknown query fields such as tenantId, applicationKey, environment and category cannot replace server configuration or widen these filters.

In the default sanitized mode, repositories select approved metadata columns only. Error messages, exception/stack/request/response/path content and log Message/Detail are not selected. Business BeforeJson/AfterJson/OriginalJson/TransformedJson, actor/device/IP and entity data are excluded. Projections use fixed summaries, normalized levels/source catalogs and keyed correlation pseudonyms; IDs hide physical keys. The portal projects typed target DTOs again. IBM-0097 adds explicit non-Production raw-field settings without relaxing source/category/tenant/audit restrictions; see [sanitization controls](diagnostic-sanitization.md).

IDs and cursors authenticate purpose, deployment, source, range and applicable log source/level/trace filters. Continuations freeze the initial query and expiry. Cross-app, cross-source and cross-operation replay cannot authorize a point read; query changes fail safely. The portal adds human organization/user/assignment/connection binding and independently authorizes each read, so a saved selector or link never grants access.

## Storage permissions and recovery

Provision diagnostic tables using deployment tooling. Runtime reader credentials need read-only access to the intended diagnostic tables, separately from Identity, app/assignment metadata, credential storage and append-only management audit. Do not use an account-wide write key as the diagnostic reader. Restrict Azure identity roles or table-specific read SAS as appropriate. Read paths never create tables, rewrite rows, backfill tags or update ETags. Supply separate credentials for migration/retention tools and do not expose them to support sessions.

For existing installations:

1. Inventory the writers and historical provenance of each table, including earlier applications/environments using it. Keep shared sources unavailable while provenance is uncertain.
2. Prefer a new, dedicated diagnostic table per app/environment when existing mixed history cannot be reliably attributed. Reconfigure the writer and reader together through normal deployment controls; preserve the old archive under operator access.
3. If trustworthy external evidence can assign historical rows, use a separately reviewed migration to copy verified records into an attributable source or add exact server-owned tags. Preserve the original archive and record migration evidence. Do not infer attribution from payload text or fabricate customer tenant IDs.
4. Enable TaggedShared only after every writer supplies reliable immutable deployment tags. Untagged historical rows remain unavailable; recapture equivalent operational events from trusted writers where necessary.
5. Run a read-only verification using one assigned support user and separate target credentials per app. Verify a foreign app/environment, business audit row and untagged shared record remain inaccessible, and unchanged rows retain ETags.
6. If a source was misclassified, disable/disconnect the management app, correct source configuration and rotate protection keys to expire outstanding links. Re-enable only after proving source ownership. Existing secret rotation/app version changes invalidate portal-bound IDs and cursors.

Private-emulator verification is in DiagnosticIsolationIntegrationTests and the error/log repository integration tests: mixed tags, dedicated legacy records, exact predicates/selected columns, read-only SAS, no runtime provisioning/mutation, unchanged ETags, raw canaries on success/failure/audit and token replay across sources/apps. These tests use isolated local storage, never production records.
