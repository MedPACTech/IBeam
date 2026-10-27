using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.CustomerIdentity;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services;

public sealed class ManagementCustomerIdentityInfoService(IManagementPrincipalProvider principals,
    IManagementApiCredentialRepository credentials, IOptions<ManagementDiagnosticsOptions> deployment,
    IOptions<ManagementCustomerIdentityTargetOptions> options, IEnumerable<IManagementCustomerIdentityCapability> modules,
    IServiceProvider provider, IServiceOperationExecutor operations) : IManagementCustomerIdentityInfoService
{
    public Task<ManagementCustomerIdentityInfo> GetAsync(CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            token.ThrowIfCancellationRequested();
            var identity = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Credential, ManagementCustomerIdentityOperations.Info);
            var configured = deployment.Value;
            if (ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.ApplicationKey) != configured.ApplicationKey ||
                ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.Environment) != configured.Environment)
                throw new ManagementAccessDeniedException();
            var id = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.CredentialId);
            var row = await credentials.GetAsync(configured.ApplicationKey, configured.Environment, id, token);
            if (row is null || row.CredentialId != id || row.ApplicationKey != configured.ApplicationKey || row.Environment != configured.Environment ||
                row.Status != "Active" || row.RevokedUtc.HasValue || row.ExpiresUtc <= DateTimeOffset.UtcNow ||
                row.Version != ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.CredentialVersion) ||
                !ManagementCredentialPolicy.ValidCapabilities(row.Capabilities) ||
                !row.Capabilities.Contains(ManagementCustomerIdentityOperations.Info, StringComparer.Ordinal))
                throw new ManagementAccessDeniedException();
            var realm = options.Value;
            if (!realm.DedicatedApplicationRealm || realm.IdentityRealmId == Guid.Empty) throw new ManagementAccessDeniedException();
            var capabilities = new List<string> { ManagementCustomerIdentityOperations.Info };
            foreach (var module in modules)
            {
                if (module.Operation == ManagementCustomerIdentityOperations.Info ||
                    !ManagementCustomerIdentityOperations.All.Contains(module.Operation, StringComparer.Ordinal) ||
                    module.RequiredServiceType is null || !module.RequiredServiceType.IsInterface)
                    throw new ManagementConnectionInvalidException();
                // A module cannot report a missing adapter or an operation this credential does not hold.
                if (ManagementCustomerIdentityPermissionPolicy.RequiredFor(module.Operation).All(required =>
                        row.Capabilities.Contains(required, StringComparer.Ordinal) && realm.EnabledOperations.Contains(required, StringComparer.Ordinal)) &&
                    provider.GetService(module.RequiredServiceType) is not null) capabilities.Add(module.Operation);
            }
            capabilities = capabilities.Where(x => x == ManagementCustomerIdentityOperations.Info ||
                ManagementCustomerIdentityPermissionPolicy.RequiredFor(x).All(required => capabilities.Contains(required, StringComparer.Ordinal))).ToList();
            var result = new ManagementCustomerIdentityInfo(configured.ApplicationKey, configured.Environment,
                realm.IdentityRealmId, ManagementCustomerIdentityProtocol.DedicatedRealm, ManagementCustomerIdentityProtocol.Version,
                Array.AsReadOnly(capabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())) {
                    OperationReceiptProtocolVersion = provider.GetService<IManagementIdentityReceiptProtocol>()?.Version == 1 &&
                        provider.GetService<IManagementIdentityOperationLookupService>()?.SupportsLookup == true ? 1 : 0 };
            ManagementCustomerIdentityProtocol.Validate(result);
            return result;
        }, new ServiceOperationExecutionOptions { OperationName = ManagementCustomerIdentityOperations.Info,
            AuditAction = ManagementCustomerIdentityOperations.Info, EntityName = "ManagementCustomerIdentityInfo",
            PermissionEnabled = false, AuditEnabled = true }, ct);
}
