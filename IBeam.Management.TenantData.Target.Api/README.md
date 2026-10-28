# IBeam.Management.TenantData.Target.Api

Register AddIBeamManagementTenantDataTargetApi(configuration, options) to explicitly enable tenant export with its required host-owned providers, then call MapIBeamManagementRegisteredOperations once for the host. The common signed dispatcher maps the compiled POST /api/management-tenant-data/tenants/{tenantId}/exports and read-only GET /api/management-tenant-data/tenants/{tenantId}/exports/{jobId}, plus its existing info/receipt endpoints. Default disabled registration adds no routes or capability.

Status contains safe coverage/progress only. Customer retrieval authorization and private artifact delivery belong to the consumer. The standard durable job service/worker and Azure Table journal are optional registrations; scoped support portal export endpoints remain a subsequent card. See docs/management/tenant-export.md.
