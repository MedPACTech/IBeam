# IBeam.Management.Repositories.AzureTable

Read the root .agent/implementation-guide.md. Repositories persist one entity, do not enforce business authorization, and never call APIs/services/other repositories. Reference Management contracts only, not Identity or services.
Never create tables on runtime read paths. Use point keys bound to application/environment. Conditional writes must reject stale versions so a rotate cannot undo a concurrent revoke. Do not serialize credential entities into responses/audit snapshots; never persist raw keys. Keep provider fixture tests and package documentation current.
