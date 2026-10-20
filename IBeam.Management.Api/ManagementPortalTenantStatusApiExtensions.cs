using System.Reflection;
using IBeam.Management.Services.CustomerIdentity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Management.Api;
public static class ManagementPortalTenantStatusApiExtensions
{
    public static IServiceCollection AddIBeamManagementPortalTenantStatusApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementPortalIdentityOperationsApi(configuration); services.AddIBeamManagementPortalTenantAdministration(configuration);
        services.AddControllers().ConfigureApplicationPartManager(x => { if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature()); }); return services;
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        foreach (var type in new[] { typeof(ManagementPortalTenantStatusController), typeof(ManagementPortalMutationOperationsController) }.Select(x => x.GetTypeInfo()))
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
}
