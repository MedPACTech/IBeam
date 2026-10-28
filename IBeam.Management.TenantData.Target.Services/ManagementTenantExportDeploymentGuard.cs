using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.TenantData;

namespace IBeam.Management.TenantData.Target.Services;

internal static class ManagementTenantExportDeploymentGuard
{
    public static void Require(ManagementTenantExportExecution execution, ManagementDiagnosticsOptions deployment, ManagementOperationTargetOptions target, bool requireWorkAuthority = true)
    {
        ManagementTenantExportJobService.Validate(execution);
        if (execution.ApplicationKey != deployment.ApplicationKey || execution.Environment != deployment.Environment || execution.Authority.RealmId != target.RealmId ||
            !target.DedicatedApplicationRealm || requireWorkAuthority && (!target.EnabledOperations.Contains(ManagementTenantDataOperations.Export, StringComparer.Ordinal) ||
            !target.TrustedPortals.Any(x => x.OrganizationId == execution.Authority.OrganizationId && x.ApplicationId == execution.Authority.ApplicationId)))
            throw new ManagementAccessDeniedException();
    }
}
