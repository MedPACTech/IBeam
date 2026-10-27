using System.Security.Claims;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services;
using IBeam.Management.Services.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Delegation;

internal sealed class ManagementCustomerIdentityCredentialGuard(IManagementApiCredentialRepository credentials,
    IOptions<ManagementDiagnosticsOptions> deployment, IOptions<ManagementCustomerIdentityTargetOptions> options,
    IEnumerable<IManagementCustomerIdentityCapability> modules, IServiceProvider provider)
{
    public async Task<Guid> RequireAsync(ClaimsPrincipal machine, string operation, CancellationToken ct)
    {
        var required = ManagementCustomerIdentityPermissionPolicy.RequiredFor(operation);
        var identity = ManagementPrincipalGuard.RequireIdentity(machine, ManagementSubjectTypes.Credential, operation);
        var configured = deployment.Value;
        var realm = options.Value;
        if (!realm.DedicatedApplicationRealm || realm.IdentityRealmId == Guid.Empty ||
            ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.ApplicationKey) != configured.ApplicationKey ||
            ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.Environment) != configured.Environment)
            throw new ManagementAccessDeniedException();
        var id = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.CredentialId);
        var row = await credentials.GetAsync(configured.ApplicationKey, configured.Environment, id, ct);
        if (row is null || row.CredentialId != id || row.ApplicationKey != configured.ApplicationKey || row.Environment != configured.Environment ||
            row.Status != "Active" || row.RevokedUtc.HasValue || row.ExpiresUtc <= DateTimeOffset.UtcNow ||
            row.Version != ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.CredentialVersion) ||
            !(provider.GetService<global::IBeam.Management.Operations.ManagementOperationRegistry>() ?? global::IBeam.Management.Operations.ManagementOperationRegistry.BuiltIn).ValidCapabilities(row.Capabilities)) throw new ManagementAccessDeniedException();
        foreach (var permission in required)
            if (!row.Capabilities.Contains(permission, StringComparer.Ordinal) || !realm.EnabledOperations.Contains(permission, StringComparer.Ordinal) ||
                !modules.Any(m => m.Operation == permission && m.RequiredServiceType.IsInterface && provider.GetService(m.RequiredServiceType) is not null))
                throw new ManagementAccessDeniedException();
        return id;
    }
}
