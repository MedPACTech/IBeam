# IBeam.Management.Repositories.AzureTable

Read the root .agent/implementation-guide.md. Repositories persist one entity, do not enforce business authorization, and never call APIs/services/other repositories. Reference Management contracts only, not Identity or services.
Never create tables on runtime read paths. Use point keys bound to application/environment. Conditional writes must reject stale versions so a rotate cannot undo a concurrent revoke. Do not serialize credential entities into responses/audit snapshots; never persist raw keys. Keep provider fixture tests and package documentation current.

SystemErrors reads require exact date-partition equality and bounded single-page queries; select metadata only. Never fetch raw error/request payloads or auto-create schema. Keep diagnostic storage explicit and independent of Identity prefixes. Repositories do not decide attribution trust; preserve malformed tags as invalid for the service's source policy. SystemLogs reads are SYSTEM|DAY partitions with Category System only and never select Message/Detail, actor/device/IP, tenant/entity or *Json columns.

Application uniqueness uses an atomic same-partition primary/index creation. Always constrain organization in list/item operations. Application connection rows hold only scoped secret references and verified metadata; the app ETag-protected active credential pointer is authoritative. Never assume cross-table atomic writes or create runtime tables.

ManagementApplicationAccess has organization/application partitions and unique user rows. Atomic creates and explicit ETags enforce duplicate/update/revoke conflicts. Retain revocation metadata, never use wildcard updates or create runtime tables. Invitation redemption uses an atomic marker in the supplied existing management Identity TenantInvites table and never stores raw tokens.

ManagementAudit is append-only through IManagementAuditRepository; do not add portal list/update/delete or automatic retention deletion. ExpiresUtc is metadata, not provider TTL; a separate operator performs cleanup. Diagnostics remain independent read-only TableClients with full physical source identity; a management Identity prefix never grants diagnostic attribution.
