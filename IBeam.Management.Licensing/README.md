# IBeam.Management.Licensing

Optional typed Management licence adapters over native IBeam Licensing. Core Management has no Licensing dependency. Registration defaults off; existing `tenants.status` or `memberships.manage` grants never become commercial permissions.

See [licence status management](https://github.com/MedPACTech/IBeam/blob/development/docs/management/license-status-management.md) for explicit registration, durable conditional provider requirements, safe retry/lookup behavior and the dependency on registered-operation HTTP dispatch.
