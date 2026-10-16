# IBeam.Management.Repositories.AzureTable

Read the root .agent/implementation-guide.md. Repositories persist one entity, do not enforce business authorization, and never call APIs/services/other repositories. Reference Management contracts only, not Identity or services.
Never create tables on runtime read paths. Use point keys bound to application/environment. Conditional writes must reject stale versions so a rotate cannot undo a concurrent revoke. Do not serialize credential entities into responses/audit snapshots; never persist raw keys. Keep provider fixture tests and package documentation current.

SystemErrors reads require exact date-partition equality and bounded single-page queries; select metadata only. Never fetch raw error/request payloads or auto-create schema. Keep diagnostic storage explicit and independent of Identity prefixes. Repositories do not decide attribution trust; preserve malformed tags as invalid for the service's source policy.

Application uniqueness uses an atomic same-partition primary/index creation. Always constrain organization in list/item operations. Application connection rows hold only scoped secret references and verified metadata; the app ETag-protected active credential pointer is authoritative. Never assume cross-table atomic writes or create runtime tables.
