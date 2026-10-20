using IBeam.Identity.Interfaces;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Tenants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services.Onboarding;

public static class ManagementEnterpriseOnboardingServiceExtensions
{
    public static IServiceCollection AddIBeamManagementEnterpriseOnboarding(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementTenantAdministration(configuration);
        services.AddOptions<ManagementEnterpriseOnboardingOptions>().Bind(configuration.GetSection(ManagementEnterpriseOnboardingOptions.SectionName))
            .Validate(x => x.IsValid(), "Onboarding requires explicit policy keys mapped to target-local administrator roles.").ValidateOnStart();
        services.TryAddScoped<IExternalTenantInviteService>(p => p.GetRequiredService<ITenantInviteService>() is IExternalTenantInviteService service && service.SupportsDurableCreation
            ? service : throw new InvalidOperationException("Onboarding requires a durable external Identity invitation adapter."));
        services.TryAddScoped<IManagementCustomerOnboardingPolicy, ConfiguredManagementCustomerOnboardingPolicy>();
        services.TryAddScoped<IManagementCustomerOnboardingPolicyCatalog, ManagementCustomerOnboardingPolicyCatalog>();
        services.TryAddScoped<IManagementEnterpriseOnboardingService, ManagementEnterpriseOnboardingService>();
        services.TryAddScoped<IManagementEnterpriseOnboardingCommandService, ManagementEnterpriseOnboardingCommandService>();
        return services;
    }
}
