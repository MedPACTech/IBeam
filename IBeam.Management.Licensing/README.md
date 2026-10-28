# IBeam.Management.Licensing

Optional typed Management licence adapters over native IBeam Licensing. Core Management has no Licensing dependency. Registration defaults off; existing `tenants.status` or `memberships.manage` grants never become commercial permissions.

IBM-0128 adds independent optional seat read/assign/unassign adapters with customer subject policy, native capacity checks, shared licence revision fencing and immutable receipts. See [seat management](https://github.com/MedPACTech/IBeam/blob/development/docs/management/seat-management.md). Native subject/profile/provider fields are excluded from Management responses and audit.

See [licence status management](https://github.com/MedPACTech/IBeam/blob/development/docs/management/license-status-management.md) for explicit registration, durable conditional provider requirements, safe retry/lookup behavior and the dependency on registered-operation HTTP dispatch.
