using IBeam.Identity.Interfaces;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services.Tenants;
public static class ManagementTenantStatusExtensions
{
    public static IServiceCollection AddIBeamManagementTenantStatus(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementTenantAdministration(configuration);
        services.TryAddScoped<IManagementTenantStatusService, StatusService>(); return services;
    }
    private sealed class StatusService(IManagementTenantAdministrationService service, IIdentityTenantAdministrationService identities,
        IIdentityTenantAccessEnforcement enforcement) : IManagementTenantStatusService
    {
        public Task<ManagementTenantMutationResult> SetStatusAsync(Guid tenant, bool active, string version, string key, CancellationToken ct = default)
        {
            if (enforcement.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(enforcement.Mode) || identities is not IIdentityTenantStatusAdministrationService { SupportsConditionalStatus: true })
                throw new ManagementAccessDeniedException();
            return service.SetStatusAsync(tenant, active, version, key, ct);
        }
    }
}
