# IBeam.Management.Services

Service boundary for management operations. Register `services.AddIBeamManagementServices()` and supply a trusted scoped `IManagementPrincipalProvider`, or use an API module which supplies the HTTP principal provider.

`ManagementIdentityService.GetCurrentAsync` requires a named authenticated human, one nonempty management organization ID and `management.identity.read`. `ManagementDiagnosticsInfoService.GetAsync` requires a management credential ID, exact application/environment binding and `management.diagnostics.info`.

Both methods execute through `IServiceOperationExecutor`. They use dedicated management guards inside the operation rather than generic customer-tenant owner/admin checks (`Permission=false` explicitly disables that unrelated tenant-role evaluator). Removing the optional generic authorizer cannot bypass these guards. The same operation names feed audit execution. Durable management audit/actor correlation is a later feature; production hosts must configure their durable audit sink and policy.

Configure `ManagementDiagnosticsOptions` (application key and environment) for a target info service. The target API module binds and validates these settings at startup.

No errors/logs, database access, user provisioning, API-key verification, login/MFA endpoints or outbound application client are implemented by this foundation. These are separately scoped Hubbsly cards.
