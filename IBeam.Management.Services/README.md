# IBeam.Management.Services

Service boundary for management operations. Register `services.AddIBeamManagementServices()` and supply a trusted scoped `IManagementPrincipalProvider`, or use an API module which supplies the HTTP principal provider.

`ManagementIdentityService.GetCurrentAsync` requires a named authenticated human, one nonempty management organization ID and `management.identity.read`. `ManagementDiagnosticsInfoService.GetAsync` requires a management credential ID, exact application/environment binding and `management.diagnostics.info`.

Both methods execute through `IServiceOperationExecutor`. They use dedicated management guards inside the operation rather than generic customer-tenant owner/admin checks (`Permission=false` explicitly disables that unrelated tenant-role evaluator). Removing the optional generic authorizer cannot bypass these guards. The same operation names feed audit execution. Durable management audit/actor correlation is a later feature; production hosts must configure their durable audit sink and policy.

Configure `ManagementDiagnosticsOptions` (application key and environment) for a target info service. The target API module binds and validates these settings at startup.

Errors/logs reading, user/application assignments and the outbound application client remain later cards. Optional login/MFA support is in IBeam.Management.Identity.Services/Api; these general services do not depend on Identity.

## Deployment credentials

Register AddIBeamManagementCredentialValidation(configuration) for runtime read-only verification, and supply IManagementApiCredentialRepository (for example IBeam.Management.Repositories.AzureTable). IBeam:Management:Diagnostics:ApplicationKey and Environment bind every operation to the local deployment. Registration does not install Identity or change customer authentication.

Keys are ibmgmt_v1.<credentialId in N format>.<32 random bytes encoded base64url>. Only a domain-separated, versioned SHA-256 hash is stored and checked with CryptographicOperations.FixedTimeEquals. This fast hash is for high-entropy generated credentials, never human passwords. Legacy tenant keys, JWTs and different format versions are rejected. Validation rereads current status, expiry, deployment and capabilities without caching. Infrastructure failures propagate and fail closed.

Capabilities are explicit: management.diagnostics.info, management.diagnostics.errors.read and management.diagnostics.logs.read. No wildcard, owner/admin shortcut or implied Errors/Logs grants exist. Defining a capability does not expose its diagnostic endpoint; readers are later features.

Trusted provisioning tooling alone calls AddIBeamManagementCredentialProvisioning(configuration). Set IBeam:Management:Credentials:Provisioning:Enabled=true only there, and replace the default-deny IManagementCredentialOperatorProvider with trusted host/operator authorization. RequireAuthorizedOperatorAsync must validate an operator independently of customer/portal roles and return a nonempty stable operator ID. No public HTTP issue/rotate/revoke route is installed. Runtime hosts register validation only and use read-only storage permissions.

Resolve IManagementCredentialProvisioningService and call IssueAsync(explicitCapabilities, expiry), RotateAsync(credentialId), or RevokeAsync(credentialId). MaximumLifetimeDays defaults to 90 and accepts 1-365. Rotation retains ID, capabilities and original expiry; it never reactivates revoked/expired credentials. Conditional persistence rejects concurrent changes instead of retrying a stale rotation over a revocation. Issue/rotate return a ManagementIssuedCredential whose ApiKey property must be handed directly to a secret store. That property and stored hash are excluded from JSON and redacted from ToString. Audit operations contain metadata only; never add request/entity/return snapshots. Operator IDs are persisted with lifecycle timestamps. Durable audit delivery/correlation remains the later audit feature.

## Target authentication and operation access

The optional Diagnostics API adapter registers ManagementCredentialAuthenticationService and ManagementDiagnosticsAccessService. The former creates only a machine principal after key/network validation; the latter requires the exact persisted operation grant and rechecks status, expiry, deployment and record version before every diagnostic query. The original three-argument info-service constructor remains available for BYO-auth; DI selects the live four-argument guard when installed by the credential adapter. Keep future error/log readers behind IManagementDiagnosticsAccessService.RequireAsync inside their IServiceOperationExecutor operation.

ManagementCredentialNetworkPolicy accepts configured CIDR ranges and a server-observed IPAddress; HTTP/proxy trust belongs to the host. No arbitrary request headers, customer roles or implied wildcard grants are accepted. Authentication/authorization audit records include credential ID, operation and safe request correlation, with no secret/hash snapshots. Existing host audit context providers are preserved.
