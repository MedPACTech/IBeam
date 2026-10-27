using System.Reflection;
using IBeam.Management.Services.CustomerIdentity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Management.Api;

public static class ManagementCustomerIdentityApiExtensions
{
    /// <summary>Requires the support host's AddIBeamManagementApi authentication and storage registrations.</summary>
    public static IServiceCollection AddIBeamManagementCustomerIdentityApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementConnectionsApi(configuration);
        services.AddIBeamManagementCustomerIdentity(configuration);
        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            if (!manager.FeatureProviders.OfType<CustomerIdentityFeature>().Any()) manager.FeatureProviders.Add(new CustomerIdentityFeature());
        });
        return services;
    }
    private sealed class CustomerIdentityFeature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var type = typeof(ManagementCustomerIdentityController).GetTypeInfo();
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type);
        }
    }
}
