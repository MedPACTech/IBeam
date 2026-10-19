using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services;

public static class ManagementCustomerIdentityTargetServiceExtensions
{
    public static IServiceCollection AddIBeamManagementCustomerIdentityTargetServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementCredentialValidation(configuration);
        services.AddOptions<ManagementCustomerIdentityTargetOptions>()
            .Bind(configuration.GetSection(ManagementCustomerIdentityTargetOptions.SectionName))
            .Validate(x => x.IdentityRealmId != Guid.Empty && x.DedicatedApplicationRealm,
                "An explicit non-empty customer Identity realm and DedicatedApplicationRealm=true are required. Shared app realms need a trusted scope adapter before support.")
            .ValidateOnStart();
        services.TryAddScoped<IManagementCustomerIdentityInfoService, ManagementCustomerIdentityInfoService>();
        return services;
    }
}
