using IBeam.Management.Api;
using IBeam.Management.Licensing;

namespace IBeam.Management.API.Hosting;

/// <summary>Installs portal contracts only. No target handlers, workers, grants or demo data are created.</summary>
public static class LifecyclePortalRegistration
{
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("ManagementHost:LifecyclePortal:Enabled")) return;
        services.AddIBeamManagementRegisteredOperationsApi(configuration);
        services.AddIBeamManagementLicensePortal();
        services.AddIBeamManagementPortalLicenseApi(configuration);
        services.AddIBeamManagementSeatsPortal();
        services.AddIBeamManagementPortalSeatsApi(configuration);
        services.AddIBeamManagementPortalTenantExportApi(configuration);
        services.AddIBeamManagementPortalTenantPurgeApi(configuration);
        services.AddIBeamManagementPortalBulkInvitationRevokeApi(configuration);
    }
}
